using StarVortex.Core;
using StarVortex.Enemies;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 法杖火球：直线飞行 + 命中爆炸 + 爽点分支（分裂/燃烧地面）。
    /// 走统一对象池，与旧弹丸同一套生命周期规则。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class Fireball : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D _rb;

        private float _damage;
        private float _burstMul;
        private bool _canSplit;
        private bool _burning;
        private float _lifetime;

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
        }

        private void OnEnable() => _lifetime = 2.4f;

        public void Launch(Vector2 dir, float damage, float burstMul, bool canSplit)
        {
            _rb.velocity = dir * 9.5f;
            _damage = damage;
            _burstMul = burstMul;
            _canSplit = canSplit;
            _lifetime = 2.4f;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.flipX = dir.x < 0;
        }

        private void Update()
        {
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) ReturnToPool();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<EnemyBase>();
            if (target == null) return;

            Vector2 center = transform.position;
            // 主伤害 + 爆裂范围伤害（爆裂范围爽点）
            foreach (var hit in Physics2D.OverlapCircleAll(center, 1.4f * _burstMul, 1 << 7))
            {
                var e = hit.GetComponentInParent<EnemyBase>();
                if (e == null || e.IsDead) continue;
                if (e == target)
                    e.TakeDamage(new DamageInfo(_damage, center, false));
                else
                    e.TakeDamage(new DamageInfo(_damage * 0.6f * _burstMul, center, false)); // 溅射 6 折
            }

            Juice.DeathBurst(center, new Color(1.6f, 0.9f, 0.4f), 10, 0.5f);
            Juice.HitStop(0.03f);                 // 弹体命中也吃一拍轻停帧（与近战手感统一，0.15s 冷却自动节流）
            CameraRig.Instance?.AddTrauma(0.1f);  // 爆炸轻震屏
            AudioService.Instance?.PlayWithPitch(SfxId.CritHit, 0.85f);
            BoonEffectsHookFireball(center);

            if (_canSplit) Split(center);
            ReturnToPool();
        }

        private void BoonEffectsHookFireball(Vector2 center)
        {
            var hero = FindObjectOfType<HeroController>();
            if (hero != null && hero.Boons.Has(BoonIds.StaffBurningGround))
                BurningZone.Spawn(center, 1.8f, 2f, hero.Stats.Attack * 0.4f);
        }

        private void Split(Vector2 center)
        {
            var prefab = ProjectilePool.Get(ProjectilePool.Fireball);
            if (prefab == null) return;
            for (int i = 0; i < 3; i++)
            {
                float ang = -35f + 35f * i;
                var dir = Quaternion.Euler(0, 0, ang) * Vector2.right;
                var go = PoolService.Instance.Spawn(prefab, center, Quaternion.identity);
                var fb = go.GetComponent<Fireball>();
                // 分裂火球不再继续分裂
                fb.SetSubProjectile();
                fb.Launch(dir, _damage * 0.45f, 1f, false);
            }
        }

        /// <summary>分裂产生的子火球（不再分裂，避免递归）。</summary>
        public void SetSubProjectile() => _canSplit = false;

        private void ReturnToPool()
        {
            if (!gameObject.activeSelf) return;
            PoolService.Instance.Despawn(gameObject);
        }
    }
}
