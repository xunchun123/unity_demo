using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>燃烧地面：范围内怪物按冷却持续掉血（火焰冲刺/燃烧地面爽点的载体）。</summary>
    public class BurningZone : MonoBehaviour
    {
        private float _dps;
        private float _life;
        private float _tickTimer;

        /// <summary>创建一片燃烧区（半径/时长/每秒伤害）。</summary>
        public static BurningZone Spawn(Vector3 pos, float radius, float duration, float dps)
        {
            var go = new GameObject("BurningZone");
            go.transform.position = pos;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = radius;
            col.isTrigger = true;
            var zone = go.AddComponent<BurningZone>();
            zone._dps = dps;
            zone._life = duration;
            // 视觉：橙色光晕（复用爆点粒子的静态贴图）
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpritePool.GlowDisc;
            if (sr.sprite != null)
            {
                float d = sr.sprite.bounds.size.x;
                sr.transform.localScale = Vector3.one * (radius * 2f / Mathf.Max(0.01f, d));
                sr.color = new Color(1.3f, 0.6f, 0.25f, 0.4f);
                sr.sortingOrder = -5;
            }
            return zone;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            _tickTimer -= Time.deltaTime;
            if (_tickTimer > 0f) return;
            var enemy = other.GetComponentInParent<StarVortex.Enemies.EnemyBase>();
            if (enemy == null || enemy.IsDead) return;
            _tickTimer = 0.4f;
            enemy.TakeDamage(new DamageInfo(_dps * 0.4f, transform.position, false));
            Juice.HitSpark(enemy.transform.position, new Color(1.4f, 0.7f, 0.3f));
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>常用共享精灵的静态快捷入口：优先走 Resources（打包后可用），编辑器下缓存。</summary>
    public static class SpritePool
    {
        private static Sprite _glowDisc;
        private static Sprite[] _slashes;
        public static Sprite GlowDisc => _glowDisc != null
            ? _glowDisc : _glowDisc = Resources.Load<Sprite>("Sprites/glow_disc");
        public static Sprite[] Slashes => _slashes ??= new[]
        {
            Resources.Load<Sprite>("Sprites/slash_0"),
            Resources.Load<Sprite>("Sprites/slash_1"),
            Resources.Load<Sprite>("Sprites/slash_2"),
        };
    }
}
