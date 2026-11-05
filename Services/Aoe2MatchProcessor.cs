using AoE4OverlayCS.Models;
using AoE4OverlayCS.ViewModels;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// 把 Relic 对局收成覆盖层已经在用的 { map, players[] }，不走 AOE4 的 GameProcessor。
    /// </summary>
    public static class Aoe2MatchProcessor
    {
        public static object Process(JObject payload, AppSettings settings)
        {
            var match = payload["match"] as JObject ?? payload;
            var profiles = IndexProfiles(payload["profiles"] as JArray);
            var stats = payload["stats"] as JObject;
            var matchType = match.Value<int?>("matchtype_id") ?? 0;
            var board = Aoe2Catalog.LeaderboardId(matchType);
            bool zh = IsZh(settings);

            var result = new Dictionary<string, object>
            {
                ["source"] = "aoe2",
                ["map"] = Aoe2Catalog.MapName(match.Value<string>("mapname"), zh),
                ["mode"] = matchType,
                ["started"] = UnixToLocal(match.Value<long?>("startgametime")),
                ["ranked"] = Aoe2Catalog.IsRanked(matchType),
                ["server"] = "",
                ["match_id"] = match.Value<long?>("id")?.ToString() ?? ""
            };

            var members = (match["matchhistorymember"] as JArray ?? new JArray())
                .OfType<JObject>()
                .ToList();
            int? mainTeam = null;
            foreach (var member in members)
            {
                if (member.Value<long?>("profile_id")?.ToString() == settings.ProfileId)
                {
                    mainTeam = member.Value<int?>("teamid") ?? 0;
                    break;
                }
            }

            members.Sort((a, b) =>
            {
                var teamA = a.Value<int?>("teamid") ?? 99;
                var teamB = b.Value<int?>("teamid") ?? 99;
                if (mainTeam.HasValue)
                {
                    if (teamA == mainTeam.Value && teamB != mainTeam.Value) return -1;
                    if (teamB == mainTeam.Value && teamA != mainTeam.Value) return 1;
                }
                return teamA.CompareTo(teamB);
            });

            var players = new List<object>();
            foreach (var member in members)
            {
                var profileId = member.Value<long?>("profile_id")?.ToString() ?? "";
                profiles.TryGetValue(profileId, out var profile);
                var civId = member.Value<int?>("civilization_id") ?? 0;
                var civ = Aoe2Catalog.CivEnglish(civId);
                var wins = member.Value<int?>("wins") ?? 0;
                var losses = member.Value<int?>("losses") ?? 0;
                var rank = FindRank(stats, profileId, board);
                players.Add(new
                {
                    civ = string.IsNullOrEmpty(civ) ? "unknown" : civ,
                    civ_display = Aoe2Catalog.CivDisplay(civId, zh),
                    name = profile?.Value<string>("alias") ?? "?",
                    team = (member.Value<int?>("teamid") ?? 0) + 1,
                    country = profile?.Value<string>("country") ?? "",
                    country_display = CountryNameTranslator.Translate(profile?.Value<string>("country"), settings.Language),
                    rating = (member.Value<int?>("newrating") ?? 0).ToString(),
                    rank = rank > 0 ? $"#{rank}" : "",
                    wins = wins.ToString(),
                    losses = losses.ToString(),
                    winrate = WinRate(wins, losses),
                    civ_games = "",
                    civ_winrate = "",
                    civ_win_length_median = ""
                });
            }

            result["players"] = players;
            return result;
        }

        public static List<MatchHistoryItem> ToHistory(JObject history, AppSettings settings)
        {
            var profiles = IndexProfiles(history["profiles"] as JArray);
            bool zh = IsZh(settings);
            var items = new List<MatchHistoryItem>();
            var matches = (history["matchHistoryStats"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Where(m => Aoe2Catalog.IsRanked(m.Value<int?>("matchtype_id") ?? 0))
                .OrderByDescending(m => m.Value<long?>("startgametime") ?? 0)
                .ToList();

            foreach (var match in matches)
            {
                var members = (match["matchhistorymember"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .ToList();
                var me = members.FirstOrDefault(m => m.Value<long?>("profile_id")?.ToString() == settings.ProfileId);
                var myTeam = me?.Value<int?>("teamid");
                var matchType = match.Value<int?>("matchtype_id") ?? 0;

                var item = new MatchHistoryItem
                {
                    UseAoe2Links = true,
                    Map = Aoe2Catalog.MapName(match.Value<string>("mapname"), zh),
                    MatchId = match.Value<long?>("id")?.ToString() ?? "",
                    ProfileId = settings.ProfileId ?? "",
                    Mode = Aoe2Catalog.ModeName(matchType, zh),
                    Started = UnixToLocal(match.Value<long?>("startgametime"))
                };

                if (me != null)
                {
                    var outcome = me.Value<int?>("outcome");
                    item.Result = outcome switch
                    {
                        1 => zh ? "赢" : "win",
                        0 => zh ? "输" : "loss",
                        _ => ""
                    };
                    var oldRating = me.Value<int?>("oldrating");
                    var newRating = me.Value<int?>("newrating");
                    if (oldRating.HasValue && newRating.HasValue)
                    {
                        var diff = newRating.Value - oldRating.Value;
                        item.RatingDiff = diff > 0 ? $"+{diff}" : diff.ToString();
                    }
                }

                var orderedTeams = members
                    .GroupBy(m => m.Value<int?>("teamid") ?? 0)
                    .OrderBy(g => myTeam.HasValue && g.Key == myTeam.Value ? 0 : 1)
                    .ThenBy(g => g.Key)
                    .ToList();

                item.Team1Players = ToPlayers(orderedTeams.FirstOrDefault(), profiles, zh, settings);
                item.Team2Players = ToPlayers(orderedTeams.Skip(1).SelectMany(g => g), profiles, zh, settings);
                item.Team1Display = string.Join(Environment.NewLine, item.Team1Players.Select(p => $"{p.Name} ({p.Civ})"));
                item.Team2Display = string.Join(Environment.NewLine, item.Team2Players.Select(p => $"{p.Name} ({p.Civ})"));
                items.Add(item);
            }

            return items;
        }

        private static List<PlayerDisplayInfo> ToPlayers(IEnumerable<JObject>? members, Dictionary<string, JObject> profiles, bool zh, AppSettings settings)
        {
            if (members == null) return new List<PlayerDisplayInfo>();
            return members.Select(member =>
            {
                var profileId = member.Value<long?>("profile_id")?.ToString() ?? "";
                profiles.TryGetValue(profileId, out var profile);
                var civId = member.Value<int?>("civilization_id") ?? 0;
                return new PlayerDisplayInfo
                {
                    UseAoe2Links = true,
                    Name = profile?.Value<string>("alias") ?? "?",
                    ProfileId = profileId,
                    Civ = Aoe2Catalog.CivDisplay(civId, zh),
                    CivColor = settings.CivStatsColor
                };
            }).ToList();
        }

        private static Dictionary<string, JObject> IndexProfiles(JArray? profiles)
        {
            var map = new Dictionary<string, JObject>();
            if (profiles == null) return map;
            foreach (var profile in profiles.OfType<JObject>())
            {
                var id = profile.Value<long?>("profile_id")?.ToString();
                if (!string.IsNullOrEmpty(id))
                    map[id] = profile;
            }
            return map;
        }

        private static int FindRank(JObject? stats, string profileId, int leaderboardId)
        {
            if (stats == null || string.IsNullOrEmpty(profileId)) return -1;
            int? statGroupId = null;
            foreach (var group in (stats["statGroups"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var mine = (group["members"] as JArray ?? new JArray()).OfType<JObject>()
                    .Any(m => m.Value<long?>("profile_id")?.ToString() == profileId);
                if (mine)
                {
                    statGroupId = group.Value<int?>("id");
                    break;
                }
            }
            if (statGroupId == null) return -1;

            foreach (var row in (stats["leaderboardStats"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (row.Value<int?>("statgroup_id") != statGroupId) continue;
                if (row.Value<int?>("leaderboard_id") != leaderboardId) continue;
                return row.Value<int?>("rank") ?? -1;
            }
            return -1;
        }

        private static string WinRate(int wins, int losses)
        {
            var total = wins + losses;
            if (total <= 0) return "0%";
            return $"{(double)wins / total * 100:0}%";
        }

        private static string UnixToLocal(long? unix)
        {
            if (unix == null || unix <= 0) return "";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix.Value).ToLocalTime().ToString("g");
            }
            catch
            {
                return "";
            }
        }

        private static bool IsZh(AppSettings settings) =>
            string.Equals(settings.Language, "zh-CN", StringComparison.OrdinalIgnoreCase);
    }
}
