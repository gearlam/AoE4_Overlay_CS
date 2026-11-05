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
        private const string Aoe2Api = "https://aoe-api.worldsedgelink.com/community/leaderboard/";

        private readonly object _audienceLock = new();
        private bool _audiencePresent = true;
        private TaskCompletionSource<bool>? _audienceWake;

        private DateTime _lastMatchTime = DateTime.MinValue;
        private long _lastAoe2MatchId;
        private JObject? _aoe2History;
        private string? _aoe2HistoryProfileId;
        private DateTime _aoe2HistoryAt = DateTime.MinValue;

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

        public void ResetMatchCursor()
        {
            _lastMatchTime = DateTime.MinValue;
            _lastAoe2MatchId = 0;
            _aoe2History = null;
            _aoe2HistoryProfileId = null;
        }

        /// <summary>
        /// 覆盖层、主窗口、WebSocket 都没人看时传 false，轮询先等着。
        /// 从没人看变回有人看时打断等待，马上再拉一次。
        /// </summary>
        public void SetAudiencePresent(bool present)
        {
            TaskCompletionSource<bool>? wake = null;
            lock (_audienceLock)
            {
                if (_audiencePresent == present) return;
                _audiencePresent = present;
                if (!present) return;
                wake = _audienceWake;
                _audienceWake = null;
            }
            wake?.TrySetResult(true);
        }

        private bool AudiencePresent
        {
            get { lock (_audienceLock) return _audiencePresent; }
        }

        private async Task Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (AudiencePresent)
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
                }

                try { await WaitForNextPoll(token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task WaitForNextPoll(CancellationToken token)
        {
            Task wake;
            bool present;
            lock (_audienceLock)
            {
                present = _audiencePresent;
                _audienceWake ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                wake = _audienceWake.Task;
            }

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var delay = present
                ? Task.Delay(_settings.Current.Interval * 1000, delayCts.Token)
                : Task.Delay(Timeout.Infinite, delayCts.Token);
            await Task.WhenAny(delay, wake);
            delayCts.Cancel();
            try { await delay; } catch (OperationCanceledException) { }
            token.ThrowIfCancellationRequested();
        }

        private async Task<JObject?> CheckLastGame()
        {
            if (IsAoe2())
                return await GetAoe2Latest(onlyIfNew: true);

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
            if (IsAoe2())
                return await GetAoe2Latest(onlyIfNew: false);

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
            if (IsAoe2()) return null;
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
            if (IsAoe2()) return null;
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
             if (IsAoe2())
                 return await FindAoe2Player(query);

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

        public async Task<JObject?> GetAoe2History()
        {
            if (string.IsNullOrEmpty(_settings.Current.ProfileId)) return null;
            return await FetchAoe2History(_settings.Current.ProfileId, allowCache: true);
        }

        public async Task<JObject?> GetAoe2PersonalStat(string? profileIds)
        {
            if (string.IsNullOrWhiteSpace(profileIds)) return null;
            try
            {
                var url = $"{Aoe2Api}getPersonalStat?title=age2&profile_ids=[{profileIds}]";
                return await GetJson(url);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        private bool IsAoe2() => _settings.Current.IsAoe2Game();

        private async Task<JObject?> GetAoe2Latest(bool onlyIfNew)
        {
            var profileId = _settings.Current.ProfileId;
            if (string.IsNullOrEmpty(profileId)) return null;

            var history = await FetchAoe2History(profileId, allowCache: onlyIfNew);
            var latest = NewestRankedInWindow(history);
            if (latest == null) return null;

            var matchId = latest.Value<long?>("id") ?? 0;
            if (onlyIfNew && matchId == _lastAoe2MatchId) return null;
            _lastAoe2MatchId = matchId;

            var ids = string.Join(",", (latest["matchhistorymember"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(m => m.Value<long?>("profile_id")?.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct());
            var stats = await GetAoe2PersonalStat(ids);

            return new JObject
            {
                ["source"] = "aoe2",
                ["match"] = latest.DeepClone(),
                ["profiles"] = history?["profiles"]?.DeepClone() ?? new JArray(),
                ["stats"] = stats?.DeepClone() ?? new JObject()
            };
        }

        private static JObject? NewestRankedInWindow(JObject? history)
        {
            var matches = (history?["matchHistoryStats"] as JArray ?? new JArray())
                .OfType<JObject>()
                .OrderByDescending(m => m.Value<long?>("startgametime") ?? 0)
                .Take(Aoe2Catalog.PollWindow);
            return matches.FirstOrDefault(m => Aoe2Catalog.IsRanked(m.Value<int?>("matchtype_id") ?? 0));
        }

        private async Task<JObject?> FetchAoe2History(string profileId, bool allowCache)
        {
            if (allowCache &&
                _aoe2History != null &&
                _aoe2HistoryProfileId == profileId &&
                DateTime.UtcNow - _aoe2HistoryAt < TimeSpan.FromSeconds(15))
            {
                return _aoe2History;
            }

            try
            {
                var url = $"{Aoe2Api}getRecentMatchHistory?title=age2&profile_ids=[{profileId}]";
                var json = await GetJson(url);
                if (json?["result"]?["code"]?.Value<int>() is int code && code != 0)
                    return null;
                _aoe2History = json;
                _aoe2HistoryProfileId = profileId;
                _aoe2HistoryAt = DateTime.UtcNow;
                return json;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        private async Task<JObject?> FindAoe2Player(string query)
        {
            try
            {
                LastError = null;
                query = query.Trim();
                if (long.TryParse(query, out _) && query.Length < 16)
                {
                    var stat = await GetAoe2PersonalStat(query);
                    var member = (stat?["statGroups"] as JArray)?.OfType<JObject>()
                        .SelectMany(g => (g["members"] as JArray ?? new JArray()).OfType<JObject>())
                        .FirstOrDefault(m => m.Value<long?>("profile_id")?.ToString() == query);
                    return member == null ? null : PlayerFromProfile(member);
                }

                string url;
                if (long.TryParse(query, out _) && query.Length >= 16)
                {
                    var names = Uri.EscapeDataString($"[\"/steam/{query}\"]");
                    url = $"{Aoe2Api}getRecentMatchHistory?title=age2&profile_names={names}";
                }
                else
                {
                    var escaped = query.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var aliases = Uri.EscapeDataString($"[\"{escaped}\"]");
                    url = $"{Aoe2Api}getRecentMatchHistory?title=age2&aliases={aliases}";
                }

                var history = await GetJson(url);
                var profiles = (history?["profiles"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                if (profiles.Count == 0) return null;

                JObject? chosen = null;
                if (query.Length >= 16 && long.TryParse(query, out _))
                    chosen = profiles.FirstOrDefault(p => string.Equals(p.Value<string>("name"), $"/steam/{query}", StringComparison.OrdinalIgnoreCase));
                chosen ??= profiles.FirstOrDefault(p => string.Equals(p.Value<string>("alias"), query, StringComparison.OrdinalIgnoreCase));
                return chosen == null ? null : PlayerFromProfile(chosen);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        private static JObject PlayerFromProfile(JObject profile)
        {
            var platform = profile.Value<string>("name") ?? "";
            var steam = platform.StartsWith("/steam/", StringComparison.OrdinalIgnoreCase)
                ? platform["/steam/".Length..]
                : platform;
            return new JObject
            {
                ["profile_id"] = profile.Value<long?>("profile_id")?.ToString(),
                ["name"] = profile.Value<string>("alias"),
                ["steam_id"] = steam
            };
        }

        private async Task<JObject?> GetJson(string url)
        {
            var resp = await _http.GetStringAsync(url);
            return JObject.Parse(resp);
        }
    }
}
