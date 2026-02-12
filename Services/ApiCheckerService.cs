using AoE4OverlayCS.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
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

        public ApiCheckerService(SettingsService settings)
        {
            _settings = settings;
            _http = new HttpClient();
            // Default timeout
            _http.Timeout = TimeSpan.FromSeconds(10);
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
            var url = $"https://aoe4world.com/api/v0/players/{_settings.Current.ProfileId}/games/last";
            var resp = await _http.GetStringAsync(url);
            var json = JObject.Parse(resp);

            if (json.ContainsKey("error")) return null;

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

        public async Task<JObject?> FindPlayer(string query)
        {
             try 
             {
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
             catch {}
             return null;
        }
    }
}
