using System;
using System.Collections;
using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>
    /// 轻量补间工具：只覆盖 demo 用到的曲线（缩放弹出、位移、淡入淡出、数值滚动）。
    /// 面试点：为什么不用 DOTween——demo 级别不需要完整补间库，60 行手写实现
    /// "零外部依赖 + 支持 unscaled time（timeScale=0 时 UI 动画照常播放）" 更能讲清楚原理。
    /// </summary>
    public static class Tween
    {
        /// <summary>缓动函数集。</summary>
        public static float EaseOutBack(float t) { const float c = 1.70158f; t -= 1f; return 1f + (c + 1f) * t * t * t + c * t * t; }
        public static float EaseOutCubic(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float EaseInQuad(float t) => t * t;
        public static float Linear(float t) => t;

        /// <summary>从 MonoBehaviour 上启动一个协程补间（宿主销毁时自动停止）。</summary>
        public static Coroutine Start(MonoBehaviour host, float duration, Action<float> onUpdate,
            Func<float, float> ease = null, bool unscaled = true, Action onComplete = null)
        {
            return host.StartCoroutine(Run(duration, onUpdate, ease ?? EaseOutCubic, unscaled, onComplete));
        }

        private static IEnumerator Run(float duration, Action<float> onUpdate, Func<float, float> ease,
            bool unscaled, Action onComplete)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += unscaled ? Time.unscaledDeltaTime / duration : Time.deltaTime / duration;
                onUpdate?.Invoke(ease(Mathf.Clamp01(t)));
                yield return null;
            }
            onComplete?.Invoke();
        }

        /// <summary>缩放弹出：0→目标值，带回弹（用于升级卡片、结算面板入场）。</summary>
        public static Coroutine ScaleIn(MonoBehaviour host, Transform target, float to = 1f, float duration = 0.25f)
        {
            target.localScale = Vector3.zero;
            return Start(host, duration, k => target.localScale = Vector3.one * (k * to), EaseOutBack);
        }

        /// <summary>数值滚动：把整数从 from 滚到 to（用于结算界面的击杀数）。</summary>
        public static Coroutine CountTo(MonoBehaviour host, float duration, int from, int to, Action<int> onUpdate)
        {
            return Start(host, duration, k => onUpdate(Mathf.RoundToInt(Mathf.Lerp(from, to, k))), Linear);
        }
    }
}
