using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay.World
{
    /// <summary>
    /// 坠落回收：勇者跌出关卡底部（y &lt; -5）→ 扣 20 血并传回最后落脚点。
    /// 由 LevelBuilder 挂在勇者实例上（勇者 prefab 资产归另一流程所有，不改 prefab 本体）。
    /// 面试点：横版关卡的深渊/断桥惩罚是"扣血回退"而非卡死局外；
    /// "最后落脚点"限频采样且必须真实接地——土狼时间边缘的悬空帧不允许存档，
    /// 否则会把勇者传回半空造成二次坠落循环（平台游戏经典 bug）。
    /// </summary>
    [RequireComponent(typeof(HeroController))]
    public class HeroRespawn : MonoBehaviour
    {
        [SerializeField] private float killY = -5f;      // 关卡底部死亡线
        [SerializeField] private float fallDamage = 20f; // 坠落惩罚
        [SerializeField] private float sampleInterval = 0.2f;

        private HeroController _hero;
        private Rigidbody2D _rb;
        private Vector2 _lastGrounded;
        private float _sampleTimer;

        // 层数字契约：与 HeroController.groundMask（1 << 8）一致
        private const int GroundLayer = 8;

        private void Awake()
        {
            _hero = GetComponent<HeroController>();
            _rb = GetComponent<Rigidbody2D>();
            _lastGrounded = transform.position;
        }

        private void Update()
        {
            // 限频采样最后落脚点（只在真实接地时记录）
            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = sampleInterval;
                if (IsGrounded()) _lastGrounded = transform.position;
            }

            if (transform.position.y < killY) Respawn();
        }

        /// <summary>接地判定：与 HeroController.IsGrounded 同款脚印盒，两处判定不会打架。</summary>
        private bool IsGrounded()
        {
            var box = new Vector2(0.6f, 0.16f);
            var origin = (Vector2)transform.position + Vector2.down * 0.55f;
            return Physics2D.OverlapBox(origin, box, 0f, 1 << GroundLayer) != null;
        }

        private void Respawn()
        {
            // 扣血走统一伤害入口（暂停/无敌帧等状态由 TakeDamage 内部自行拒绝）；
            // 伤害来源取坠落点自身位置——击退方向归零，只剩扣血与传送
            _hero.TakeDamage(new DamageInfo(fallDamage, transform.position, false));

            _rb.velocity = Vector2.zero;
            transform.position = _lastGrounded + Vector2.up * 0.6f; // 略抬高避免贴地穿模
        }
    }
}
