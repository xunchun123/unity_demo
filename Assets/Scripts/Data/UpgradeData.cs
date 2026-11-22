using UnityEngine;

namespace StarVortex.Data
{
    /// <summary>升级效果的种类。</summary>
    public enum UpgradeKind
    {
        DamageMul,      // 伤害倍率
        FireRateMul,    // 攻速倍率
        MoveSpeedMul,   // 移速倍率
        MaxHpAdd,       // 最大生命值加值（同时回复等量）
        PickupRadiusMul,// 拾取半径倍率
        ProjectileAdd,  // 弹幕数量 +1
        CritChanceAdd,  // 暴击率加值
        HpRegenAdd,     // 每秒回复
    }

    /// <summary>升级稀有度（决定卡片颜色与抽中权重）。</summary>
    public enum UpgradeRarity { Common = 0, Rare = 1, Epic = 2 }

    /// <summary>
    /// 升级项配置（三选一卡池的元素）。
    /// 面试点：升级逻辑 = 枚举种类 + 数值，全部数据驱动；
    /// 策划加新升级 = 新建资产，不用碰 PlayerStats 的代码。
    /// </summary>
    [CreateAssetMenu(fileName = "Upgrade_", menuName = "StarVortex/Upgrade Data")]
    public class UpgradeData : ScriptableObject
    {
        public string displayName = "火力全开";
        [TextArea] public string description = "伤害提升 25%";
        public UpgradeKind kind = UpgradeKind.DamageMul;
        public float value = 0.25f;
        public UpgradeRarity rarity = UpgradeRarity.Common;
        public Sprite icon;
        public Color iconColor = new Color(1f, 0.88f, 0.4f, 1f);
    }
}
