using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// 蝙蝠：飞行骚扰怪。
    /// 行为环：悬浮正弦漂浮 → 勇者进入 8 格 → 悬停瞄准 0.5s（翅膀抖动）→ 直线俯冲（冲过即止）→ 拉高回巡航高度 → 循环。
    /// 面试点：飞行怪 gravityScale=0 全程速度自控——漂浮用"目标点弹簧"而不是直接挪 transform，
    /// 击退/冻结都表现为对 velocity 的扰动，弹簧会自然把它拉回巡航点，不需要额外的状态修复。
    /// </summary>
    public class Bat : EnemyBase
    {
        [Header("巡航漂浮")]
        [SerializeField] private float hoverStiffness = 4f;   // 弹簧刚度：偏离巡航点越远拉回越快
        [SerializeField] private float bobAmplitude = 0.35f;  // 正弦浮动幅度
        [SerializeField] private float bobFrequency = 2.2f;   // 正弦浮动频率

        [Header("俯冲")]
        [SerializeField] private float detectRange = 8f;      // 索敌视野
        [SerializeField] private float aimTime = 0.5f;        // 悬停瞄准前摇
        [SerializeField] private float diveSpeed = 9f;        // 俯冲速度
        [SerializeField] private float maxDiveDist = 12f;     // 俯冲最长距离（冲过目标即止的保险丝）

        /// <summary>行为状态机：巡航 → 瞄准 → 俯冲 → 拉高。</summary>
        private enum State { Cruise, Aim, Dive, Recover }
        private State _state = State.Cruise;
        private Vector2 _anchor;        // 巡航锚点（Init 时的位置）
        private float _bobPhase;        // 正弦浮动相位
        private float _stateTimer;
        private Vector2 _diveTarget;    // 俯冲锁定点（出手瞬间的勇者位置，追旧不追新）
        private Vector2 _diveDir;
        private float _diveDist;
        private Vector3 _baseScale = Vector3.one;
        private float _frozenTimer;     // 冻结契约（SetFrozen）：>0 时跳过行为、velocity 清零

        public override void Init(Transform hero)
        {
            base.Init(hero);
            _state = State.Cruise;
            _anchor = transform.position;   // 巡航中心 = 每次出场的落点
            _bobPhase = Random.value * Mathf.PI * 2f; // 错峰：同屏多只不同步浮动
            _stateTimer = 0f;
            _diveDist = 0f;
            _frozenTimer = 0f;
            _contactTimer = 0f;
            _baseScale = transform.localScale;
            if (_sr != null) _sr.flipX = false;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>冻结：冻结期间悬停原地（gravityScale=0 不会坠落），解冻回巡航。</summary>
        public override void SetFrozen(float seconds)
        {
            _frozenTimer = Mathf.Max(_frozenTimer, seconds);
            if (_rb != null) _rb.velocity = Vector2.zero;
            transform.localScale = _baseScale;
            _state = State.Cruise;
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

            switch (_state)
            {
                case State.Cruise:
                {
                    // 弹簧 + 正弦：漂浮点是巡航点上下摆动，velocity 朝它收敛
                    _bobPhase += Time.deltaTime * bobFrequency;
                    var hoverPoint = _anchor + Vector2.up * (Mathf.Sin(_bobPhase) * bobAmplitude);
                    _rb.velocity = (hoverPoint - (Vector2)transform.position) * hoverStiffness;

                    if (_hero != null &&
                        Vector2.Distance(transform.position, _hero.position) <= detectRange)
                    {
                        _state = State.Aim;
                        _stateTimer = aimTime;
                    }
                    break;
                }

                case State.Aim:
                {
                    // 悬停瞄准（前摇）：锁死锚点小幅弹簧 + 翅膀抖动（scale 高频颤动），告诉玩家"要冲了"
                    _stateTimer -= Time.deltaTime;
                    _rb.velocity = (_anchor - (Vector2)transform.position) * hoverStiffness;
                    float jitter = 1f + 0.05f * Mathf.Sin(Time.time * 55f);
                    transform.localScale = new Vector3(_baseScale.x, _baseScale.y * jitter, _baseScale.z);

                    if (_stateTimer <= 0f)
                    {
                        transform.localScale = _baseScale;
                        _diveTarget = _hero != null ? _hero.position : transform.position;
                        _diveDir = (_diveTarget - (Vector2)transform.position).normalized;
                        if (_diveDir.sqrMagnitude < 0.001f) _diveDir = Vector2.down;
                        _state = State.Dive;
                        _diveDist = 0f;
                    }
                    break;
                }

                case State.Dive:
                {
                    // 俯冲：直线匀速冲向锁定点，"冲过即止"——越过目标点的瞬间拉高
                    _rb.velocity = _diveDir * diveSpeed;
                    _diveDist += diveSpeed * Time.deltaTime;
                    bool passed = Vector2.Dot((Vector2)transform.position - _diveTarget, _diveDir) > 0f;
                    if (passed || _diveDist >= maxDiveDist)
                    {
                        _state = State.Recover;
                    }
                    break;
                }

                case State.Recover:
                {
                    // 拉高回巡航高度：回锚点弹簧，到位后重新进入漂浮循环
                    _rb.velocity = (_anchor - (Vector2)transform.position) * hoverStiffness * 0.8f;
                    if (((Vector2)transform.position - _anchor).sqrMagnitude < 0.25f)
                        _state = State.Cruise;
                    break;
                }
            }
        }

        /// <summary>击退：飞行怪更飘（×1.5），弹簧会在巡航/拉高阶段自然收敛回轨道。</summary>
        public override void ApplyKnockback(Vector2 dir, float force)
        {
            if (IsDead || _rb == null) return;
            _rb.velocity += dir * force * 1.5f;
        }

        protected override void OnDeath()
        {
            // 死亡清理：回池（未走池生成的实例由 Despawn 兜底 Destroy）
            if (PoolService.Instance != null) PoolService.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }
    }
}
