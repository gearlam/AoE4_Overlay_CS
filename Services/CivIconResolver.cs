using System;
using System.Collections.Generic;
using System.IO;

namespace AoE4OverlayCS
{
    public static class CivIconResolver
    {
        private static readonly Dictionary<string, string> CivCodeMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            { "abb", "Abbasid" },
            { "abbasid", "Abbasid" },
            { "abbasid dynasty", "Abbasid" },
            { "ayy", "Ayyubids" },
            { "ayyubids", "Ayyubids" },
            { "byz", "Byzantines" },
            { "byzantines", "Byzantines" },
            { "chi", "Chinese" },
            { "chinese", "Chinese" },
            { "del", "Delhi" },
            { "delhi", "Delhi" },
            { "delhi sultanate", "Delhi" },
            { "eng", "English" },
            { "english", "English" },
            { "fre", "French" },
            { "french", "French" },
            { "goh", "GoldenHorde" },
            { "golden_horde", "GoldenHorde" },
            { "golden horde", "GoldenHorde" },
            { "hol", "HouseofLancaster" },
            { "hos", "HouseofLancaster" },
            { "house_of_lancaster", "HouseofLancaster" },
            { "house of lancaster", "HouseofLancaster" },
            { "lancaster", "HouseofLancaster" },
            { "hre", "HRE" },
            { "holy roman empire", "HRE" },
            { "jap", "Japanese" },
            { "japanese", "Japanese" },
            { "jin dynasty", "JinDynasty" },
            { "jda", "JeanneDArc" },
            { "jeanne_darc", "JeanneDArc" },
            { "jeanne d'arc", "JeanneDArc" },
            { "jeanne darc", "JeanneDArc" },
            { "jeanne", "JeanneDArc" },
            { "kte", "KnightsTemplar" },
            { "koc", "KnightsTemplar" },
            { "knights_templar", "KnightsTemplar" },
            { "knights templar", "KnightsTemplar" },
            { "templar", "KnightsTemplar" },
            { "mac", "MacedonianDynasty" },
            { "macedonian", "MacedonianDynasty" },
            { "macedonian dynasty", "MacedonianDynasty" },
            { "mal", "Malians" },
            { "malians", "Malians" },
            { "mon", "Mongols" },
            { "mongols", "Mongols" },
            { "dra", "OrderOfTheDragon" },
            { "order_of_the_dragon", "OrderOfTheDragon" },
            { "order of the dragon", "OrderOfTheDragon" },
            { "dragon", "OrderOfTheDragon" },
            { "ott", "Ottomans" },
            { "ottomans", "Ottomans" },
            { "pol", "Poles" },
            { "poles", "Poles" },
            { "rus", "Rus" },
            { "scots", "Scots" },
            { "vikings", "Vikings" },
            { "sen", "SengokuDaimyo" },
            { "sengoku", "SengokuDaimyo" },
            { "sengoku daimyo", "SengokuDaimyo" },
            { "sengoku_daimyo", "SengokuDaimyo" },
            { "teu", "KnightsTemplar" },
            { "tug", "TughlaqDynasty" },
            { "tughlaq", "TughlaqDynasty" },
            { "tughlaq dynasty", "TughlaqDynasty" },
            { "ven", "Venetians" },
            { "venetians", "Venetians" },
            { "zxl", "ZhuXiLegacy" },
            { "zhu_xi_legacy", "ZhuXiLegacy" },
            { "zhu_xis_legacy", "ZhuXiLegacy" },
            { "zhu xi's legacy", "ZhuXiLegacy" },
            { "zhu xis legacy", "ZhuXiLegacy" },
            { "zhuxi", "ZhuXiLegacy" },
        };

        public static string? Resolve(string baseDir, string civ, string civKey)
        {
            var searchPaths = new List<string>();
            // The shipped flags use display-name filenames, while API values use lowercase slugs.
            var flagName = Services.CivNameTranslator.Translate(civ, "en-US").Replace("'", "").Replace("’", "");
            var keyFlagName = Services.CivNameTranslator.Translate(civKey, "en-US").Replace("'", "").Replace("’", "");
            searchPaths.Add(Path.Combine(baseDir, "img", "flags", flagName + ".png"));
            searchPaths.Add(Path.Combine(baseDir, "img", "flags", keyFlagName + ".png"));

            if (CivCodeMapping.TryGetValue(civ, out var mappedName))
            {
                searchPaths.Add(Path.Combine(baseDir, "img", "build_order", "civilization_flag", $"CivIcon-{mappedName}AoE4.png"));
                searchPaths.Add(Path.Combine(baseDir, "img", "build_order", "civilization_flag", $"CivIcon-{mappedName}AoE4_spacing.png"));
            }

            if (CivCodeMapping.TryGetValue(civKey, out mappedName))
            {
                searchPaths.Add(Path.Combine(baseDir, "img", "build_order", "civilization_flag", $"CivIcon-{mappedName}AoE4.png"));
                searchPaths.Add(Path.Combine(baseDir, "img", "build_order", "civilization_flag", $"CivIcon-{mappedName}AoE4_spacing.png"));
            }

            searchPaths.Add(Path.Combine(baseDir, "img", "build_order", "civilization_flag", $"{civKey}.png"));
            searchPaths.Add(Path.Combine(baseDir, "img", "flags", $"{civ}.png"));

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
