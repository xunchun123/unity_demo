using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay.World
{
    /// <summary>
    /// 横版跟随相机：只跟 X 轴，Y 锁定关卡地面高度，X 按关卡边界钳制。
    /// 面试点：
    /// ① 横版闯关相机的第一原则是"纵向构图稳定"——勇者上下跳台阶时画面跟着晃会晕，
    ///    所以 Y 锁死在地面高度（由 LevelDirector 注入），只有 X 平滑追踪；
    /// ② X 边界钳制（LevelDirector 注入 minX/maxX）：关卡边缘露出"世界之外"的空黑是
    ///    横版游戏最扎眼的穿帮，钳制时若关卡比视口还窄则取居中；
    /// ③ 与 CameraRig 震屏兼容：本组件挂 [DefaultExecutionOrder(-100)]，LateUpdate
    ///    先落定"基准位"，CameraRig（若存在于场景，DDOL 单例）随后在其上叠加位移抖动；
    ///    下一帧基准位被重新断言，抖动位移不会累积漂移、跟随也不会被抖动带偏。
    ///    场景里没有 CameraRig 时，内置同款 trauma 震屏兜底（监听 PlayerHurt 事件），
    ///    检测到 CameraRig.Instance 存在则自动关闭内置震屏，避免双重抖动。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Camera))]
    public class SideCamera : MonoBehaviour
    {
        [Header("跟随")]
        [Tooltip("平滑响应率（每秒）：任务书'SmoothDamp 常数 5'按响应率量纲理解，与 CameraRig.followSmooth=6 同一标度，换算 SmoothDamp smoothTime = 1/5 = 0.2s。若按字面 smoothTime=5s，稳态滞后 ≈ 移速×5 ≈ 30 世界单位，勇者必然出画")]
        [SerializeField] private float followSmooth = 5f;
        [Tooltip("相机高于锁定地面的抬升量：地面线落在画面下 1/3 的构图")]
        [SerializeField] private float groundLift = 2.8f;
        [Tooltip("纵向死区：勇者脚底高出地面这么多以内，相机不动（跳跃不晃）；再高就抬镜头，高台不出画")]
        [SerializeField] private float verticalDeadZone = 2.2f;
        [Tooltip("纵向跟随的平滑时间（秒）：比横向慢，抬镜头时更稳")]
        [SerializeField] private float verticalSmoothTime = 0.3f;

        [Header("内置震屏（无 CameraRig 时的兜底，参数对齐 CameraRig 基准）")]
        [SerializeField] private float maxShakeOffset = 0.3f;
        [SerializeField] private float maxShakeAngle = 3.5f;
        [SerializeField] private float traumaDecay = 1.6f;

        private Camera _cam;
        private Transform _target;
        private float _retargetTimer;

        private float _minX;
        private float _maxX;
        private float _groundY;
        private bool _hasBounds;

        private float _smoothVelocity;   // SmoothDamp 的速度记忆
        private float _smoothVelocityY;
        private float _trauma;
        private float _z;

        /// <summary>注入跟随目标（通常为勇者）。</summary>
        public void SetTarget(Transform target) => _target = target;

        /// <summary>注入关卡取景边界与地面高度（LevelDirector.Init 调用）。</summary>
        public void SetLevelBounds(float minX, float maxX, float groundY)
        {
            _minX = minX;
            _maxX = maxX;
            _groundY = groundY;
            _hasBounds = true;
        }

        /// <summary>立即贴合目标（关卡初始化时用，避免相机从旧位置慢慢滑过来）。</summary>
        public void SnapToTarget()
        {
            _smoothVelocity = 0f;
            _smoothVelocityY = 0f;
            float y = _hasBounds ? ComputeTargetY() : transform.position.y;
            transform.position = new Vector3(ComputeClampedTargetX(), y, _z);
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _z = transform.position.z;
        }

        private void OnEnable() => GameEvents.PlayerHurt += OnPlayerHurt;
        private void OnDisable() => GameEvents.PlayerHurt -= OnPlayerHurt;

        private void OnPlayerHurt(Vector2 from)
        {
            // HeroController 已在受击时调用 CameraRig.Instance?.AddTrauma——
            // 有 CameraRig 时它负责震屏；没有时由内置 trauma 兜底，保证打击感不因组件缺失而丢
            if (CameraRig.Instance == null) AddTrauma(0.3f);
        }

        /// <summary>内置震屏入口（与 CameraRig.AddTrauma 同签名，语义一致）。</summary>
        public void AddTrauma(float amount)
        {
            if (CameraRig.Instance != null) return; // 外部震屏在场，内置关闭
            _trauma = Mathf.Clamp01(_trauma + Mathf.Min(amount, 0.3f));
        }

        private void LateUpdate()
        {
            // 目标丢失时低频重找（勇者 prefab 缺失/延迟生成的场景也能自动接上）
            if (_target == null)
            {
                _retargetTimer -= Time.deltaTime;
                if (_retargetTimer <= 0f)
                {
                    _retargetTimer = 0.5f;
                    var hero = FindFirstObjectByType<HeroController>();
                    if (hero != null) _target = hero.transform;
                }
            }

            transform.position = ComputeBasePosition(Time.deltaTime);

            // ---------- 内置震屏（创伤模型，与 CameraRig 同款：trauma² + Perlin） ----------
            if (CameraRig.Instance != null) return; // 有 CameraRig：位置已断言完基准位，抖动交给它叠加
            float shake = _trauma * _trauma;
            if (shake > 0.001f)
            {
                float t = Time.time * 28f;
                float offsetX = (Mathf.PerlinNoise(t, 0f) * 2f - 1f) * maxShakeOffset * shake;
                float offsetY = (Mathf.PerlinNoise(0f, t) * 2f - 1f) * maxShakeOffset * shake * 0.6f;
                float angle = (Mathf.PerlinNoise(t, t) * 2f - 1f) * maxShakeAngle * shake;
                transform.position += new Vector3(offsetX, offsetY, 0f);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                _trauma = Mathf.Max(0f, _trauma - traumaDecay * Time.deltaTime);
            }
            else
            {
                transform.rotation = Quaternion.identity;
                _trauma = 0f;
            }
        }

        /// <summary>目标 X 经视口半宽钳制后的取景中心（关卡比视口窄时取居中）。</summary>
        private float ComputeClampedTargetX()
        {
            float targetX = _target != null ? _target.position.x : transform.position.x;
            // 视口半宽：正交相机 = 正交尺寸 × 宽高比（关卡构建保证 orthographic；
            // 透视模式也按正交尺寸近似，边界略保守）
            float halfViewWidth = _cam.orthographicSize * _cam.aspect;
            if (!_hasBounds) return targetX;
            float lo = _minX + halfViewWidth;
            float hi = _maxX - halfViewWidth;
            return lo > hi ? (_minX + _maxX) * 0.5f : Mathf.Clamp(targetX, lo, hi);
        }

        /// <summary>计算本帧基准位：X 平滑追踪 + 边界钳制，Y 锁定地面。</summary>
        private Vector3 ComputeBasePosition(float deltaTime)
        {
            // SmoothDamp 平滑追踪：followSmooth 是"每秒响应率"，smoothTime 取其倒数
            float smoothTime = Mathf.Max(0.01f, 1f / Mathf.Max(0.01f, followSmooth));
            float x = Mathf.SmoothDamp(transform.position.x, ComputeClampedTargetX(), ref _smoothVelocity, smoothTime, Mathf.Infinity, deltaTime);

            float y = _hasBounds
                ? Mathf.SmoothDamp(transform.position.y, ComputeTargetY(), ref _smoothVelocityY, verticalSmoothTime, Mathf.Infinity, deltaTime)
                : transform.position.y;
            return new Vector3(x, y, _z);
        }

        /// <summary>
        /// 纵向取景：平时锁在地面构图（地面线在画面下 1/3）；勇者登上高台、脚底超出死区时，
        /// 镜头只抬超出的那部分——普通跳跃不晃，高台也不会把人顶出画面。掉坑不往下追（坑底没东西可看）。
        /// </summary>
        private float ComputeTargetY()
        {
            float baseY = _groundY + groundLift;
            if (_target == null) return baseY;
            float excess = _target.position.y - (_groundY + verticalDeadZone);
            return excess > 0f ? baseY + excess : baseY;
        }
    }
}
