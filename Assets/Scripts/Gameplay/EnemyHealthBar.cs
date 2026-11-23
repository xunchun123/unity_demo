using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 怪物头顶血条：第一次受伤后出现，满血或死亡时隐藏。
    /// 三层：暗底 / 白色"掉血残影" / 红色当前血量。残影延迟 0.35 秒再追上当前值，
    /// 一刀砍掉多少血一眼看得出来（格斗游戏常见的伤害可读性做法）。
    /// 全部用代码生成的 1 像素白图 + 纯色材质，不依赖美术资源，也不受 2D 灯光影响。
    /// 挂在怪物根上而不是 Visual 上：Visual 会随朝向翻转、随受击抖动，血条不应该跟着动。
    /// </summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        private const float Height = 0.09f;
        private const float Border = 0.025f;
        private const float TrailDelay = 0.35f;
        private const float TrailSpeed = 1.6f; // 每秒追上的比例

        private static readonly Color BackColor = new Color(0.06f, 0.04f, 0.05f, 0.85f);
        private static readonly Color TrailColor = new Color(1f, 0.92f, 0.75f, 1f);
        private static readonly Color FillHigh = new Color(0.86f, 0.16f, 0.16f, 1f);
        private static readonly Color FillLow = new Color(1f, 0.55f, 0.1f, 1f);

        private static Sprite _centerSprite;
        private static Sprite _leftSprite;

        private Transform _root;
        private SpriteRenderer _back;
        private SpriteRenderer _trail;
        private SpriteRenderer _fill;
        private float _width;
        private float _ratio = 1f;
        private float _trailRatio = 1f;
        private float _trailWait;

        public static EnemyHealthBar Create(Transform owner, float width, float height, Material flatMaterial)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var bar = go.AddComponent<EnemyHealthBar>();
            bar.Build(width, flatMaterial);
            return bar;
        }

        private void Build(float width, Material flatMaterial)
        {
            EnsureSprites();
            _root = transform;
            _width = width;
            var material = flatMaterial != null ? flatMaterial : new Material(Shader.Find("GUI/Text Shader"));

            _back = Layer("Back", _centerSprite, BackColor, 40, material);
            _back.transform.localScale = new Vector3(width + Border * 2f, Height + Border * 2f, 1f);
            _trail = Layer("Trail", _leftSprite, TrailColor, 41, material);
            _fill = Layer("Fill", _leftSprite, FillHigh, 42, material);
            _trail.transform.localPosition = _fill.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
            Apply();
            SetVisible(false);
        }

        private SpriteRenderer Layer(string name, Sprite sprite, Color color, int order, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>满血复位（池化复用时调用）。</summary>
        public void ResetFull()
        {
            _ratio = _trailRatio = 1f;
            _trailWait = 0f;
            Apply();
            SetVisible(false);
        }

        public void SetRatio(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            if (ratio < _ratio) _trailWait = TrailDelay; // 新的一刀：残影先停住
            _ratio = ratio;
            if (_trailRatio < _ratio) _trailRatio = _ratio;
            SetVisible(_ratio > 0f && _ratio < 1f);
            Apply();
        }

        private void LateUpdate()
        {
            // 抵消父物体缩放：怪物 AI 会挤压根节点表达蓄力，血条不能跟着变形
            var parentScale = _root.parent != null ? _root.parent.lossyScale : Vector3.one;
            _root.localScale = new Vector3(SafeInverse(parentScale.x), SafeInverse(parentScale.y), 1f);

            if (_trailRatio <= _ratio) return;
            if (_trailWait > 0f) { _trailWait -= Time.deltaTime; return; }
            _trailRatio = Mathf.MoveTowards(_trailRatio, _ratio, TrailSpeed * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_fill == null) return;
            _fill.transform.localScale = new Vector3(_width * _ratio, Height, 1f);
            _trail.transform.localScale = new Vector3(_width * _trailRatio, Height, 1f);
            _fill.color = Color.Lerp(FillLow, FillHigh, Mathf.InverseLerp(0.15f, 0.6f, _ratio));
        }

        private void SetVisible(bool visible)
        {
            if (_back != null) _back.enabled = _trail.enabled = _fill.enabled = visible;
        }

        private static float SafeInverse(float v) => Mathf.Abs(v) < 0.0001f ? 1f : 1f / Mathf.Abs(v);

        private static void EnsureSprites()
        {
            if (_centerSprite != null) return;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "HealthBarWhite" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            // PPU = 4：一张图正好 1×1 世界单位，缩放值就是世界尺寸
            _centerSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _leftSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f); // 左端锚定：缩短时从右往左掉
        }
    }
}
