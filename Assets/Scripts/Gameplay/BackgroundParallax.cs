using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 星空视差背景：三层星图以不同系数跟随相机滚动（材质 UV 偏移实现无限平铺）。
    /// 面试点：视差用材质偏移而非移动物体——一张贴图平铺整个宇宙，零额外对象；
    /// 系数递减（近层 0.5 → 远层 0.1）制造纵深，是"画面高级感"性价比最高的一个组件。
    /// </summary>
    public class BackgroundParallax : MonoBehaviour
    {
        [System.Serializable]
        public struct Layer
        {
            public SpriteRenderer renderer;
            [Range(0f, 1f)] public float followFactor;
            public Vector2 scale; // 缩放倍数：贴图要大于视野若干倍避免露边
        }

        [SerializeField] private Layer[] _layers;
        private Transform _cam;

        private void Start()
        {
            _cam = Camera.main.transform;
            foreach (var layer in _layers)
            {
                // 贴图 Repeat 包裹 + 缩放放大：让 UV 偏移在视野内平铺
                layer.renderer.transform.localScale = new Vector3(layer.scale.x, layer.scale.y, 1f);
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            Vector2 camPos = _cam.position;
            foreach (var layer in _layers)
            {
                if (layer.renderer == null) continue;
                // 位置跟住相机（保证始终覆盖视野），UV 偏移承担视差滚动
                var tr = layer.renderer.transform;
                tr.position = new Vector3(camPos.x, camPos.y, tr.position.z);
                var mat = layer.renderer.material;
                mat.mainTextureOffset = camPos * layer.followFactor / layer.scale;
            }
        }
    }
}
