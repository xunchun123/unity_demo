using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 玩家弹丸：触发器碰撞 + 匀速直线飞行 + 穿透计数。
    /// 面试点：弹丸用 Trigger（不阻挡移动），敌人对玩家用 Collider 实体碰撞——
    /// 两种物理形态各司其职，配合 Layer Collision Matrix 分层管理。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class Projectile : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D _rb;
        [SerializeField] private SpriteRenderer _sr;
        [SerializeField] private TrailRenderer _trail;

        private float _damage;
        private bool _isCrit;
        private int _pierceLeft;
        private float _lifetime;

        private void Reset()
        {
            _rb = GetComponent<Rigidbody2D>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
        }

        private void OnEnable() => _lifetime = 0f;

        /// <summary>发射（由武器控制器调用，同时完成伤害暴击结算）。</summary>
        public void Launch(Vector2 dir, float speed, float damage, bool isCrit, int pierce, float lifetime)
        {
            _rb.velocity = dir * speed;
            transform.rotation = Quaternion.LookRotation(Vector3.forward, dir);
            _damage = damage;
            _isCrit = isCrit;
            _pierceLeft = pierce;
            _lifetime = lifetime;
        }

        private void Update()
        {
            // 场景切换过渡/菜单状态下不再飞行结算（池对象跨场景常驻，旧弹丸不应在新场景里乱飞）
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) ReturnToPool();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 命中判定走 IDamageable 接口（见 Damage.cs），弹丸不关心具体是敌人还是别的可破坏物
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;

            target.TakeDamage(new DamageInfo(_damage, transform.position, _isCrit));
            Juice.HitSpark(transform.position, _sr.color);
            AudioService.Instance?.Play(_isCrit ? SfxId.CritHit : SfxId.Hit);

            _pierceLeft--;
            if (_pierceLeft <= 0) ReturnToPool();
        }

        private void ReturnToPool()
        {
            // 防重入：同一帧"命中耗尽穿透"和"寿命到期"可能同时触发，二次 Release 会抛池异常
            if (!gameObject.activeSelf) return;
            PoolService.Instance.Despawn(gameObject);
        }
    }
}
