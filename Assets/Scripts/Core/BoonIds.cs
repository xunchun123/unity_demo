using System.Collections.Generic;
using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>初始武器类型（选人界面三选一，强化树互相独立不污染）。</summary>
    public enum WeaponKind { Sword, Staff, Bow }

    /// <summary>
    /// 爽点 ID 常量：按武器分池——长剑的强化不碰法杖，弓箭的穿透不碰长剑。
    /// 设计规则（用户明确要求）：爽点三选一只从"当前武器池 + 通用池"抽，杜绝跨武器矛盾。
    /// </summary>
    public static class BoonIds
    {
        // ---------- 长剑池（近战连击形态） ----------
        public const string SwordShockwave = "sword_shockwave";   // 三段连击末段挥出远程剑气
        public const string SwordLong = "sword_long";             // 剑刃加长 40%（判定+视觉）
        public const string SwordWhirlwind = "sword_whirlwind";   // 长按 J 旋转斩（持续范围伤害）
        public const string SwordQuakeSlam = "sword_quake_slam";  // 空中下劈落地震波
        public const string SwordComboLightning = "sword_lightning"; // 三段连击触发落雷
        public const string SwordLifesteal = "sword_lifesteal";   // 近战命中回 2% 生命
        public const string SwordComboBurn = "sword_combo_burn";  // 三段连击末段附带火焰灼烧（持续掉血）

        // ---------- 法杖池（火球形态） ----------
        public const string StaffFireballSplit = "staff_fireball_split"; // 火球命中炸成 3 小火球
        public const string StaffBurningGround = "staff_burning_ground"; // 火球落地留燃烧地面
        public const string StaffTwinBolt = "staff_twin_bolt";           // 火球双发
        public const string StaffBigBurst = "staff_big_burst";           // 爆炸范围 +60%
        public const string StaffFrostNova = "staff_frost_nova";         // K 键改冰霜新星（冻结周围敌人 1.5s）
        public const string StaffArcaneFlow = "staff_arcane_flow";       // 击杀回 6 蓝

        // ---------- 弓箭池（射击形态） ----------
        public const string BowPiercePlus = "bow_pierce_plus";     // 箭矢穿透 +1
        public const string BowMultishot = "bow_multishot";        // 同时射出 2 箭（小角度散射）
        public const string BowPowerShot = "bow_power_shot";       // 蓄力满 0.5s 伤害 ×2.5
        public const string BowArrowRain = "bow_arrow_rain";       // K 键改箭雨（范围内天降箭矢）
        public const string BowBackstep = "bow_backstep";          // 射击自动后撤小跳（走 A 风筝）
        public const string BowExplosive = "bow_explosive";        // 箭头爆裂（小范围溅射）

        // ---------- 通用池（任何武器都能刷到） ----------
        public const string UniversalDashMaster = "u_dash_master"; // 冲刺冷却 -40%
        public const string UniversalSecondWind = "u_second_wind"; // 每关开始回复 30% 生命
        public const string UniversalFlameDash = "u_flame_dash";   // 冲刺路径灼烧敌人
    }

    /// <summary>爽点清单：记录已获得的爽点 ID，战斗系统用 Has() 查询改变行为。</summary>
    public class BoonInventory
    {
        private readonly HashSet<string> _owned = new();

        /// <summary>拥有指定爽点？</summary>
        public bool Has(string id) => _owned.Contains(id);

        /// <summary>获得爽点（广播事件供 HUD 提示）。</summary>
        public void Add(string id)
        {
            if (_owned.Add(id))
                GameEvents.PublishBoonAcquired(id);
        }

        public IReadOnlyCollection<string> Owned => _owned;
    }
}
