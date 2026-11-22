using StarVortex.Core;
using StarVortex.Data;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 武器控制器：自动索敌 + 定时射击。
    /// 面试点：索敌用 Physics2D.OverlapCircleAll + 层过滤，节流到 0.1 秒一次
    /// （割草场景敌人几十上百个，每帧全场景查询是隐形性能黑洞）。
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PlayerController _player;
        [SerializeField] private Projectile _projectilePrefab;

        private WeaponData _data;
        private PlayerStats _stats;
        private float _fireTimer;
        private float _targetSearchTimer;
        private Transform _cachedTarget;

        /// <summary>
        /// 敌人层掩码（实例字段而非 static：static 初始化早于层注册，
        /// LayerMask.NameToLayer 会拿到 -1 导致位移结果错误——经典的初始化顺序陷阱）。
        /// </summary>
        private int _enemyLayerMask = -1;

        private const float TargetSearchInterval = 0.1f;

        /// <summary>注入配置（出生武器或未来多武器系统）。</summary>
        public void Equip(WeaponData data)
        {
            _data = data;
            _stats = _player.Stats;
            _fireTimer = 0.2f; // 开局稍作延迟，别贴脸就糊脸
        }

        private void Update()
        {
            if (_data == null || GameManager.Instance.State != GameState.Playing) return;

            // 索敌节流：每 0.1s 刷新一次最近目标
            _targetSearchTimer -= Time.deltaTime;
            if (_targetSearchTimer <= 0f)
            {
                _targetSearchTimer = TargetSearchInterval;
                _cachedTarget = FindNearestEnemy();
            }

            _fireTimer -= Time.deltaTime;
            if (_fireTimer <= 0f && _cachedTarget != null)
            {
                Fire(_cachedTarget.position);
                _fireTimer = _data.fireInterval / Mathf.Max(0.01f, _stats.fireRateMul);
            }
        }

        /// <summary>取索敌半径内最近的敌人；没有则清空目标停止射击（省性能也省弹丸）。</summary>
        private Transform FindNearestEnemy()
        {
            if (_enemyLayerMask < 0)
                _enemyLayerMask = 1 << LayerMask.NameToLayer("Enemy");

            Vector2 origin = transform.position;
            var hits = Physics2D.OverlapCircleAll(origin, _data.targetRadius, _enemyLayerMask);
            Transform best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                float sqr = ((Vector2)hits[i].transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = hits[i].transform; }
            }
            return best;
        }

        /// <summary>朝目标发射弹幕：多弹丸按 spreadAngle 扇形展开。</summary>
        private void Fire(Vector2 targetPos)
        {
            Vector2 origin = transform.position;
            Vector2 baseDir = (targetPos - origin).normalized;
            int count = _data.projectileCount + Mathf.Max(0, _stats.projectileAdd);
            float spreadRad = _data.spreadAngle * Mathf.Deg2Rad;

            for (int i = 0; i < count; i++)
            {
                // 奇数弹幕居中，偶数弹幕偏移半个步进
                float offset = count > 1
                    ? Mathf.Lerp(-spreadRad * 0.5f, spreadRad * 0.5f, i / (float)(count - 1))
                    : 0f;
                Vector2 dir = Rotate(baseDir, offset);
                SpawnProjectile(origin, dir);
            }

            AudioService.Instance?.Play(SfxId.Shoot);
            Juice.MuzzleFlash(origin, baseDir);
        }

        private static Vector2 Rotate(Vector2 v, float rad)
        {
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        private void SpawnProjectile(Vector2 origin, Vector2 dir)
        {
            var go = PoolService.Instance.Spawn(_projectilePrefab.gameObject, origin, Quaternion.identity);
            var proj = go.GetComponent<Projectile>();
            bool crit = Random.value < _stats.critChance;
            proj.Launch(dir, _data.projectileSpeed, _data.projectileDamage * _stats.damageMul * (crit ? 2f : 1f),
                crit, _data.pierce, _data.projectileLifetime);
        }
    }
}
