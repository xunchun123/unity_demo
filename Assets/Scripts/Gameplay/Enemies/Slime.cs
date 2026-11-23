using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// 史莱姆（外观：地狱犬）：地面蹦跳型小怪。
    /// 行为环：待机 → 勇者进入 6 格视野 → 蓄力 0.4s（压扁）→ 朝勇者方向跳起 → 落地缓冲 0.8s → 循环。
    /// 面试点：所有攻击都带"可读"的前摇——压扁蹲伏就是起跳预告，玩家看到蓄力就知道该走位，
    /// 怪物数值强不强不重要，"能被预判"才有对抗乐趣；接触伤害复用基类 OnTriggerStay2D。
    /// </summary>
    public class Slime : EnemyBase
    {
        [Header("蹦跳参数")]
        [SerializeField] private float detectRange = 6f;        // 视野（1 格 ≈ 1 世界单位）
        [SerializeField] private float windupTime = 0.4f;       // 蓄力时长（压扁前摇）
        [SerializeField] private float jumpForward = 5f;        // 起跳水平速度
        [SerializeField] private float jumpUp = 6f;             // 起跳垂直速度
        [SerializeField] private float landRest = 0.8f;         // 落地缓冲时长
        [SerializeField] private LayerMask groundMask = 1 << 8; // 与 HeroController 同一地面层约定（关卡引导器可注入）

        /// <summary>行为状态机：待机 → 蓄力 → 滞空 → 落地缓冲。</summary>
        private enum State { Idle, Windup, Airborne, LandRest }
        private State _state = State.Idle;
        private float _stateTimer;
        private float _airTime;         // 滞空计时：地面探测失灵时的兜底，绝不卡在空中
        private float _frozenTimer;     // 冻结契约（SetFrozen）：>0 时跳过行为、velocity 清零

        // 像素帧本身已有蹲伏/腾空姿势，程序化形变只留一点弹性（大幅缩放会让像素粗细不均）
        private static readonly Vector3 SquashScale = new Vector3(1.08f, 0.9f, 1f);   // 压扁姿态（蓄力前摇 & 落地共用）
        private static readonly Vector3 StretchScale = new Vector3(0.95f, 1.06f, 1f); // 起跳瞬间拉伸姿态
        private static readonly Vector3 BaseScale = Vector3.one;

        public override void Init(Transform hero)
        {
            base.Init(hero);
            _state = State.Idle;
            _stateTimer = 0f;
            _airTime = 0f;
            _frozenTimer = 0f;
            _contactTimer = 0f;                 // 基类不复位接触冷却，池化复用要自己清
            transform.localScale = BaseScale;   // 上次死亡时可能停在压扁姿态
            if (_sr != null) _sr.flipX = false;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>冻结（冰霜新星）：打断一切姿态回待机，解冻后从 Idle 重新起手（状态机无残留）。</summary>
        public override void SetFrozen(float seconds)
        {
            _frozenTimer = Mathf.Max(_frozenTimer, seconds);
            if (_rb != null) _rb.velocity = Vector2.zero;
            transform.localScale = BaseScale;
            _state = State.Idle;
            AnimWindup(false);
        }

        protected override void Update()
        {
            // 冻结期间 AI 完全停摆且 velocity 每帧清零（击退也会被压住，冰块就是不能动）
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
            if (_sr != null) _sr.flipX = !_facingRight; // 精灵默认朝右，勇者在左侧时翻转

            switch (_state)
            {
                case State.Idle:
                    // 待机：横向速度向 0 收敛（击退余速靠"摩擦"衰减，而不是每帧瞬清吃掉受击反馈）
                    _rb.velocity = new Vector2(
                        Mathf.MoveTowards(_rb.velocity.x, 0f, 8f * Time.deltaTime), _rb.velocity.y);
                    if (_hero != null &&
                        Vector2.Distance(transform.position, _hero.position) <= detectRange)
                    {
                        _state = State.Windup;
                        _stateTimer = windupTime;
                        AnimWindup(true); // 蹲伏蓄力
                    }
                    break;

                case State.Windup:
                {
                    // 压扁蓄力：动画即前摇，越接近出手压得越扁
                    _stateTimer -= Time.deltaTime;
                    float k = Mathf.Clamp01(1f - _stateTimer / Mathf.Max(0.01f, windupTime));
                    transform.localScale = Vector3.Lerp(BaseScale, SquashScale, k);
                    if (_stateTimer <= 0f)
                    {
                        // 跳跃方向以出手瞬间为准——蓄力期间走位可以骗它跳反方向
                        var dir = new Vector2(_facingRight ? 1f : -1f, 0f);
                        _rb.velocity = dir * jumpForward + Vector2.up * jumpUp;
                        transform.localScale = StretchScale; // 蓄力压扁→起跳拉伸：一鼓一收，弹感翻倍
                        AnimAttack(); // 扑咬腾空
                        _state = State.Airborne;
                        _airTime = 0f;
                    }
                    break;
                }

                case State.Airborne:
                {
                    // 滞空：击退叠加在抛物线上（空中不影响弹跳逻辑），落地时统一起点
                    _airTime += Time.deltaTime;
                    // 拉伸姿态缓缓回正：落点处几乎恢复，落地瞬间再压扁
                    transform.localScale = Vector3.MoveTowards(
                        transform.localScale, BaseScale, Time.deltaTime * 2.5f);
                    bool landed = _rb.velocity.y <= 0.1f && IsGrounded()
                                  || _airTime > 1.2f; // 兜底：groundMask 配错也不会永久滞空
                    if (landed)
                    {
                        _rb.velocity = Vector2.zero;    // 落地清零：弹跳余速与击退一并抹掉
                        transform.localScale = SquashScale; // 落地压扁（跟蓄力同一姿态，语言统一）
                        _state = State.LandRest;
                        _stateTimer = landRest;
                    }
                    break;
                }

                case State.LandRest:
                    _rb.velocity = new Vector2(
                        Mathf.MoveTowards(_rb.velocity.x, 0f, 8f * Time.deltaTime), _rb.velocity.y);
                    _stateTimer -= Time.deltaTime;
                    // 压扁姿态在缓冲期内缓缓弹回（落地重量感的后半拍）
                    float recover = Mathf.Clamp01(1f - _stateTimer / Mathf.Max(0.01f, landRest));
                    transform.localScale = Vector3.Lerp(SquashScale, BaseScale, recover);
                    if (_stateTimer <= 0f)
                    {
                        transform.localScale = BaseScale;
                        _state = State.Idle;
                    }
                    break;
            }
        }

        /// <summary>地面探测：脚底一小块 OverlapBox（层掩码与勇者跳跃判定同源）。</summary>
        private bool IsGrounded()
        {
            var origin = (Vector2)transform.position + Vector2.down * 0.15f;
            return Physics2D.OverlapBox(origin, new Vector2(0.4f, 0.12f), 0f, groundMask) != null;
        }

        /// <summary>击退：直接叠加速度（滞空时相当于改写抛物线，落地统一清零回收）。</summary>
        public override void ApplyKnockback(Vector2 dir, float force)
        {
            if (IsDead || _rb == null) return;
            _rb.velocity += dir * force;
        }

        protected override void OnDeath()
        {
            // 死亡清理：回池（未走池生成的实例由 Despawn 兜底 Destroy），姿态复位交给下次 Init
            if (PoolService.Instance != null) PoolService.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }
    }
}
