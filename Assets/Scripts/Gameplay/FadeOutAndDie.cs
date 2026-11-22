using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>淡出后自毁：残影/剑气/闪电等一次性视觉的通用件。</summary>
    public class FadeOutAndDie : MonoBehaviour
    {
        public float life = 0.25f;
        private float _t;
        private SpriteRenderer _sr;
        private Color _start;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) _start = _sr.color;
        }

        private void Update()
        {
            // unscaled：停帧（HitStop）与升级暂停（timeScale=0）期间剑气也要淡完——
            // 否则击杀升级瞬间生成的弧光会定格在暂停画面上，用户截图里的"永久半圆"就是这么来的
            _t += Time.unscaledDeltaTime;
            float k = 1f - _t / life;
            if (_sr != null) _sr.color = new Color(_start.r, _start.g, _start.b, _start.a * k);
            if (_t >= life) Destroy(gameObject);
        }
    }
}
