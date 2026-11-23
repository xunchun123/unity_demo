using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 受击剪影闪光：在精灵上叠一层同形状的纯色剪影，闪 0.08 秒。
    /// 为什么不直接改 SpriteRenderer.color：color 是乘法染色，白色 × 原图 = 原图，
    /// "闪白"写成 sr.color = white 等于什么都没发生（旧版受击无反馈的根因）。
    /// 剪影层用 GUI/Text Shader：它只取贴图的 alpha、颜色全来自顶点色，天然就是纯色剪影，
    /// 而且不受 2D 灯光影响，暗场景里也一样醒目。
    /// 剪影层在 LateUpdate 里同步 sprite 与翻转（晚于 Animator 换帧），动画播到哪一帧剪影就是哪一帧。
    /// </summary>
    public class SpriteFlash : MonoBehaviour
    {
        private SpriteRenderer _source;
        private SpriteRenderer _overlay;
        private float _timer;
        private float _duration;
        private Color _color;

        /// <summary>给某个 SpriteRenderer 挂上剪影层（material 为空时按名字找 GUI/Text Shader）。</summary>
        public static SpriteFlash Attach(SpriteRenderer source, Material flatMaterial)
        {
            if (source == null) return null;
            var flash = source.GetComponent<SpriteFlash>();
            if (flash == null) flash = source.gameObject.AddComponent<SpriteFlash>();
            flash.Init(source, flatMaterial);
            return flash;
        }

        private void Init(SpriteRenderer source, Material flatMaterial)
        {
            _source = source;
            if (_overlay != null) return;

            var go = new GameObject("HitFlash");
            go.transform.SetParent(source.transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            var shader = Shader.Find("GUI/Text Shader");
            _overlay.sharedMaterial = flatMaterial != null ? flatMaterial : (shader != null ? new Material(shader) : null);
            _overlay.sortingLayerID = source.sortingLayerID;
            _overlay.sortingOrder = source.sortingOrder + 1;
            _overlay.enabled = false;
        }

        public void Play(Color color, float duration = 0.08f)
        {
            if (_overlay == null) return;
            _color = color;
            _duration = Mathf.Max(0.01f, duration);
            _timer = _duration;
            _overlay.enabled = true;
            Sync();
        }

        private void OnDisable()
        {
            _timer = 0f;
            if (_overlay != null) _overlay.enabled = false;
        }

        private void LateUpdate()
        {
            if (_timer <= 0f) return;
            // 真实时间：命中停帧（timeScale≈0）期间闪光照样播完，停帧本身就是在"展示"这一下
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f)
            {
                _overlay.enabled = false;
                return;
            }
            Sync();
        }

        private void Sync()
        {
            _overlay.sprite = _source.sprite;
            _overlay.flipX = _source.flipX;
            var c = _color;
            c.a *= Mathf.Clamp01(_timer / _duration * 1.6f); // 前半段满亮，后半段淡出
            _overlay.color = c;
        }
    }
}
