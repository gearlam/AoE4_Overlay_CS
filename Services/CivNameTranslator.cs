using System;
using System.Collections.Generic;

namespace AoE4OverlayCS.Services
{
    public static class CivNameTranslator
    {
        private static readonly Dictionary<string, string> ZhCnCivNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["abbasid dynasty"] = "阿拔斯王朝",
            ["abbasid"] = "阿拔斯王朝",
            ["ayyubids"] = "阿尤布",
            ["byzantines"] = "拜占庭",
            ["chinese"] = "中国",
            ["delhi sultanate"] = "德里苏丹",
            ["delhi"] = "德里苏丹",
            ["english"] = "英格兰",
            ["french"] = "法兰西",
            ["golden horde"] = "金帐汗国",
            ["house of lancaster"] = "兰开斯特",
            ["holy roman empire"] = "神圣罗马帝国",
            ["japanese"] = "日本",
            ["jeanne darc"] = "贞德",
            ["jin dynasty"] = "金朝",
            ["knights templar"] = "圣殿骑士团",
            ["macedonian dynasty"] = "马其顿",
            ["macedonian"] = "马其顿",
            ["malians"] = "马里",
            ["mongols"] = "蒙古",
            ["order of the dragon"] = "龙之骑士团",
            ["ottomans"] = "奥斯曼",
            ["rus"] = "罗斯",
            ["scots"] = "苏格兰人",
            ["vikings"] = "维京人",
            ["sengoku daimyo"] = "战国大名",
            ["tughlaq dynasty"] = "图格鲁克王朝",
            ["tughlaq"] = "图格鲁克王朝",
            ["zhu xis legacy"] = "朱熹的遗产",
            ["venetians"] = "威尼斯",
            ["poles"] = "波兰",
        };

        private static readonly Dictionary<string, string> EnglishCivNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["abbasid dynasty"] = "Abbasid Dynasty",
            ["abbasid"] = "Abbasid Dynasty",
            ["ayyubids"] = "Ayyubids",
            ["byzantines"] = "Byzantines",
            ["chinese"] = "Chinese",
            ["delhi sultanate"] = "Delhi Sultanate",
            ["delhi"] = "Delhi Sultanate",
            ["english"] = "English",
            ["french"] = "French",
            ["golden horde"] = "Golden Horde",
            ["house of lancaster"] = "House of Lancaster",
            ["holy roman empire"] = "Holy Roman Empire",
            ["japanese"] = "Japanese",
            ["jeanne darc"] = "Jeanne d'Arc",
            ["jin dynasty"] = "Jin Dynasty",
            ["knights templar"] = "Knights Templar",
            ["macedonian dynasty"] = "Macedonian Dynasty",
            ["macedonian"] = "Macedonian Dynasty",
            ["malians"] = "Malians",
            ["mongols"] = "Mongols",
            ["order of the dragon"] = "Order of the Dragon",
            ["ottomans"] = "Ottomans",
            ["rus"] = "Rus",
            ["scots"] = "Scots",
            ["vikings"] = "Vikings",
            ["sengoku daimyo"] = "Sengoku Daimyo",
            ["tughlaq dynasty"] = "Tughlaq Dynasty",
            ["tughlaq"] = "Tughlaq Dynasty",
            ["zhu xis legacy"] = "Zhu Xi's Legacy",
            ["venetians"] = "Venetians",
            ["poles"] = "Poles",
        };

        public static string Translate(string? civName, string? language)
        {
            if (string.IsNullOrWhiteSpace(civName)) return civName ?? "";
            var normalized = civName.Trim().Replace('_', ' ').Replace("'", "").Replace("’", "");
            if (!string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase))
                return EnglishCivNames.TryGetValue(normalized, out var english) ? english : civName;
            return ZhCnCivNames.TryGetValue(normalized, out var translated) ? translated : civName;
        }
    }
}
