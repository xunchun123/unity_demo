using UnityEngine;

namespace StarVortex.Data
{
    /// <summary>
    /// 武器配置（ScriptableObject 资产）。
    /// 面试点：数值策划视角——武器数值不进代码，改平衡只动资产不重新编译；
    /// 新增武器 = 新建一份 SO 资产 + 挂到升级池，零代码改动。
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_", menuName = "StarVortex/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("基础")]
        public string displayName = "脉冲步枪";
        [TextArea] public string description = "自动锁定最近的敌人射击";

        [Header("射击")]
        [Tooltip("两次射击的间隔（秒）")]
        public float fireInterval = 0.45f;
        [Tooltip("一次发射的弹丸数量（多弹幕时呈扇形散开）")]
        public int projectileCount = 1;
        [Tooltip("多弹幕时的总散射角度（度）")]
        public float spreadAngle = 12f;

        [Header("弹丸")]
        public float projectileSpeed = 14f;
        public float projectileDamage = 12f;
        [Tooltip("可穿透的敌人数（1 = 命中一个即消失）")]
        public int pierce = 1;
        public float projectileLifetime = 1.6f;

        [Header("索敌")]
        [Tooltip("自动索敌半径（超出则不射击）")]
        public float targetRadius = 9f;

        [Header("视觉")]
        public Sprite projectileSprite;
        public Color projectileColor = new UnityEngine.Color(1f, 0.95f, 0.6f, 1f);
        [ColorUsage(true, true)] public Color projectileEmission = new Color(1.6f, 1.5f, 0.8f, 1f);
    }
}
