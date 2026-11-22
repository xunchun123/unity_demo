using System;
using StarVortex.Data;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 玩家运行时属性面板：基础值 + 升级修改器。
    /// 面试点：运行时数据与配置数据分离——ScriptableObject 是共享模板，
    /// 运行时用普通 C# 类持有一份可变拷贝，绝不直接改 SO 资产（改了会污染所有对局）。
    /// </summary>
    [Serializable]
    public class PlayerStats
    {
        // ---------- 基础值（出生配置） ----------
        public float baseMaxHp = 100f;
        public float baseMoveSpeed = 5.2f;
        public float basePickupRadius = 2.2f;

        // ---------- 升级修改器 ----------
        public float damageMul = 1f;
        public float fireRateMul = 1f;
        public float moveSpeedMul = 1f;
        public float pickupRadiusMul = 1f;
        public int projectileAdd = 0;
        public float critChance = 0.05f;   // 基础 5% 暴击
        public float hpRegen = 0f;         // 每秒回复

        // ---------- 经验/等级 ----------
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpToNext => XpForLevel(Level);

        /// <summary>升级所需经验：线性+平方混合曲线，前期爽快后期有压力。</summary>
        public static int XpForLevel(int level) => 5 + level * 4 + level * level;

        public float MaxHp => baseMaxHp;
        public float MoveSpeed => baseMoveSpeed * moveSpeedMul;
        public float PickupRadius => basePickupRadius * pickupRadiusMul;

        /// <summary>应用一项升级效果（由三选一 UI 调用）。</summary>
        public void ApplyUpgrade(UpgradeData upgrade)
        {
            switch (upgrade.kind)
            {
                case UpgradeKind.DamageMul:       damageMul += upgrade.value; break;
                case UpgradeKind.FireRateMul:     fireRateMul += upgrade.value; break;
                case UpgradeKind.MoveSpeedMul:    moveSpeedMul += upgrade.value; break;
                case UpgradeKind.MaxHpAdd:        baseMaxHp += upgrade.value; break;
                case UpgradeKind.PickupRadiusMul: pickupRadiusMul += upgrade.value; break;
                case UpgradeKind.ProjectileAdd:   projectileAdd += Mathf.RoundToInt(upgrade.value); break;
                case UpgradeKind.CritChanceAdd:   critChance += upgrade.value; break;
                case UpgradeKind.HpRegenAdd:      hpRegen += upgrade.value; break;
                default: throw new ArgumentOutOfRangeException(nameof(upgrade));
            }
        }

        /// <summary>获得经验；返回本次累计的升级次数（可能一局经验溢出连升）。</summary>
        public int AddXp(int amount)
        {
            Xp += amount;
            int levelUps = 0;
            while (Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                levelUps++;
            }
            return levelUps;
        }
    }
}
