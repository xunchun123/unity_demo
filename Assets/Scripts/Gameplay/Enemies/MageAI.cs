using StarVortex.Core;
using StarVortex.Gameplay;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// 骷髅法师：远程施法怪。
    /// 行为环：与勇者保持 4-7 格走位（太近后退/太远靠近）→ 施法前摇 0.6s（法杖顶红光渐变）→
    /// 直线火球射向勇者当前位置 → 冷却 2.2s。
    /// 面试点：远程怪的威胁感来自"弹道可读"——火球永远直线飞向出手瞬间的位置，
    /// 玩家跳跃/冲刺就能躲；法师不预判走位，所以走位永远是对策（这是手感公平性的底线）。
    /// </summary>
    public class MageAI : EnemyBase
    {
        [Header("走位")]
        [SerializeField] private float walkSpeed = 2f;      // 地面移动速度
        [SerializeField] private float keepMin = 4f;        // 最近保持距离（太近后退）
        [SerializeField] private float keepMax = 7f;        // 最远保持距离（太远靠近）
        [SerializeField] private float castRange = 8f;      // 施法最大距离

        [Header("施法")]
        [SerializeField] private float castWindup = 0.6f;   // 前摇（法杖顶红光渐变）
        [SerializeField] private float castCooldown = 2.2f; // 冷却
        [SerializeField] private float boltSpeed = 7f;      // 火球飞行速度
        [SerializeField] private float boltDamage = 12f;    // 火球伤害

        private enum State { Reposition, Windup }
        private State _state = State.Reposition;
        private float _castCdTimer;
        private float _stateTimer;
        private Color _baseColor = Color.white;
        private float _frozenTimer;     // 冻结契约（SetFrozen）：>0 时跳过行为、velocity 清零

        private static readonly Color WindupRed = new Color(1f, 0.25f, 0.2f); // 前摇红光

        public override void Init(Transform hero)
        {
            base.Init(hero);
            _state = State.Reposition;
            _castCdTimer = 0.8f;        // 出场给一小段缓冲再开始施法
            _stateTimer = 0f;
            _frozenTimer = 0f;
            _contactTimer = 0f;
            _baseColor = _sr != null ? _sr.color : Color.white;
            if (_sr != null) _sr.color = Color.white;
            if (_sr != null) _sr.flipX = false;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>冻结：打断施法（颜色复位），解冻后重新走位。</summary>
        public override void SetFrozen(float seconds)
        {
            _frozenTimer = Mathf.Max(_frozenTimer, seconds);
            if (_rb != null) _rb.velocity = Vector2.zero;
            if (_sr != null) _sr.color = Color.white;
            _state = State.Reposition;
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
            _castCdTimer -= Time.deltaTime;

            float dx = Mathf.Abs(_hero.position.x - transform.position.x); // 横向距离（走位只看横向）

            switch (_state)
            {
                case State.Reposition:
                {
                    // 走位：保持 4-7 格——太近后退、太远靠近、带内站定
                    float toward = Mathf.Sign(_hero.position.x - transform.position.x);
                    if (dx < keepMin) _rb.velocity = new Vector2(-toward * walkSpeed, _rb.velocity.y);
                    else if (dx > keepMax) _rb.velocity = new Vector2(toward * walkSpeed, _rb.velocity.y);
                    else _rb.velocity = new Vector2(0f, _rb.velocity.y);

                    // 带内且冷却好了：站定开始施法前摇
                    if (_castCdTimer <= 0f && dx <= castRange)
                    {
                        _state = State.Windup;
                        _stateTimer = castWindup;
                        Juice.PunchScale(transform, 0.14f, 0.3f); // 前摇起手挤压 telegraph：看得清"要挨打了"
                    }
                    break;
                }

                case State.Windup:
                {
                    // 前摇：法杖顶红光渐变（整体 tint 变红），站定不动给玩家反应窗口
                    _rb.velocity = new Vector2(0f, _rb.velocity.y);
                    _stateTimer -= Time.deltaTime;
                    float k = Mathf.Clamp01(1f - _stateTimer / Mathf.Max(0.01f, castWindup));
                    if (_sr != null) _sr.color = Color.Lerp(_baseColor, WindupRed, k);

                    if (_stateTimer <= 0f)
                    {
                        if (_sr != null) _sr.color = _baseColor;
                        FireBolt();
                        _castCdTimer = castCooldown;
                        _state = State.Reposition;
                    }
                    break;
                }
            }
        }

        /// <summary>发射火球：朝勇者当前位置直线飞行（不预判，跳跃可躲）。</summary>
        private void FireBolt()
        {
            if (_hero == null) return;
            var origin = (Vector2)transform.position
                         + Vector2.up * 0.7f
                         + Vector2.right * (_facingRight ? 0.4f : -0.4f); // 法杖顶
            var dir = ((Vector2)_hero.position - origin).normalized;
            MageBolt.Spawn(origin, dir, boltSpeed, boltDamage, _sr != null ? _sr.sprite : null);
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 0.7f); // 低音高的敌方法师弹道
        }

        /// <summary>击退：地面单位，直接叠加速度，重力与摩擦负责回收。</summary>
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
    /// 法师火球：直线匀速飞行的小弹道（独立于 ProjectilePool——怪物弹道不走玩家弹池注册表，
    /// 现场建、命中/撞墙/超时即毁）。命中判定双保险：
    /// ① 每帧 OverlapBox 轮询勇者（不依赖层碰撞矩阵配置）；② OnTriggerEnter2D 快路径。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class MageBolt : MonoBehaviour
    {
        [SerializeField] private LayerMask groundMask = 1 << 8; // 与 HeroController 同一地面层约定（撞墙销毁）

        private float _damage;
        private float _lifetime;
        private bool _consumed; // 防重入：同一帧命中/撞墙/超时只结算一次

        private static readonly Color BoltColor = new Color(1.7f, 0.8f, 0.35f); // HDR 橙，喂 Bloom

        /// <summary>生成一颗火球（现场建 GameObject，短寿命弹道不需要池化）。</summary>
        public static MageBolt Spawn(Vector2 pos, Vector2 dir, float speed, float damage, Sprite visual = null)
        {
            var go = new GameObject("MageBolt");
            go.transform.position = pos;
            // 挂 Enemy 层（若已注册）：勇者攻击查询 1<<7 时会扫到它，但 GetComponentInParent<EnemyBase>
            // 为 null 会被安全跳过；层矩阵若已开启 Player↔Enemy，还能吃到触发器快路径
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) go.layer = enemyLayer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.velocity = dir * speed;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.14f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = visual != null ? visual : SpritePool.GlowDisc; // 没贴图就用光晕盘兜底
            sr.color = BoltColor;
            sr.sortingOrder = 12;

            var bolt = go.AddComponent<MageBolt>();
            bolt._damage = damage;
            bolt._lifetime = 2.2f; // 超时保险丝：撞墙探测失灵时也绝不永生
            return bolt;
        }

        private void Update()
        {
            if (_consumed) return;

            // 超时销毁
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) { Consume(); return; }

            // 命中勇者：OverlapBox 不带层掩码（不赌碰撞矩阵配置），靠组件过滤
            foreach (var hit in Physics2D.OverlapBoxAll(transform.position, new Vector2(0.3f, 0.3f), 0f))
            {
                var hero = hit.GetComponentInParent<HeroController>();
                if (hero == null || hero.IsDead) continue;
                hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
                Juice.HitSpark(transform.position, BoltColor);
                Consume();
                return;
            }

            // 撞墙销毁：地面层探测（掩码与勇者地面判定同源；配错时由超时兜底）
            if (Physics2D.OverlapBox(transform.position, new Vector2(0.26f, 0.26f), 0f, groundMask) != null)
            {
                Juice.HitSpark(transform.position, BoltColor);
                Consume();
            }
        }

        /// <summary>触发器快路径（层矩阵开启 Player↔Enemy 时生效，提前于轮询命中）。</summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_consumed) return;
            var hero = other.GetComponentInParent<HeroController>();
            if (hero == null || hero.IsDead) return;
            hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
            Juice.HitSpark(transform.position, BoltColor);
            Consume();
        }

        /// <summary>统一出口：标记已结算并销毁（现场建的对象直接 Destroy，不回池）。</summary>
        private void Consume()
        {
            _consumed = true;
            Destroy(gameObject);
        }
    }
}
