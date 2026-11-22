using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>一次伤害的完整信息（struct 传参，零 GC 分配）。</summary>
    public struct DamageInfo
    {
        public float Amount;
        public Vector2 SourcePosition;
        public bool IsCrit;

        public DamageInfo(float amount, Vector2 source, bool isCrit = false)
        {
            Amount = amount;
            SourcePosition = source;
            IsCrit = isCrit;
        }
    }

    /// <summary>
    /// 可受击接口：弹丸只认接口不认具体类（玩家/敌人/未来可破坏物都实现它）。
    /// 面试点：依赖倒置——投射物逻辑与"谁被击中"完全解耦，新增可受伤对象不需要改弹丸代码。
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo damage);
    }
}
