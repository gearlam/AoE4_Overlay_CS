using AoE4OverlayCS.Models;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AoE4OverlayCS.Services
{
    public class ApiCheckerService
    {
        private readonly SettingsService _settings;
        private readonly HttpClient _http;
        private CancellationTokenSource? _cts;
        private DateTime _lastMatchTime = DateTime.MinValue;

        public event Action<JObject>? OnNewGame;
        public event Action<string>? OnError;

        /// <summary>最近一次 FindPlayer 失败时的错误信息（null 表示请求正常完成但未找到玩家）。</summary>
        public string? LastError { get; private set; }

        /// <summary>单个地址的 TCP 连接超时（需覆盖 SYN 重传，部分网络存在丢包）。</summary>
        private static readonly TimeSpan ConnectAttemptTimeout = TimeSpan.FromSeconds(10);

        public ApiCheckerService(SettingsService settings)
        {
            _settings = settings;
            _http = new HttpClient(CreateHandler());
            // 整体请求预算：需覆盖连接层最坏情况（IPv4 竞速 10s + IPv6 回退 10s + DNS）
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// 创建 IPv4 优先的连接处理器。部分网络环境下存在两类问题：
        /// 1) IPv6 路由不通（连接被黑洞直到超时）；2) 同一域名的多个 IPv4 地址中个别地址不可达，
        /// 且 DNS 返回顺序会轮换，串行尝试会被单个黑洞地址拖垮整个请求。
        /// 因此对同族地址并发竞速（取最快成功的连接），IPv4 全部失败后再回退 IPv6。
        /// </summary>
        private static SocketsHttpHandler CreateHandler()
        {
            return new SocketsHttpHandler
            {
                // 外层连接预算：覆盖 DNS + IPv4 竞速 + IPv6 回退，避免过早取消后续尝试
                ConnectTimeout = TimeSpan.FromSeconds(25),
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                    if (addresses.Length == 0)
                        throw new IOException($"DNS 未解析到地址：{ctx.DnsEndPoint.Host}");

                    var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                    var others = addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork).ToList();

                    if (ipv4.Count > 0)
                    {
                        var stream = await TryConnectRaceAsync(ipv4, ctx.DnsEndPoint.Port, ct);
                        if (stream != null) return stream;
                    }

                    var fallback = await TryConnectRaceAsync(others, ctx.DnsEndPoint.Port, ct);
                    if (fallback != null) return fallback;

                    throw new IOException($"无法连接到 {ctx.DnsEndPoint.Host}:{ctx.DnsEndPoint.Port}");
                }
            };
        }

        /// <summary>
        /// 同族地址并发竞速：返回最快成功的连接，全部失败返回 null。
        /// 迟到成功的连接会被释放，避免 socket 泄漏。
        /// </summary>
        private static async Task<Stream?> TryConnectRaceAsync(List<IPAddress> addresses, int port, CancellationToken ct)
        {
            if (addresses.Count == 0) return null;

            using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = addresses.Select(a => ConnectOneAsync(a, port, raceCts.Token)).ToList();

            try
            {
                while (pending.Count > 0)
                {
                    var finished = await Task.WhenAny(pending);
                    pending.Remove(finished);
                    try
                    {
                        var stream = await finished;
                        raceCts.Cancel();   // 通知其余竞速者停止
                        return stream;
                    }
                    catch (OperationCanceledException) when (raceCts.IsCancellationRequested)
                    {
                        throw;              // 外层取消（应用退出/整体超时）
                    }
                    catch
                    {
                        // 该地址失败，继续等待其余竞速者
                    }
                }
            }
            finally
            {
                // 仍在竞速的任务：若稍后成功则释放其连接；异常一并观察，避免未处理异常
                foreach (var task in pending)
                {
                    _ = task.ContinueWith(
                        t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                            else _ = t.Exception;
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            return null;
        }

        private static async Task<Stream> ConnectOneAsync(IPAddress address, int port, CancellationToken ct)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(ConnectAttemptTimeout);
                await socket.ConnectAsync(new IPEndPoint(address, port), attemptCts.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_settings.Current.ProfileId))
                    {
                        var data = await CheckLastGame();
                        if (data != null)
                        {
                            OnNewGame?.Invoke(data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex.Message);
                }

                try { await Task.Delay(_settings.Current.Interval * 1000, token); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task<JObject?> CheckLastGame()
        {
            var json = await GetLastGame();
            if (json == null) return null;

            var startedAtStr = json["started_at"]?.ToString();
            if (startedAtStr != null && DateTime.TryParse(startedAtStr, out var startedAt))
            {
                // Simple logic: if new timestamp > old timestamp, it's new
                // In production, might need more robust ongoing check
                if (startedAt > _lastMatchTime)
                {
                    _lastMatchTime = startedAt;
                    return json;
                }
            }
            return null;
        }

        public async Task<JObject?> GetLastGame()
        {
            if (string.IsNullOrEmpty(_settings.Current.ProfileId)) return null;

            try
            {
                var url = $"https://aoe4world.com/api/v0/players/{_settings.Current.ProfileId}/games/last";
                var resp = await _http.GetStringAsync(url);
                var json = JObject.Parse(resp);

                if (json.ContainsKey("error")) return null;
                return json;
            }
            catch
            {
                return null;
            }
        }
        
        public async Task<JArray?> GetMatchHistory(int limit = 10)
        {
            if (string.IsNullOrEmpty(_settings.Current.ProfileId)) return null;
            try
            {
                var url = $"https://aoe4world.com/api/v0/players/{_settings.Current.ProfileId}/games?limit={limit}";
                var resp = await _http.GetStringAsync(url);
                var json = JObject.Parse(resp);
                return json["games"] as JArray;
            }
            catch { return null; }
        }

        /// <summary>获取当前玩家的完整档案（含 modes 段位数据）。</summary>
        public async Task<JObject?> GetPlayerProfile()
        {
            if (string.IsNullOrEmpty(_settings.Current.ProfileId)) return null;
            try
            {
                var url = $"https://aoe4world.com/api/v0/players/{_settings.Current.ProfileId}";
                var resp = await _http.GetStringAsync(url);
                var json = JObject.Parse(resp);
                if (json.ContainsKey("error")) return null;
                return json;
            }
            catch { return null; }
        }

        public async Task<JObject?> FindPlayer(string query)
        {
             try 
             {
                LastError = null;

                // Try profile ID first
                if (long.TryParse(query, out _))
                {
                    try {
                        var url = $"https://aoe4world.com/api/v0/players/{query}";
                        var resp = await _http.GetStringAsync(url);
                        return JObject.Parse(resp);
                    } catch {}
                }

                // Search by query
                var searchUrl = $"https://aoe4world.com/api/v0/players/search?query={query}";
                var searchResp = await _http.GetStringAsync(searchUrl);
                var searchJson = JObject.Parse(searchResp);
                var players = searchJson["players"] as JArray;
                if (players != null && players.Count > 0)
                {
                    return players[0] as JObject;
                }
             }
             catch (Exception ex)
             {
                 LastError = ex.Message;
             }
             return null;
        }
    }
}
