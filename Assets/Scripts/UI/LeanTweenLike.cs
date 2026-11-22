using StarVortex.Core;
using UnityEngine;

namespace StarVortex.UI
{
    /// <summary>常驻循环补间（菜单标题浮动等），独立于一次性 Tween。</summary>
    public static class LeanTweenLike
    {
        /// <summary>正弦浮动循环（unscaled）。</summary>
        public static void FloatingLoop(MonoBehaviour host, RectTransform target, float amplitude, float period)
        {
            Vector2 basePos = target.anchoredPosition;
            Tween.Start(host, float.MaxValue, k =>
            {
                float t = (Time.unscaledTime % period) / period * Mathf.PI * 2f;
                var p = basePos;
                p.y += Mathf.Sin(t) * amplitude;
                target.anchoredPosition = p;
            }, Tween.Linear);
        }
    }
}
