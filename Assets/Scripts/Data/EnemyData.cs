using UnityEngine;

namespace StarVortex.Data
{
    /// <summary>
    /// 敌人配置（ScriptableObject 资产）。
    /// 面试点：三种敌人 = 三份资产（小怪/冲锋怪/坦克），行为代码只有一份 Enemy 类，
    /// 通过数据区分——这是"组合优于继承"的典型应用。
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy_", menuName = "StarVortex/Enemy Data")]
    public class EnemyData : ScriptableObject
    {
        public string displayName = "浮游 drone";

        [Header("战斗")]
        public float maxHp = 30f;
        [Tooltip("接触玩家时造成的伤害")]
        public float contactDamage = 8f;
        [Tooltip("两次接触伤害的最小间隔（秒）")]
        public float contactCooldown = 0.8f;
        [Tooltip("死亡掉落的经验值")]
        public int xpValue = 3;

        [Header("移动")]
        public float moveSpeed = 2.2f;
        [Tooltip("同类敌人之间的分离半径（防止叠成一坨）")]
        public float separationRadius = 0.9f;

        [Header("体型与视觉")]
        public float radius = 0.45f;
        public Sprite sprite;
        [ColorUsage(true, true)] public Color tintColor = new Color(1f, 0.35f, 0.4f, 1f);
        public Sprite deathParticleSprite;

        [Header("出场节奏（由波次导演引用）")]
        [Tooltip("出现权重（按游戏分钟数动态变化，详见 DifficultyCurve）")]
        public float baseWeight = 1f;
    }
}
