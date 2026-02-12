using AoE4OverlayCS.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AoE4OverlayCS.Services
{
    public static class GameProcessor
    {
        public static object ProcessGame(JObject gameData, AppSettings settings)
        {
            var result = new Dictionary<string, object>();
            result["map"] = gameData["map"]?.ToString() ?? "";
            result["mode"] = gameData["leaderboard_id"]?.ToObject<int>() ?? 0;
            result["started"] = gameData["started_at"]?.ToString() ?? "";
            var kind = gameData["kind"]?.ToString() ?? "";
            result["ranked"] = kind.Contains("qm_") || kind.Contains("rm_");
            result["server"] = gameData["server"]?.ToString() ?? "";
            result["match_id"] = gameData["game_id"]?.ToString() ?? "";

            var mode = kind;
            if (new[] { "rm_4v4", "rm_3v3", "rm_2v2" }.Contains(mode))
            {
                mode = "rm_team";
            }

            var teams = gameData["teams"] as JArray;
            var players = new List<dynamic>();
            int? mainTeam = null;

            if (teams != null)
            {
                for (int i = 0; i < teams.Count; i++)
                {
                    var team = teams[i] as JArray;
                    if (team != null)
                    {
                        foreach (var p in team)
                        {
                            var player = p as JObject;
                            if (player != null)
                            {
                                // Add team index
                                player["team"] = i;
                                
                                var profileId = player["profile_id"]?.ToString();
                                if (profileId == settings.ProfileId?.ToString())
                                {
                                    mainTeam = i;
                                }
                                players.Add(player);
                            }
                        }
                    }
                }
            }

            // Sort players: main team first
            players.Sort((a, b) =>
            {
                int teamA = a["team"]?.ToObject<int>() ?? 99;
                int teamB = b["team"]?.ToObject<int>() ?? 99;

                if (mainTeam.HasValue)
                {
                    if (teamA == mainTeam.Value && teamB != mainTeam.Value) return -1;
                    if (teamB == mainTeam.Value && teamA != mainTeam.Value) return 1;
                }
                return teamA.CompareTo(teamB);
            });

            var processedPlayers = new List<object>();

            foreach (var p in players)
            {
                var lookupMode = mode;
                var currentCiv = p["civilization"]?.ToString() ?? "";
                var name = p["name"]?.ToString() ?? "?";
                
                // Stats
                string civGames = "", civWinrate = "", civWinMedian = "";
                
                try 
                {
                    // Basic mode mapping logic from python
                    var modes = p["modes"] as JObject;
                    if (modes != null && !modes.ContainsKey(lookupMode))
                    {
                        if (lookupMode.Contains("rm_")) lookupMode = lookupMode.Replace("rm_", "qm_");
                        else if (lookupMode.Contains("qm_")) lookupMode = lookupMode.Replace("qm_", "rm_");
                    }

                    if (modes != null && modes.ContainsKey(lookupMode))
                    {
                        var modeDataObj = modes[lookupMode] as JObject;
                        var civs = modeDataObj?["civilizations"] as JArray;
                        if (civs != null)
                        {
                            foreach (var c in civs)
                            {
                                if (c["civilization"]?.ToString() == currentCiv)
                                {
                                    civGames = c["games_count"]?.ToString() ?? "";
                                    var wr = c["win_rate"]?.ToObject<double>() ?? 0;
                                    civWinrate = $"{wr / 100:P1}";
                                    // Median calculation simplified
                                    break;
                                }
                            }
                        }
                    }
                }
                catch {}

                var modeData = p["modes"]?[lookupMode] as JObject;
                var modeStr = lookupMode.Split('_')[0].ToUpper();
                
                int teamIdx = p["team"]?.ToObject<int>() ?? 0;

                processedPlayers.Add(new {
                    civ = currentCiv.Replace("_", " "), // Capitalize handled by WPF converter if needed
                    name = name,
                    team = teamIdx + 1,
                    country = p["country"]?.ToString() ?? "",
                    rating = modeData?["rating"]?.ToString() ?? "0",
                    rank = $"{modeStr}#{modeData?["rank"]?.ToString() ?? "0"}",
                    wins = modeData?["wins_count"]?.ToString() ?? "0",
                    losses = modeData?["losses_count"]?.ToString() ?? "0",
                    winrate = $"{modeData?["win_rate"]?.ToString() ?? "0"}%",
                    civ_games = civGames,
                    civ_winrate = civWinrate,
                    civ_win_length_median = civWinMedian
                });
            }

            result["players"] = processedPlayers;
            return result;
        }
    }
}
