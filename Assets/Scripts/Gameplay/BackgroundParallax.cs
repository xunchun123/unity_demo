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
            if (_cam == null) return;
            foreach (var layer in _layers)
            {
                var sr = layer.renderer;
                // 关键：视差层用"精灵原图 + Repeat 包裹"的独立材质，不能用图集
                //（图集 UV 会被 offset 切到邻居精灵区域）。material getter 会自动实例化副本。
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.mainTexture = sr.sprite.texture;
                sr.material = mat;
                sr.transform.localScale = new Vector3(layer.scale.x, layer.scale.y, 1f);
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            Vector2 camPos = _cam.position;
            foreach (var layer in _layers)
            {
                if (layer.renderer == null) continue;
                var tr = layer.renderer.transform;
                tr.position = new Vector3(camPos.x, camPos.y, tr.position.z);
                // UV 偏移滚动：近层跟得紧、远层跟得松 → 纵深感
                var mat = layer.renderer.material;
                mat.mainTextureOffset = camPos * layer.followFactor / layer.scale;
            }
        }
    }
}
