using System.Collections.Generic;

namespace StarVortex.Core
{
    /// <summary>
    /// 爽点目录：ID → 中文名称 / 效果描述 / 可叠加等级。
    /// 面试点：BoonIds 只定义"行为开关"的 ID，名称与文案是展示层数据——
    /// 单独成表避免把中文文案硬编码进三选一面板；后续接表格配置只需替换这一处。
    /// 抽卡规则配套：maxRank=1 的卡被抽到一次即从卡池移除（不放回）；
    /// maxRank&gt;1 的卡可重复获取，第 2/3 次显示等级 II / III。
    /// </summary>
    public static class BoonCatalog
    {
        /// <summary>单条爽点文案。</summary>
        public class Entry
        {
            public string id;
            public string name;
            public string desc;
            public int maxRank = 1; // 可重复获取的卡 &gt;1（II/III）；当前爽点全是行为开关，默认 1
        }

        private static readonly Dictionary<string, Entry> _map = new()
        {
            // ---------- 长剑池（近战连击形态，7 张） ----------
            [BoonIds.SwordShockwave]       = new Entry { id = BoonIds.SwordShockwave, name = "剑气激荡", desc = "三段连击的收尾斩挥出穿透剑气，远程收割残血" },
            [BoonIds.SwordLong]            = new Entry { id = BoonIds.SwordLong, name = "巨剑刃", desc = "剑刃判定加长 40%，连击更容易扫到敌人" },
            [BoonIds.SwordWhirlwind]       = new Entry { id = BoonIds.SwordWhirlwind, name = "旋风斩", desc = "长按 J 旋身连斩，持续切割周围敌人" },
            [BoonIds.SwordQuakeSlam]       = new Entry { id = BoonIds.SwordQuakeSlam, name = "震地裂斩", desc = "空中按 K 下劈落地，震波伤害并击退周围敌人" },
            [BoonIds.SwordComboLightning]  = new Entry { id = BoonIds.SwordComboLightning, name = "雷鸣连击", desc = "三段连击的收尾斩召唤落雷，劈击附近最多 3 名敌人" },
            [BoonIds.SwordLifesteal]       = new Entry { id = BoonIds.SwordLifesteal, name = "嗜血剑意", desc = "近战命中回复 2% 最大生命，越战越勇" },
            [BoonIds.SwordComboBurn]       = new Entry { id = BoonIds.SwordComboBurn, name = "烈焰连斩", desc = "三段连击的收尾斩点燃敌人，附加持续灼烧" },

            // ---------- 法杖池（奥术弹形态，蓝仅供 K 秘法新星） ----------
            [BoonIds.StaffFireballSplit]   = new Entry { id = BoonIds.StaffFireballSplit, name = "烈焰分裂", desc = "奥术弹命中后炸裂成 3 颗小火球" },
            [BoonIds.StaffBurningGround]   = new Entry { id = BoonIds.StaffBurningGround, name = "焦土术", desc = "奥术弹落地留下燃烧地面，灼烧踏入的敌人" },
            [BoonIds.StaffTwinBolt]        = new Entry { id = BoonIds.StaffTwinBolt, name = "双生法弹", desc = "每次射击附带一发 75% 伤害的副弹" },
            [BoonIds.StaffBigBurst]        = new Entry { id = BoonIds.StaffBigBurst, name = "爆裂扩容", desc = "爆炸范围 +60%，清群更痛快" },
            [BoonIds.StaffFrostNova]       = new Entry { id = BoonIds.StaffFrostNova, name = "冰霜新星", desc = "K 秘法新星附带冰霜，冻结周围敌人 1.5 秒" },
            [BoonIds.StaffArcaneFlow]      = new Entry { id = BoonIds.StaffArcaneFlow, name = "奥术回流", desc = "击杀敌人回复 6 点蓝量，新星转得更勤" },

            // ---------- 弓箭池（射击形态） ----------
            [BoonIds.BowPiercePlus]        = new Entry { id = BoonIds.BowPiercePlus, name = "穿云箭", desc = "箭矢穿透 +1 名敌人，排成串打" },
            [BoonIds.BowMultishot]         = new Entry { id = BoonIds.BowMultishot, name = "分裂箭袋", desc = "同时射出 2 支箭（小角度散射）" },
            [BoonIds.BowPowerShot]         = new Entry { id = BoonIds.BowPowerShot, name = "蓄力狙击", desc = "按住 J 蓄力 0.5 秒，伤害 ×2.5" },
            [BoonIds.BowArrowRain]         = new Entry { id = BoonIds.BowArrowRain, name = "箭雨风暴", desc = "按 K 在面前区域天降箭雨" },
            [BoonIds.BowBackstep]          = new Entry { id = BoonIds.BowBackstep, name = "箭舞后撤", desc = "射击自动后撤小跳，走 A 风筝更丝滑" },
            [BoonIds.BowExplosive]         = new Entry { id = BoonIds.BowExplosive, name = "爆裂箭头", desc = "箭矢命中引发小范围溅射爆炸" },

            // ---------- 通用池（任何武器都能刷到） ----------
            [BoonIds.UniversalDashMaster]  = new Entry { id = BoonIds.UniversalDashMaster, name = "疾风步", desc = "冲刺冷却 -40%" },
            [BoonIds.UniversalSecondWind]  = new Entry { id = BoonIds.UniversalSecondWind, name = "顺风回息", desc = "每关开始时回复 30% 生命" },
        };

        /// <summary>通用爽点池 ID（抽卡时拼在武器池后面；WeaponKit.boonPool 不含它们）。</summary>
        public static readonly string[] UniversalPool =
        {
            BoonIds.UniversalDashMaster,
            BoonIds.UniversalSecondWind,
        };

        public static bool TryGet(string id, out Entry entry) => _map.TryGetValue(id, out entry);

        /// <summary>名称（目录缺项时回退显示 ID，保证 UI 永远有字可看）。</summary>
        public static string Name(string id) => _map.TryGetValue(id, out var e) ? e.name : id;

        /// <summary>效果描述。</summary>
        public static string Desc(string id) => _map.TryGetValue(id, out var e) ? e.desc : string.Empty;

        /// <summary>可叠加等级上限（1 = 不放回）。</summary>
        public static int MaxRank(string id) => _map.TryGetValue(id, out var e) ? e.maxRank : 1;

        /// <summary>带等级后缀的显示名：第 2/3 次获取显示 "名称 II / III"，首取无后缀。</summary>
        public static string DisplayName(string id, int rank)
        {
            string name = Name(id);
            return rank >= 2 ? $"{name} {RankSuffix(rank)}" : name;
        }

        /// <summary>等级罗马数字后缀（仅 2/3 级显示）。</summary>
        public static string RankSuffix(int rank)
        {
            switch (rank)
            {
                case 2: return "II";
                case 3: return "III";
                default: return string.Empty;
            }
        }
    }
}
