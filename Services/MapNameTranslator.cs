using System;
using System.Collections.Generic;

namespace AoE4OverlayCS.Services
{
    public static class MapNameTranslator
    {
        private static readonly Dictionary<string, string> ZhCnMapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Baltic"] = "波罗的海",
            ["Archipelago"] = "群岛",
            ["Channel"] = "海峡 / 运河",
            ["Canal"] = "运河",
            ["Cliffside"] = "悬崖边",
            ["Coastal Cliffs"] = "海岸悬崖",
            ["Confluence"] = "汇流之地",
            ["Continental"] = "大陆",
            ["Danube River"] = "多瑙河",
            ["Dry Arabia"] = "干燥阿拉伯",
            ["Dry River"] = "干涸之河",
            ["Floodplains"] = "冲积平原",
            ["Flankwoods"] = "林地边缘",
            ["Forest Ponds"] = "森林池塘",
            ["Forts"] = "堡垒",
            ["Four Lakes"] = "四个湖",
            ["French Pass"] = "法兰西关隘",
            ["Frisian Marshes"] = "弗里西亚沼泽",
            ["Glade"] = "林间空地",
            ["Golden Heights"] = "黄金高地",
            ["Golden Pit"] = "金坑",
            ["Gorge"] = "峡谷",
            ["Haunted Gulch"] = "闹鬼峡谷",
            ["Haywire"] = "大混乱",
            ["Hidden Valley"] = "隐秘山谷",
            ["Hideout"] = "藏身处",
            ["High View"] = "高视野区",
            ["Highview"] = "高视野区",
            ["Highland"] = "高地",
            ["Hill and Dale"] = "山脉与山谷",
            ["Himeyama"] = "姬路山",
            ["Holy Island"] = "圣岛",
            ["Jousting Fields"] = "比武场",
            ["Kawasan"] = "卡瓦桑",
            ["King of The Hill"] = "山丘之王",
            ["Lipany"] = "利帕尼",
            ["Marasita"] = "马拉西塔",
            ["Marshland"] = "沼泽地",
            ["Mega Random"] = "超级随机",
            ["Megarandom"] = "超级随机",
            ["Migration"] = "移民",
            ["Mongolian Heights"] = "蒙古高原",
            ["Mountain Clearing"] = "林中空地",
            ["Mountain Pass"] = "山脉通道 / 险要关隘",
            ["Moving Out"] = "迁徙",
            ["Nagari"] = "纳加里",
            ["Narrows"] = "狭道海峡",
            ["New Four Lakes"] = "新四湖",
            ["Oasis"] = "绿洲",
            ["Peagee"] = "PG测试图",
            ["Plains"] = "平原",
            ["Prairie"] = "草原",
            ["Random"] = "随机",
            ["Regions"] = "区域 / 领域",
            ["Relic River"] = "遗迹河流",
            ["Rhinelands"] = "莱茵兰",
            ["River Kingdom"] = "河流王国",
            ["Rockies"] = "落基山脉",
            ["Rocky Canyon"] = "岩石峡谷",
            ["Rocky River"] = "岩石之河",
            ["Rolling Rivers"] = "翻滚之河",
            ["Rugged"] = "崎岖之地",
            ["Savanna Woodlands"] = "萨凡纳林地",
            ["Scandinavia"] = "斯堪的纳维亚",
            ["Skargard"] = "斯卡加德群岛",
            ["Socotra"] = "索科特拉岛",
            ["Sunkenlands"] = "沉没之地",
            ["tempi"] = "圣殿",
            ["The Pit"] = "深坑",
            ["Turtle"] = "乌龟岛",
            ["Turtle Ridge"] = "乌龟山脊",
            ["Volcanic Island"] = "火山岛",
            ["Wadden Sea"] = "瓦登海",
            ["Warring Islands"] = "交战群岛",
            ["Water Drake"] = "水龙 / 蛟龙",
            ["Waterholes"] = "水坑",
            ["Wetlands"] = "湿地",
            ["Woodwall"] = "木墙 / 林墙"
        };

        public static string Translate(string? mapName, string? language)
        {
            var normalized = Normalize(mapName);
            if (string.IsNullOrEmpty(normalized)) return mapName ?? "";
            if (!string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)) return normalized;

            return ZhCnMapNames.TryGetValue(normalized, out var translated)
                ? translated
                : normalized;
        }

        private static string Normalize(string? mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return "";
            return mapName.Trim().Replace('_', ' ');
        }
    }
}
