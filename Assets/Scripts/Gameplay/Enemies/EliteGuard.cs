using StarVortex.Core;
using StarVortex.Gameplay;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// 精英·石像卫士：BOSS 级精英，双技能轮换。
    /// 技能①地震波：前摇 0.7s（抬手+变色）→ 以自身为中心左右两侧扩散的地面波（6 格 / 0.6s），命中扣 18，冷却 3.5s；
    /// 技能②冲锋撞击：前摇 0.5s（后退半步蓄力）→ 沿勇者方向冲 8 格（速度 12），命中扣 22，撞墙眩晕 0.8s，冷却 2.5s；
    /// 两个技能都在冷却时才缓慢走向勇者（速度 1.8）。
    /// 面试点：双技能"轮换"让玩家的对策必须交替（躲波要跳、躲冲锋要走），单一解法永远不够；
    /// 两个前摇都是独立可读的视觉语言（抬手 vs 后撤），玩家 3 分钟内能建立条件反射。
    /// </summary>
    public class EliteGuard : EnemyBase
    {
        [Header("走位")]
        [SerializeField] private float walkSpeed = 1.8f;        // 缓慢追击（技能冷却时才移动）

        [Header("地震波")]
        [SerializeField] private float quakeWindup = 0.7f;      // 前摇：抬手+变色
        [SerializeField] private float quakeCooldown = 3.5f;    // 冷却
        [SerializeField] private float quakeDamage = 18f;       // 波伤害
        [SerializeField] private float quakeRange = 6f;         // 波扩散距离
        [SerializeField] private float quakeDuration = 0.6f;    // 波存活时长（6 格 / 0.6s = 10 速度）

        [Header("冲锋撞击")]
        [SerializeField] private float chargeWindup = 0.5f;     // 前摇：后退半步蓄力
        [SerializeField] private float chargeCooldown = 2.5f;   // 冷却
        [SerializeField] private float chargeDamage = 22f;      // 撞击伤害
        [SerializeField] private float chargeSpeed = 12f;       // 冲锋速度
        [SerializeField] private float chargeRange = 8f;        // 冲锋最大距离
        [SerializeField] private float stunTime = 0.8f;         // 撞墙眩晕
        [SerializeField] private LayerMask groundMask = 1 << 8; // 与 HeroController 同一地面层约定（撞墙探测）

        /// <summary>行为状态机：走位 → 两个技能的前摇/执行 → 眩晕。</summary>
        private enum State { Walk, QuakeWindup, ChargeWindup, Charging, Stunned }
        private enum Skill { Quake, Charge }
        private State _state = State.Walk;
        private Skill _nextSkill = Skill.Quake;  // 轮换指针：上次放过谁，这次优先另一个
        private float _quakeCd;
        private float _chargeCd;
        private float _stateTimer;
        private Vector2 _chargeDir;              // 冲锋方向（出手瞬间锁定）
        private float _chargeDist;
        private bool _chargeHitHero;             // 每次冲锋只命中一次
        private float _frozenTimer;              // 冻结契约（SetFrozen）：>0 时跳过行为、velocity 清零

        private static readonly Color WindupRed = new Color(1f, 0.3f, 0.22f);
        private static readonly Vector3 BaseScale = Vector3.one;

        public override void Init(Transform hero)
        {
            base.Init(hero);
            _state = State.Walk;
            _nextSkill = Skill.Quake;
            _quakeCd = 0f;
            _chargeCd = 0f;
            _stateTimer = 0f;
            _chargeDist = 0f;
            _chargeHitHero = false;
            _frozenTimer = 0f;
            _contactTimer = 0f;
            transform.localScale = BaseScale;
            if (_sr != null) _sr.color = Color.white;
            if (_sr != null) _sr.flipX = false;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>冻结：打断技能与前摇，颜色/姿态复位，解冻回走位。</summary>
        public override void SetFrozen(float seconds)
        {
            _frozenTimer = Mathf.Max(_frozenTimer, seconds);
            if (_rb != null) _rb.velocity = Vector2.zero;
            transform.localScale = BaseScale;
            if (_sr != null) _sr.color = Color.white;
            _state = State.Walk;
        }

        protected override void Update()
        {
            if (_frozenTimer > 0f)
            {
                _frozenTimer -= Time.deltaTime;
                if (_rb != null) _rb.velocity = Vector2.zero;
                return;
            }
            base.Update();
        }

        protected override void Behaviour()
        {
            if (_rb == null) return;
            if (_sr != null) _sr.flipX = !_facingRight;
            _quakeCd -= Time.deltaTime;
            _chargeCd -= Time.deltaTime;

            switch (_state)
            {
                case State.Walk:
                {
                    // 双技能都在冷却时才移动：轮换指针优先，另一手好了也别闲着
                    if (_nextSkill == Skill.Quake && _quakeCd <= 0f) StartQuake();
                    else if (_nextSkill == Skill.Charge && _chargeCd <= 0f) StartCharge();
                    else if (_quakeCd <= 0f) StartQuake();
                    else if (_chargeCd <= 0f) StartCharge();
                    else
                    {
                        float toward = Mathf.Sign(_hero.position.x - transform.position.x);
                        _rb.velocity = new Vector2(toward * walkSpeed, _rb.velocity.y);
                    }
                    break;
                }

                case State.QuakeWindup:
                {
                    // 前摇 0.7s：抬手（纵向拉伸）+ 变色（渐变红），站定给玩家反应窗口
                    _rb.velocity = new Vector2(0f, _rb.velocity.y);
                    _stateTimer -= Time.deltaTime;
                    float k = Mathf.Clamp01(1f - _stateTimer / Mathf.Max(0.01f, quakeWindup));
                    if (_sr != null) _sr.color = Color.Lerp(Color.white, WindupRed, k);
                    transform.localScale = new Vector3(1f, 1f + 0.18f * k, 1f);

                    if (_stateTimer <= 0f)
                    {
                        transform.localScale = BaseScale;
                        if (_sr != null) _sr.color = Color.white;
                        SpawnQuakeWaves();
                        _quakeCd = quakeCooldown;
                        _nextSkill = Skill.Charge; // 轮换：下一次优先冲锋
                        _state = State.Walk;
                    }
                    break;
                }

                case State.ChargeWindup:
                {
                    // 前摇 0.5s：后退半步蓄力（起手一次反向冲量，摩擦自然收住），微微泛红
                    _stateTimer -= Time.deltaTime;
                    float k = Mathf.Clamp01(1f - _stateTimer / Mathf.Max(0.01f, chargeWindup));
                    if (_sr != null) _sr.color = Color.Lerp(Color.white, WindupRed, k * 0.6f);

                    if (_stateTimer <= 0f)
                    {
                        if (_sr != null) _sr.color = Color.white;
                        // 冲锋方向以出手瞬间为准（蓄力期间走位可以骗它冲空）
                        _chargeDir = new Vector2(_facingRight ? 1f : -1f, 0f);
                        _chargeDist = 0f;
                        _chargeHitHero = false;
                        _state = State.Charging;
                    }
                    break;
                }

                case State.Charging:
                {
                    // 冲锋：匀速直线，撞墙（身体高度探测）或冲满 8 格即止
                    _rb.velocity = _chargeDir * chargeSpeed;
                    _chargeDist += chargeSpeed * Time.deltaTime;

                    TryChargeHitHero();

                    // 撞墙探测：身体高度前方一小盒（探测不到地面层时由距离上限与碰撞回调兜底）
                    var wallProbeCenter = (Vector2)transform.position + Vector2.up * 0.5f + _chargeDir * 0.55f;
                    bool wallAhead = Physics2D.OverlapBox(wallProbeCenter, new Vector2(0.35f, 0.5f), 0f, groundMask) != null;
                    if (wallAhead || _chargeDist >= chargeRange)
                    {
                        _rb.velocity = Vector2.zero;
                        if (wallAhead) EnterStun(); // 冲满距离没撞墙则不眩晕
                        else
                        {
                            _chargeCd = chargeCooldown;
                            _state = State.Walk;
                        }
                    }
                    break;
                }

                case State.Stunned:
                {
                    // 眩晕：站桩发懵（轻微抖动），这是玩家最安全的输出窗口
                    _rb.velocity = Vector2.zero;
                    _stateTimer -= Time.deltaTime;
                    float shake = 1f + 0.04f * Mathf.Sin(Time.time * 40f);
                    transform.localScale = new Vector3(shake, 1f, 1f);
                    if (_stateTimer <= 0f)
                    {
                        transform.localScale = BaseScale;
                        _chargeCd = chargeCooldown;
                        _state = State.Walk;
                    }
                    break;
                }
            }
        }

        private void StartQuake()
        {
            _state = State.QuakeWindup;
            _stateTimer = quakeWindup;
        }

        private void StartCharge()
        {
            _state = State.ChargeWindup;
            _stateTimer = chargeWindup;
            Juice.PunchScale(transform, 0.16f, 0.35f); // 冲锋前摇挤压 telegraph（冲锋期间 Scale 无其他写入者，安全）
            // 后退半步：与冲锋方向相反的一次小冲量（摩擦自然收住，读起来就是"蓄力后撤"）
            float toward = Mathf.Sign(_hero.position.x - transform.position.x);
            _rb.velocity = new Vector2(-toward * 3.5f, _rb.velocity.y);
        }

        /// <summary>地震波：以自身为中心左右两侧各扩散一道地面波。</summary>
        private void SpawnQuakeWaves()
        {
            // 波盒贴在脚面高度（+0.1）：足够低像贴地冲击波，又保证与勇者下半身判定盒相交
            var center = (Vector2)transform.position + Vector2.up * 0.1f;
            float waveSpeed = quakeRange / Mathf.Max(0.01f, quakeDuration);
            QuakeWave.Spawn(center + Vector2.right * 0.7f, Vector2.right, waveSpeed, quakeDamage, quakeDuration);
            QuakeWave.Spawn(center + Vector2.left * 0.7f, Vector2.left, waveSpeed, quakeDamage, quakeDuration);
            AudioService.Instance?.PlayWithPitch(SfxId.CritHit, 0.6f);
            CameraRig.Instance?.AddTrauma(0.15f);
        }

        /// <summary>冲锋命中勇者：身前一盒轮询（不赌层矩阵），每次冲锋只结算一次。</summary>
        private void TryChargeHitHero()
        {
            if (_chargeHitHero) return;
            var center = (Vector2)transform.position + _chargeDir * 0.6f;
            foreach (var hit in Physics2D.OverlapBoxAll(center, new Vector2(0.9f, 1.1f), 0f))
            {
                var hero = hit.GetComponentInParent<HeroController>();
                if (hero == null || hero.IsDead) continue;
                _chargeHitHero = true;
                hero.TakeDamage(new DamageInfo(chargeDamage, transform.position, false));
                Juice.HitSpark(hero.transform.position, WindupRed);
                return;
            }
        }

        /// <summary>撞墙眩晕：冲锋打断，短暂站桩（回血窗口）。</summary>
        private void EnterStun()
        {
            _state = State.Stunned;
            _stateTimer = stunTime;
            CameraRig.Instance?.AddTrauma(0.12f);
        }

        /// <summary>撞墙的物理快路径（层矩阵开启 Enemy↔场景碰撞体时生效）：撞到的不是勇者就算撞墙。</summary>
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_state != State.Charging) return;
            if (collision.collider == null) return;
            if (collision.collider.GetComponentInParent<HeroController>() != null) return;
            _rb.velocity = Vector2.zero;
            EnterStun();
        }

        /// <summary>击退：直接叠加速度，地面摩擦与重力负责回收。</summary>
        public override void ApplyKnockback(Vector2 dir, float force)
        {
            if (IsDead || _rb == null) return;
            _rb.velocity += dir * force;
        }

        protected override void OnDeath()
        {
            // 死亡清理：回池（未走池生成的实例由 Despawn 兜底 Destroy）
            if (PoolService.Instance != null) PoolService.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 地震波：从精英两侧匀速移出的地面波小盒（触发器 + 匀速直线 + 0.6s 自毁）。
    /// 命中判定双保险：每帧 OverlapBox 轮询勇者（不依赖层矩阵）+ OnTriggerEnter2D 快路径，单波只命中一次。
    /// </summary>
    internal class QuakeWave : MonoBehaviour
    {
        private float _damage;
        private float _lifetime;
        private bool _hitHero;
        private static readonly Color WaveColor = new Color(1.5f, 0.7f, 0.3f, 0.9f); // 地面波橙

        /// <summary>生成一道地面波（现场建 GameObject，短寿命特效不值得池化）。</summary>
        public static QuakeWave Spawn(Vector2 pos, Vector2 dir, float speed, float damage, float lifetime)
        {
            var go = new GameObject("QuakeWave");
            go.transform.position = pos;
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) go.layer = enemyLayer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.velocity = dir * speed;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.55f, 0.35f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpritePool.GlowDisc; // 光晕盘横向拉扁，读起来就是贴地扩散的冲击波
            sr.color = WaveColor;
            sr.sortingOrder = 8;
            sr.transform.localScale = new Vector3(1.6f, 0.55f, 1f);

            var wave = go.AddComponent<QuakeWave>();
            wave._damage = damage;
            wave._lifetime = lifetime;
            return wave;
        }

        private void Update()
        {
            // 寿命到点自毁（速度 × 0.6s ≈ 6 格射程）
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) { Destroy(gameObject); return; }

            if (_hitHero) return;
            foreach (var hit in Physics2D.OverlapBoxAll(transform.position, new Vector2(0.6f, 0.4f), 0f))
            {
                var hero = hit.GetComponentInParent<HeroController>();
                if (hero == null || hero.IsDead) continue;
                _hitHero = true;
                hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
                Juice.HitSpark(hero.transform.position, WaveColor);
                return;
            }
        }

        /// <summary>触发器快路径（层矩阵开启 Player↔Enemy 时生效）。</summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_hitHero) return;
            var hero = other.GetComponentInParent<HeroController>();
            if (hero == null || hero.IsDead) return;
            _hitHero = true;
            hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
            Juice.HitSpark(hero.transform.position, WaveColor);
        }
    }
}
