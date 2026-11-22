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

        // ---------- 程序化发光材质 / 三段连击弧光配色 ----------

        private static Material _additiveSpriteMat;
        private static readonly Color SlashWhite = new Color(1.25f, 1.25f, 1.25f, 1f); // 一段：白
        private static readonly Color SlashGold = new Color(1.55f, 1.25f, 0.45f, 1f);  // 二段：金
        private static readonly Color SlashEmber = new Color(1.7f, 0.5f, 0.22f, 1f);   // 三段：橙红
        private static readonly Color ImpactGold = new Color(1.55f, 1.2f, 0.4f, 1f);   // 命中金晕
        private static readonly Color ImpactCore = new Color(1.8f, 1.8f, 1.8f, 1f);    // 命中白核

        /// <summary>发光精灵材质：项目自写加色 Shader（Assets/Settings/UnlitAdditive），
        /// 找不到时退回 Sprites/Default（只淡不亮，好过没有）。runtime 缓存，不进 AssetDatabase。</summary>
        private static Material AdditiveSpriteMaterial
        {
            get
            {
                if (_additiveSpriteMat != null) return _additiveSpriteMat;
                var shader = Shader.Find("StarVortex/UnlitAdditive") ?? Shader.Find("Sprites/Default");
                if (shader == null) return null;
                _additiveSpriteMat = new Material(shader);
                return _additiveSpriteMat;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureParticleOrders(); // Inspector 里绑过粒子的场景，也要把层级拉到实体之上
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

        // 粒子引用一律走惰性兜底：实测 Level1-3 场景里 Juice 组件的五个粒子引用全是
        // fileID:0（构建期没绑上），不兜底的话死亡迸发/命中火花全部静默失效——
        // 这正是"攻击特效什么都没有"的根因之一，不能指望场景重建。

        public static void DeathBurst(Vector3 pos, Color color, int count, float radius)
        {
            if (Instance == null) return;
            var ps = Instance.EnsureParticle(ref Instance._deathBurst, "DeathBurst");
            if (ps == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            var col = color; col.a = 1f;
            emit.startColor = col;
            emit.startSize = Random.Range(0.12f, 0.3f) * Mathf.Clamp(radius * 2f, 1f, 2f);
            emit.startLifetime = 0.45f;
            emit.velocity = Random.insideUnitCircle * (5f + radius * 4f);
            ps.Emit(emit, count);
        }

        public static void HitSpark(Vector3 pos, Color color)
        {
            if (Instance == null) return;
            var ps = Instance.EnsureParticle(ref Instance._hitSpark, "HitSpark");
            if (ps == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, startColor = color, startSize = 0.14f, startLifetime = 0.2f };
            emit.velocity = Random.insideUnitCircle * 3.5f;
            ps.Emit(emit, 5);
        }

        public static void PickupSpark(Vector3 pos, Color color)
        {
            if (Instance == null) return;
            var ps = Instance.EnsureParticle(ref Instance._pickupSpark, "PickupSpark");
            if (ps == null) return;
            var emit = new ParticleSystem.EmitParams { position = pos, startColor = color, startSize = 0.16f, startLifetime = 0.35f };
            emit.velocity = new Vector2(Random.Range(-1f, 1f), Random.Range(2f, 3.5f));
            ps.Emit(emit, 8);
        }

        public static void MuzzleFlash(Vector3 pos, Vector2 dir)
        {
            if (Instance == null) return;
            var ps = Instance.EnsureParticle(ref Instance._muzzleFlash, "MuzzleFlash");
            if (ps == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = pos + (Vector3)(dir * 0.25f),
                startColor = new Color(1.6f, 1.5f, 0.9f, 1f), // HDR 超 1，喂给 Bloom 发光
                startSize = 0.22f,
                startLifetime = 0.1f,
                velocity = dir * 2.5f,
            };
            ps.Emit(emit, 3);
        }

        public static void LevelUpRing(Vector3 pos)
        {
            if (Instance == null) return;
            var ps = Instance.EnsureParticle(ref Instance._levelUpRing, "LevelUpRing");
            if (ps == null) return;
            ps.transform.position = pos;
            ps.Emit(24);
        }

        /// <summary>惰性粒子兜底：引用为空就现场自建一套参数一致的粒子系统。</summary>
        private ParticleSystem EnsureParticle(ref ParticleSystem slot, string name)
        {
            if (slot != null) return slot;
            slot = BuildRuntimeParticle(name);
            return slot;
        }

        /// <summary>运行时自建粒子（参数对齐 DemoSceneBuilder.FX.BuildParticle 模板）：
        /// 加色材质 + 世界空间模拟 + 收缩淡出曲线 + 渲染层级压在实体之上。</summary>
        private ParticleSystem BuildRuntimeParticle(string name)
        {
            var go = new GameObject("JuiceFX_" + name);
            go.transform.SetParent(transform, false); // 挂 Juice 自身（DDOL，跨场景存活）
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;               // 初速由 EmitParams.velocity 注入
            main.startSize = 0.2f;
            main.startColor = Color.white;
            main.maxParticles = 512;
            main.gravityModifier = 0f;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;         // 只用 Emit() 爆发，不做持续发射

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = AdditiveSpriteMaterial;
            renderer.sortingOrder = 24;         // 勇者 20 / 怪 15：粒子必须在实体之上
            return ps;
        }

        /// <summary>把五个粒子的渲染层级拉到实体（勇者 20/怪 15）之上——构建期粒子 order=5，
        /// 死亡迸发/命中火花全被角色挡住，看起来就像"什么都没有"。</summary>
        private void EnsureParticleOrders()
        {
            foreach (var ps in new[] { _deathBurst, _hitSpark, _pickupSpark, _muzzleFlash, _levelUpRing })
            {
                if (ps == null) continue;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null && r.sortingOrder < 22) r.sortingOrder = 24;
            }
        }

        /// <summary>
        /// 挥剑弧光：三段连击各一张程序化剑气贴图，按朝向翻转，淡出自毁。
        /// 打击感规格：加色（additive）材质发光、渲染层级 30 压在勇者本体（20）之上、
        /// 三段颜色白→金→橙红递进、每段轨迹角不同（一段平挥 / 二段斜上撩 18° / 三段过顶劈 38°）、
        /// 出现瞬间 0.6→1 快速放大一拍。等级越高弧光越大（SwordLong 强化由调用方乘进 levelScale）。
        /// </summary>
        public static void SlashArc(Vector3 pos, bool facingRight, int stage, float levelScale = 1f)
        {
            var sprites = SpritePool.Slashes;
            if (sprites == null || sprites.Length == 0) return;
            var sprite = sprites[Mathf.Clamp(stage - 1, 0, sprites.Length - 1)];
            var go = new GameObject("SlashArc");
            go.transform.position = pos + Vector3.right * (facingRight ? 0.7f : -0.7f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = AdditiveSpriteMaterial; // 加色发光，喂 Bloom
            sr.flipX = !facingRight;
            sr.sortingOrder = 30;                        // 必须在角色之上（本体 20 / 怪 15）
            sr.color = stage >= 3 ? SlashEmber : stage == 2 ? SlashGold : SlashWhite;
            // 每段挥砍轨迹角不同；朝左时角度镜像（与 flipX 配套）
            float tilt = stage >= 3 ? 38f : stage == 2 ? 18f : 0f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, tilt * (facingRight ? 1f : -1f));
            go.transform.localScale = Vector3.one * levelScale;
            go.AddComponent<FadeOutAndDie>().life = 0.18f;
            // 甩出去的力道：0.1s 内从 0.6 倍弹到目标尺寸（unscaled，停帧期间也照播）
            if (Instance != null)
                Tween.Start(Instance, 0.1f, k =>
                {
                    if (go != null) go.transform.localScale = Vector3.one * (levelScale * (0.6f + 0.4f * k));
                }, Tween.EaseOutCubic, true);
        }

        // ---------- 命中爆点 / 经验珠（程序化 glow 快件，不依赖粒子引用） ----------

        /// <summary>
        /// 命中爆点：白色核心 + 金色 glow_disc 弹出扩散，0.15s 消失。
        /// 走 unscaled time——HitStop 压低 timeScale 时爆点照样播完，停帧的"定格瞬间"恰恰要靠它撑住。
        /// </summary>
        public static void ImpactSpark(Vector3 pos)
        {
            if (Instance == null) return;
            var glow = NewGlowSprite(pos, ImpactGold, 0.85f, 26);
            var core = NewGlowSprite(pos, ImpactCore, 0.42f, 27);
            Instance.StartCoroutine(Instance.ImpactSparkRoutine(glow, core));
        }

        private IEnumerator ImpactSparkRoutine(SpriteRenderer glow, SpriteRenderer core)
        {
            const float life = 0.2f;
            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / life);
                if (glow != null)
                {
                    // 金晕弹出扩散（初始 0.85u → 2.2u）同时淡出（尺寸须与 ImpactSpark 初始值一致）
                    float s = Mathf.Lerp(0.6f, 2.6f, Tween.EaseOutCubic(k));
                    glow.transform.localScale = Vector3.one * (0.85f * s);
                    SetSpriteAlpha(glow, 1f - k);
                }
                if (core != null)
                {
                    // 白核向内收爆 + 快速淡出（先亮后灭的一闪）
                    core.transform.localScale = Vector3.one * (0.42f * Mathf.Lerp(1.2f, 0.5f, k));
                    SetSpriteAlpha(core, 1f - k);
                }
                yield return null;
            }
            if (glow != null) Destroy(glow.gameObject);
            if (core != null) Destroy(core.gameObject);
        }

        /// <summary>经验珠视觉：金光珠缩放弹入（EaseOutBack 过冲）+ 上飘淡出（0.45s，unscaled）。</summary>
        public static void XpOrbPop(Vector3 pos, Color color)
        {
            if (Instance == null) return;
            var orb = NewGlowSprite(pos, color, 0.3f, 26);
            if (orb != null) Instance.StartCoroutine(Instance.XpOrbRoutine(orb));
        }

        private IEnumerator XpOrbRoutine(SpriteRenderer orb)
        {
            const float life = 0.45f;
            const float popIn = 0.4f;      // 前 40% 时长完成缩放弹入
            float t = 0f;
            Vector3 origin = orb != null ? orb.transform.position : Vector3.zero;
            while (t < life && orb != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / life);
                float popK = Mathf.Clamp01(k / popIn);
                orb.transform.localScale = Vector3.one * (0.3f * Tween.EaseOutBack(popK)); // 弹入过冲
                orb.transform.position = origin + Vector3.up * (k * 0.5f);                 // 缓缓上飘
                SetSpriteAlpha(orb, k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f);
                yield return null;
            }
            if (orb != null) Destroy(orb.gameObject);
        }

        /// <summary>发光精灵快件：glow_disc + 加色材质（命中爆点/经验珠共用），返回 null 表示缺图。</summary>
        private static SpriteRenderer NewGlowSprite(Vector3 pos, Color color, float size, int order)
        {
            var disc = SpritePool.GlowDisc;
            if (disc == null) return null;
            var go = new GameObject("JuiceGlow");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = disc;
            sr.sharedMaterial = AdditiveSpriteMaterial;
            sr.color = color;
            sr.sortingOrder = order;
            float world = Mathf.Max(0.01f, disc.bounds.size.x);
            sr.transform.localScale = Vector3.one * (size / world);
            return sr;
        }

        private static void SetSpriteAlpha(SpriteRenderer sr, float a)
        {
            var c = sr.color;
            c.a = Mathf.Clamp01(a);
            sr.color = c;
        }

        // ---------- 供场景构建脚本注入粒子引用 ----------

        public void BindParticles(ParticleSystem death, ParticleSystem hit, ParticleSystem pickup,
            ParticleSystem muzzle, ParticleSystem ring)
        {
            _deathBurst = death; _hitSpark = hit; _pickupSpark = pickup; _muzzleFlash = muzzle; _levelUpRing = ring;
            EnsureParticleOrders(); // 注入后统一把层级拉到实体之上
        }
    }
}
