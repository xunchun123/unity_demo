using System.Collections;
using System.Collections.Generic;
using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 打击感（Juice）总控：停帧、受击闪白、粒子迸发、光环、屏幕特效的统一入口。
    /// 面试点：Juice 是廉价成本换巨大体验提升的工程实践——
    /// 停帧 50ms + 闪白 + 粒子三件套能让同样的数值手感完全不同。
    /// </summary>
    public class Juice : MonoBehaviour
    {
        public static Juice Instance { get; private set; }

        [Header("粒子系统（编辑器期由构建脚本创建并注入）")]
        [SerializeField] private ParticleSystem _deathBurst;     // 敌人死亡迸发
        [SerializeField] private ParticleSystem _hitSpark;       // 命中小火花
        [SerializeField] private ParticleSystem _pickupSpark;    // 拾取星星
        [SerializeField] private ParticleSystem _muzzleFlash;    // 枪口闪光
        [SerializeField] private ParticleSystem _levelUpRing;    // 升级光环

        /// <summary>停帧状态（防止嵌套触发：短停帧期间再触发不延长）。</summary>
        private bool _hitStopping;
        private float _pendingHitStop;
        private float _nextHitStopAllowed; // unscaled 时间；高频命中的节流阀

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------- 停帧（Hit-Stop） ----------

        /// <summary>
        /// 全局停帧：timeScale 瞬间压到 0.05 再恢复，重击感来源。playing 状态才生效。
        /// 带 0.15s 冷却：割草每秒几十次命中，每次都停帧会变成周期性"顿挫"——
        /// 只让冷却外的最重一击生效（2026 game feel 共识：分级 + 节流，而非每次全冻结）。
        /// </summary>
        public static void HitStop(float realSeconds)
        {
            if (Instance == null || GameManager.Instance.State != GameState.Playing) return;
            if (Time.unscaledTime < Instance._nextHitStopAllowed) return;
            Instance._nextHitStopAllowed = Time.unscaledTime + 0.15f;
            Instance._pendingHitStop = Mathf.Max(Instance._pendingHitStop, realSeconds);
            if (!Instance._hitStopping) Instance.StartCoroutine(Instance.HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            _hitStopping = true;
            float scaleBackup = Time.timeScale;
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(_pendingHitStop);
            _pendingHitStop = 0f;
            // 只在仍处于 Playing 时恢复（升级/暂停弹窗可能在停帧期间打开，它们自己管 timeScale）
            if (GameManager.Instance.State == GameState.Playing) Time.timeScale = scaleBackup;
            _hitStopping = false;
        }

        // ---------- 受击闪白 ----------

        /// <summary>
        /// Sprite 闪白：暂存原色 → 置为高亮色 60ms → 还原。
        /// 注意直接改 sr.color：敌人精灵用 Sprites/Default（没有 _BaseColor 属性，
        /// MaterialPropertyBlock 方案在这里是无声无息的无效操作），且 sr.color
        /// 已被种族 tint 占用——暂存/还原原色，精英 tint 与池复用都不会串色。
        /// </summary>
        public static void Flash(SpriteRenderer sr, Color flashColor, float duration = 0.06f)
        {
            if (sr == null || Instance == null) return;
            Instance.StartCoroutine(Instance.FlashRoutine(sr, flashColor, duration));
        }

        private IEnumerator FlashRoutine(SpriteRenderer sr, Color flashColor, float duration)
        {
            Color original = sr.color;
            sr.color = flashColor;
            yield return new WaitForSecondsRealtime(duration);
            if (sr != null) sr.color = original;
        }

        // ---------- 受击挤压（Squash & Stretch） ----------

        private static readonly HashSet<Transform> _punching = new();

        /// <summary>受击挤压：横向拉伸+纵向压缩再弹回（90-120ms）。正在动画中的目标跳过，防止连续命中漂移。</summary>
        public static void PunchScale(Transform t, float amount, float duration = 0.1f)
        {
            if (t == null || Instance == null || !_punching.Add(t)) return;
            Instance.StartCoroutine(Instance.PunchRoutine(t, amount, duration));
        }

        private IEnumerator PunchRoutine(Transform t, float amount, float duration)
        {
            Vector3 baseScale = t.localScale;
            Vector3 peak = new Vector3(baseScale.x * (1f + amount), baseScale.y * (1f - amount), baseScale.z);
            float half = duration * 0.4f, e = 0f;
            while (e < half && t != null)
            {
                e += Time.unscaledDeltaTime;
                t.localScale = Vector3.Lerp(baseScale, peak, e / half);
                yield return null;
            }
            e = 0f;
            float back = Mathf.Max(0.01f, duration - half);
            while (e < back && t != null)
            {
                e += Time.unscaledDeltaTime;
                t.localScale = Vector3.Lerp(peak, baseScale, e / back);
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
            _punching.Remove(t);
        }

        // ---------- 粒子包装（带 EmitParams，避免为每次爆点实例化对象） ----------

        public static void DeathBurst(Vector3 pos, Color color, int count, float radius)
        {
            if (Instance?._deathBurst == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            var col = color; col.a = 1f;
            emit.startColor = col;
            emit.startSize = Random.Range(0.12f, 0.3f) * Mathf.Clamp(radius * 2f, 1f, 2f);
            emit.startLifetime = 0.45f;
            emit.velocity = Random.insideUnitCircle * (5f + radius * 4f);
            Instance._deathBurst.Emit(emit, count);
        }

        public static void HitSpark(Vector3 pos, Color color)
        {
            if (Instance?._hitSpark == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, startColor = color, startSize = 0.14f, startLifetime = 0.2f };
            emit.velocity = Random.insideUnitCircle * 3.5f;
            Instance._hitSpark.Emit(emit, 5);
        }

        public static void PickupSpark(Vector3 pos, Color color)
        {
            if (Instance?._pickupSpark == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, startColor = color, startSize = 0.16f, startLifetime = 0.35f };
            emit.velocity = new Vector2(Random.Range(-1f, 1f), Random.Range(2f, 3.5f));
            Instance._pickupSpark.Emit(emit, 8);
        }

        public static void MuzzleFlash(Vector3 pos, Vector2 dir)
        {
            if (Instance?._muzzleFlash == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = pos + (Vector3)(dir * 0.25f),
                startColor = new Color(1.6f, 1.5f, 0.9f, 1f), // HDR 超 1，喂给 Bloom 发光
                startSize = 0.22f,
                startLifetime = 0.1f,
                velocity = dir * 2.5f,
            };
            Instance._muzzleFlash.Emit(emit, 3);
        }

        public static void LevelUpRing(Vector3 pos)
        {
            if (Instance?._levelUpRing == null) return;
            Instance._levelUpRing.transform.position = pos;
            Instance._levelUpRing.Emit(24);
        }

        /// <summary>挥剑弧光：三段连击各一张程序化剑气贴图，按朝向翻转，淡出自毁。
        /// 等级越高弧光越大——"每升一级攻击更华丽"的视觉锚点。</summary>
        public static void SlashArc(Vector3 pos, bool facingRight, int stage, float levelScale = 1f)
        {
            var sprites = SpritePool.Slashes;
            if (sprites == null || sprites.Length == 0) return;
            var sprite = sprites[Mathf.Clamp(stage - 1, 0, sprites.Length - 1)];
            var go = new GameObject("SlashArc");
            go.transform.position = pos + Vector3.right * (facingRight ? 0.7f : -0.7f);
            go.transform.localScale = Vector3.one * levelScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = !facingRight;
            sr.sortingOrder = 10;
            go.AddComponent<FadeOutAndDie>().life = 0.18f;
        }

        // ---------- 供场景构建脚本注入粒子引用 ----------

        public void BindParticles(ParticleSystem death, ParticleSystem hit, ParticleSystem pickup,
            ParticleSystem muzzle, ParticleSystem ring)
        {
            _deathBurst = death; _hitSpark = hit; _pickupSpark = pickup; _muzzleFlash = muzzle; _levelUpRing = ring;
        }
    }
}
