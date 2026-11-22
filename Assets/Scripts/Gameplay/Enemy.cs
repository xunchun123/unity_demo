using StarVortex.Core;
using StarVortex.Data;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 敌人：追击 AI + 同伴分离 + 接触伤害 + 死亡结算。
    /// 面试点：三种敌人共用这一个类，靠 EnemyData 资产区分——组合优于继承；
    /// 分离力（separation）只取最多 6 个邻居且 0.15s 才算一次，
    /// 是同屏百怪不卡帧的关键（全量 O(n²) 两两分离在 100 怪时是 1 万次/帧）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class Enemy : MonoBehaviour, IDamageable
    {
        [SerializeField] private Rigidbody2D _rb;
        [SerializeField] private SpriteRenderer _sr;

        public EnemyData Data { get; private set; }
        public bool IsElite { get; private set; }

        private float _hp;
        private float _contactTimer;
        private float _separationTimer;
        private Transform _player;
        // 层掩码用实例懒初始化（static 会在层注册前跑，拿到的层号是 -1）
        private int _enemyLayerMask = -1;

        /// <summary>同屏存活计数（波次导演据此限流，保护帧率）。</summary>
        public static int AliveCount;

        private void Reset()
        {
            _rb = GetComponent<Rigidbody2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.drag = 0f;
        }

        private void OnEnable() => AliveCount++;
        private void OnDisable() => AliveCount--;

        /// <summary>从池中取出后的初始化（波次导演调用）。</summary>
        public void Init(EnemyData data, bool isElite, Transform player)
        {
            Data = data;
            IsElite = isElite;
            _player = player;
            _hp = data.maxHp * (isElite ? 4f : 1f);
            _contactTimer = 0f;
            _separationTimer = Random.value * 0.15f; // 错峰：全体验算同帧会挤在一起

            float scale = data.radius * (isElite ? 1.4f : 1f) * 2f;
            transform.localScale = Vector3.one * scale;
            _sr.color = isElite ? data.tintColor * 1.6f : data.tintColor;
        }

        private void FixedUpdate()
        {
            if (_player == null || GameManager.Instance.State != GameState.Playing) { _rb.velocity = Vector2.zero; return; }

            // 1) 追击力：朝玩家的恒定速度（质量归一后 velocity = 方向 * 速度）
            Vector2 toPlayer = _player.position - transform.position;
            Vector2 desired = toPlayer.normalized * Data.moveSpeed * (IsElite ? 0.8f : 1f);

            // 2) 分离力：和同伴保持距离（节流 0.15s，最多采样 6 个邻居）
            _separationTimer -= Time.fixedDeltaTime;
            if (_separationTimer <= 0f)
            {
                _separationTimer = 0.15f;
                _separationPush = ComputeSeparation();
            }

            _rb.velocity = desired + _separationPush;
        }

        private Vector2 _separationPush;

        /// <summary>计算分离推力：邻居越近推得越狠（平方反比），上限钳制防止爆炸。</summary>
        private Vector2 ComputeSeparation()
        {
            if (_enemyLayerMask < 0)
                _enemyLayerMask = 1 << LayerMask.NameToLayer("Enemy");

            Vector2 push = Vector2.zero;
            int sampled = 0;
            Vector2 self = transform.position;
            var hits = Physics2D.OverlapCircleAll(self, Data.separationRadius, _enemyLayerMask);
            for (int i = 0; i < hits.Length && sampled < 6; i++)
            {
                if (hits[i].attachedRigidbody == _rb) continue;
                Vector2 away = self - (Vector2)hits[i].transform.position;
                float dist = Mathf.Max(0.05f, away.magnitude);
                push += away.normalized * (1f / dist);
                sampled++;
            }
            return Vector2.ClampMagnitude(push * 2.5f, 3.5f);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // 接触伤害：持续接触时按冷却时间扣血
            _contactTimer -= Time.deltaTime;
            if (_contactTimer > 0f) return;
            if (!other.TryGetComponent<IDamageable>(out var player)) return;

            _contactTimer = Data.contactCooldown;
            player.TakeDamage(new DamageInfo(Data.contactDamage, transform.position));
        }

        // ---------- IDamageable ----------

        public void TakeDamage(in DamageInfo damage)
        {
            if (_hp <= 0f) return;
            _hp -= damage.Amount;

            Juice.Flash(_sr, Color.white);
            DamagePop.Spawn(transform.position + Vector3.up * 0.5f, Mathf.RoundToInt(damage.Amount), damage.IsCrit);

            if (_hp <= 0f) Die();
        }

        private void Die()
        {
            int xp = Data.xpValue * (IsElite ? 6 : 1);
            GameEvents.PublishEnemyDied(xp, transform.position);
            GameManager.Instance.AddKill();

            // 打击感：死亡规模越大，停帧越明显（小怪 0ms / 精英 60ms）
            if (IsElite) Juice.HitStop(0.06f);
            CameraRig.Instance?.AddTrauma(IsElite ? 0.22f : 0.07f);
            Juice.DeathBurst(transform.position, _sr.color, IsElite ? 26 : 12, Data.radius);
            AudioService.Instance?.Play(IsElite ? SfxId.EliteKill : SfxId.Kill);

            XPGem.SpawnAt(transform.position, xp);
            PoolService.Instance.Despawn(gameObject);
        }
    }
}
