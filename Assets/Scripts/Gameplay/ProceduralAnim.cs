using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 程序化动画（单帧贴图的"起死回生"件）：只作用于 SpriteRenderer 所在 transform 的
    /// scale / rotation / 局部位置，不改 sprite、不碰物理体。
    /// 面试点：占位美术是单帧图，Animator 剪辑肉眼无变化——跑动弹跳 + 待机呼吸 +
    /// 跳跃拉伸/落地压扁（squash & stretch）+ 攻击 punch，让同一张单帧图"活"起来
    /// （Dead Cells / Celeste 级 juice 手法，多帧美术到位后本组件照常叠加）。
    /// 注意：必须配合"视觉子物体与物理体分离"的预制体结构（构建器已做）——根缩放会连
    /// Rigidbody2D 碰撞体一起捏变形，本组件只动 SpriteRenderer 的 transform。
    /// </summary>
    public class ProceduralAnim : MonoBehaviour
    {
        [Header("模式")]
        [Tooltip("悬浮模式（飞行怪）：只做悬浮 bob + 呼吸，跳过跑动弹跳/空中压缩/接地探测")]
        public bool hoverMode = false;

        [Header("跑动")]
        [Tooltip("跑动弹跳幅度（世界单位）")]
        public float runBounceAmplitude = 0.06f;
        [Tooltip("跑动前倾角（度，方向随移动方向）")]
        public float leanAngle = 4f;
        [Tooltip("地面层掩码（接地探测，与 HeroController 同一约定）")]
        public LayerMask groundMask = 1 << 8;

        [Header("待机")]
        [Tooltip("呼吸缩放幅度（比例）")]
        public float breathAmplitude = 0.02f;
        [Tooltip("呼吸频率")]
        public float breathFrequency = 1.8f;
        [Tooltip("悬浮 bob 幅度（世界单位，飞行怪用）")]
        public float bobAmplitude = 0.045f;
        [Tooltip("悬浮 bob 频率")]
        public float bobFrequency = 2.2f;

        [Header("跳跃/落地/攻击")]
        [Tooltip("起跳瞬间姿态（拉伸，x 横向 y 纵向缩放）")]
        public Vector2 jumpStretch = new Vector2(0.85f, 1.2f);
        [Tooltip("落地瞬间姿态（压扁）")]
        public Vector2 landSquash = new Vector2(1.25f, 0.78f);
        [Tooltip("攻击 punch 力度倍率")]
        public float attackPunch = 1f;

        [Header("空中姿态")]
        [Tooltip("每单位垂直速度对应的缩放变化（上升拉伸/下落渐压）")]
        public float airStretchFactor = 0.02f;
        [Tooltip("空中缩放变化上限")]
        public float airStretchMax = 0.16f;

        [Header("弹簧恢复")]
        [Tooltip("缩放弹簧刚度（越大回弹越快）")]
        public float springStiffness = 170f;
        [Tooltip("缩放弹簧阻尼（越小回弹越'弹'）")]
        public float springDamping = 14f;

        private Transform _vis;      // 实际被驱动的 transform（SpriteRenderer 所在）
        private Rigidbody2D _rb;
        private Vector3 _basePos;
        private Vector3 _baseScale;
        private Quaternion _baseRot;
        private Vector2 _scaleOff;   // 缩放弹簧位移（相对基准 scale 的偏移量）
        private Vector2 _scaleVel;   // 缩放弹簧速度
        private float _bouncePhase;  // 跑动弹跳相位
        private float _bounceAmp;    // 平滑后的弹跳幅度（接地状态切换时淡入淡出）
        private float _lean;         // 平滑后的前倾角
        private bool _grounded;

        private void Awake()
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            _vis = sr != null ? sr.transform : transform;
            _rb = GetComponentInParent<Rigidbody2D>();
        }

        private void OnEnable()
        {
            // 捕获基准姿态（池化复用/重生成时可能残留上一次的变形）
            _basePos = _vis.localPosition;
            _baseScale = _vis.localScale;
            _baseRot = _vis.localRotation;
            _scaleOff = Vector2.zero;
            _scaleVel = Vector2.zero;
            _bouncePhase = 0f;
            _bounceAmp = 0f;
            _lean = 0f;
        }

        private void OnDisable()
        {
            // 还原基准姿态：绝不让压扁/拉伸姿态漏进下一次出场
            _vis.localPosition = _basePos;
            _vis.localScale = _baseScale;
            _vis.localRotation = _baseRot;
        }

        // ---------- 事件通知（HeroController / 怪物 AI 调用） ----------

        /// <summary>起跳瞬间：纵向拉伸（先蓄后弹的反差让起跳更"弹"，二段跳同样生效）。</summary>
        public void NotifyJump()
        {
            _scaleOff = new Vector2(jumpStretch.x - 1f, jumpStretch.y - 1f);
            _scaleVel = Vector2.zero;
        }

        /// <summary>落地瞬间：横向压扁（含二段跳与空中落地）。</summary>
        public void NotifyLand()
        {
            _scaleOff = new Vector2(landSquash.x - 1f, landSquash.y - 1f);
            _scaleVel = Vector2.zero;
        }

        /// <summary>攻击瞬间：轻 punch（横向扩 + 纵向收，弹簧带回弹）。</summary>
        public void NotifyAttack()
        {
            _scaleVel += new Vector2(7f, -9f) * attackPunch;
        }

        // ---------- 主驱动 ----------

        /// <summary>LateUpdate 驱动：晚于 Animator 求值，同一 transform 上本组件说了算。</summary>
        private void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            if (dt <= 0f) return; // 暂停/停帧期间不推进（世界冻结时动画也冻结才对）

            Vector2 vel = _rb != null ? _rb.velocity : Vector2.zero;
            _grounded = !hoverMode && CheckGrounded();
            bool running = _grounded && Mathf.Abs(vel.x) > 0.5f;

            // 1) 缩放弹簧：目标 = 空中按垂直速度拉伸/压缩，接地/悬浮回正
            Vector2 target = Vector2.zero;
            if (!hoverMode && _rb != null && !_grounded)
            {
                // 上升拉伸、下落随速度渐压；横向反向补偿近似体积守恒
                float sy = Mathf.Clamp(vel.y * airStretchFactor, -airStretchMax, airStretchMax);
                target = new Vector2(-sy * 0.6f, sy);
            }
            Vector2 accel = (target - _scaleOff) * springStiffness - _scaleVel * springDamping;
            _scaleVel += accel * dt;
            _scaleOff += _scaleVel * dt;

            // 2) 跑动弹跳：|sin| 波形（每一步一个小跳），幅度随接地状态平滑淡入淡出
            float targetAmp = running ? runBounceAmplitude : 0f;
            _bounceAmp = Mathf.MoveTowards(_bounceAmp, targetAmp, runBounceAmplitude * dt * 8f);
            if (running) _bouncePhase += dt * Mathf.Max(7f, Mathf.Abs(vel.x) * 3.4f);
            float bounceY = Mathf.Abs(Mathf.Sin(_bouncePhase)) * _bounceAmp;

            // 3) 悬浮 bob（飞行怪）：连续正弦漂浮
            float bobY = hoverMode ? Mathf.Sin(Time.time * bobFrequency) * bobAmplitude : 0f;

            // 4) 前倾：接地移动时朝移动方向压 leanAngle 度，指数平滑不抖
            float targetLean = running ? -leanAngle * Mathf.Sign(vel.x) : 0f;
            _lean += (targetLean - _lean) * (1f - Mathf.Exp(-12f * dt));

            // 5) 呼吸：全局正弦微缩放
            float breath = 1f + Mathf.Sin(Time.time * breathFrequency) * breathAmplitude;

            // 6) 写回视觉 transform（显式构造 Vector3，规避 Vector2/3 混算二义）
            _vis.localPosition = _basePos + new Vector3(0f, bounceY + bobY, 0f);
            float sx = (1f + _scaleOff.x) * breath;
            float syFinal = (1f + _scaleOff.y) * breath;
            _vis.localScale = new Vector3(_baseScale.x * sx, _baseScale.y * syFinal, _baseScale.z);
            _vis.localRotation = _baseRot * Quaternion.Euler(0f, 0f, _lean);
        }

        /// <summary>
        /// 接地探测：锚在物理根（Rigidbody）而非视觉 transform——视觉子物体带脚底偏移，
        /// 且压扁/拉伸随时在改它的 scale，拿它当基准会把探测点甩到半空中导致跑动弹跳永不触发。
        /// 探测盒下沿伸到脚底之下、上沿略过脚踝：站立时必与地面相交，离地 0.3u 以上即落空。
        /// </summary>
        private bool CheckGrounded()
        {
            if (_rb == null) return false;
            var origin = _rb.position + Vector2.down * 0.12f;
            return Physics2D.OverlapBox(origin, new Vector2(0.4f, 0.3f), 0f, groundMask) != null;
        }
    }
}
