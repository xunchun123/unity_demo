using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>
    /// 武器定义（SO 资产）：主攻击参数 + 该武器的爽点卡池引用。
    /// 注意：本类必须独立成文件——曾放在 BoonIds.cs 里作为"次类"（类名≠文件名），
    /// AssetDatabase.CreateAsset 写出的资产 m_Script 绑定到文件主类失败 → fileID:0 →
    /// LoadAssetAtPath 永远 null（与 GemPool/PopPool 的场景级次类坑同源，教训见 GemPool.cs）。
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponKit", menuName = "StarVortex/Weapon Kit")]
    public class WeaponKit : ScriptableObject
    {
        public WeaponKind kind;
        [TextArea] public string displayName;
        [TextArea] public string description;

        [Header("主攻击")]
        public float attackInterval = 0.4f;   // 主攻击冷却
        public float attackDamageMul = 1f;    // 相对勇者基础攻击的倍率

        [Header("该武器的爽点卡池（不含通用池）")]
        public string[] boonPool;
    }
}
