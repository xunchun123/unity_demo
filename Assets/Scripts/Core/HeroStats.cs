using System;
using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>
    /// 勇者属性面板（横版闯关版）：基础值 + 天赋加点 + 爽点标记。
    /// 升级获得 1 天赋点（玩家手动分配到五维）+ 1 次爽点三选一（随机刷出，改变攻击形态）。
    /// 面试点：属性成长（数值）与爽点（形态）分轨——数值让玩家"变强"，爽点让玩家"变酷"，
    /// 两条成长线在同一个升级暂停界面里给玩家双重决策。
    /// </summary>
    [Serializable]
    public class HeroStats
    {
        // ---------- 基础值 ----------
        public float baseMaxHp = 100f;
        public float baseMaxMana = 100f;
        public float baseAttack = 20f;      // 近战每段基础伤害
        public float baseAttackInterval = 0.32f; // 连击段间隔
        public float baseMoveSpeed = 6f;
        public float manaRegen = 4f;        // 每秒回蓝

        // ---------- 天赋加点（每点效果） ----------
        public const float HpPerTalent = 20f;
        public const float AtkPctPerTalent = 0.10f;
        public const float AtkSpeedPctPerTalent = 0.08f;
        public const float MovePctPerTalent = 0.05f;
        public const float ManaPerTalent = 20f;

        public int talentHp;        // 已投入生命的天赋点
        public int talentAttack;
        public int talentAttackSpeed;
        public int talentMove;
        public int talentMana;
        public int pendingTalentPoints; // 未分配的天赋点（升级 +1）

        // ---------- 最终属性 ----------
        public float MaxHp => baseMaxHp + talentHp * HpPerTalent;
        public float MaxMana => baseMaxMana + talentMana * ManaPerTalent;
        public float Attack => baseAttack * (1f + talentAttack * AtkPctPerTalent);
        public float AttackInterval => baseAttackInterval / (1f + talentAttackSpeed * AtkSpeedPctPerTalent);
        public float MoveSpeed => baseMoveSpeed * (1f + talentMove * MovePctPerTalent);

        /// <summary>投入 1 点到指定维度（升级界面调用）。</summary>
        public bool SpendTalent(TalentKind kind)
        {
            if (pendingTalentPoints <= 0) return false;
            pendingTalentPoints--;
            switch (kind)
            {
                case TalentKind.Hp: talentHp++; break;
                case TalentKind.Attack: talentAttack++; break;
                case TalentKind.AttackSpeed: talentAttackSpeed++; break;
                case TalentKind.Move: talentMove++; break;
                case TalentKind.Mana: talentMana++; break;
                default: return false;
            }
            return true;
        }
    }

    /// <summary>天赋维度。</summary>
    public enum TalentKind { Hp, Attack, AttackSpeed, Move, Mana }
}
