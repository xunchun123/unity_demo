using UnityEngine;

namespace StarVortex.Gameplay.World
{
    /// <summary>
    /// 横版远景视差层（简单跟随版）：整组子物体按 (1 − followFactor) × 相机 X 横移做慢速视差，
    /// 纵向钉在摆放时的世界高度（地平线）不随相机上下漂。
    /// 为什么不用 BackgroundParallax：那是"渲染器贴相机 XY + 材质 UV 偏移"的星空平铺件——
    /// 横版关卡的剪影/雾带必须钉在世界高度上（SideCamera 的 Y 锁定不代表背景内容也要贴相机走），
    /// 且逐株摆放的剪影比 UV 平铺更有机、无平铺周期感；故按需写这个几十行的跟随器。
    /// 覆盖域推导：子物体世界 x = 设计 x0 + camX·(1−f)，可见 ⇔ x0 − camX·f ∈ [−半视宽, +半视宽]
    /// → 布局侧按 x0 ∈ [−余量, 关卡宽·f + 余量] 撒点即可覆盖相机全程（推导见 LevelBuilder.BuildTreeBand）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ParallaxScroller : MonoBehaviour
    {
        [Tooltip("跟随系数：0 = 钉死世界坐标不随相机动，1 = 完全粘在相机上")]
        [SerializeField] private float _followFactor = 0.3f;

        private Transform _cam;
        private float _baseX;

        /// <summary>注入视差系数（LevelBuilder 建场景时调用；不设则用默认 0.3）。</summary>
        public void Configure(float followFactor) => _followFactor = followFactor;

        private void Start()
        {
            var cam = Camera.main;
            _cam = cam != null ? cam.transform : null;
            _baseX = transform.position.x; // 布局基准：横移全部叠加在其上
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            // 默认执行顺序晚于 SideCamera(-100)：相机基准位落定后再挂视差位；
            // 不叠加震屏位移——远景剪影不该跟着抖（震屏只作用于近景世界与相机本身）
            float x = _baseX + _cam.position.x * (1f - _followFactor);
            transform.position = new Vector3(x, transform.position.y, transform.position.z);
        }
    }
}
