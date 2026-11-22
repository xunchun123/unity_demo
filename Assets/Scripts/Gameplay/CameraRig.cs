using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 相机：平滑跟随 + 创伤模型震屏（trauma → 抖动）。
    /// 面试点：震屏用"创伤值衰减 + Perlin 噪声"而非随机数抖动——
    /// Perlin 平滑连续，随机数抖动观感"尖锐毛躁"；多次受击 trauma 叠加上限钳制，
    /// 不会因连续受伤把玩家晃吐（量控制比效果本身更重要）。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        [SerializeField] private Transform _followTarget;
        [SerializeField] private float followSmooth = 6f;
        // 2026 审美基准：小视野正交相机下，大幅长震屏是"上个时代的大甩屏"——
        // 收敛到约 2.4% 画高 + 3.5°，且垂直分量再打 6 折（人对垂直晃动耐受差）
        [SerializeField] private float maxShakeAngle = 3.5f;   // 最大旋转抖动（度）
        [SerializeField] private float maxShakeOffset = 0.3f;  // 最大位移抖动（世界单位）
        [SerializeField] private float traumaDecay = 1.6f;   // 创伤衰减速度/秒

        private Camera _cam;
        private float _trauma;
        private Vector3 _basePos;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _cam = GetComponent<Camera>();
            _basePos = transform.position;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetTarget(Transform t) => _followTarget = t;

        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + Mathf.Min(amount, 0.3f));

        /// <summary>定向冲击（大事件用）：直接给一段创伤。</summary>
        public void Kick(float amount) => AddTrauma(amount);

        private void LateUpdate()
        {
            if (_followTarget != null)
            {
                Vector3 targetPos = _followTarget.position;
                targetPos.z = _basePos.z;
                transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));
            }

            // 创伤模型：shake = trauma²（衰减时前段剧烈后段柔和），Perlin 噪声驱动
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
    }
}
