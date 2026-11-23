using System;
using System.Collections;
using System.Collections.Generic;
using StarVortex.Core;
using StarVortex.Enemies;
using StarVortex.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarVortex.Gameplay.World
{
    // ============================================================ 关卡数据 ============================================================

    /// <summary>关卡目标类型：击杀数达标 / 击败精英（开传送门）/ 击败 BOSS（直接结算）。</summary>
    public enum LevelGoalKind { KillCount, Elite, Boss }

    /// <summary>
    /// 地面高度曲线段：[startX, endX) 区间内地表高度为 groundY。
    /// 区间之间的缺口就是坑/断桥（无地面，勇者跌落由 HeroRespawn 回收）。
    /// 这份数据既是 LevelBuilder 编辑器期铺地形的蓝图，也是运行时布置玩法点的依据——
    /// 单一事实来源保证地形与怪物/陷阱坐标永远对齐。
    /// </summary>
    [Serializable]
    public class GroundSegment
    {
        public float startX;
        public float endX;
        public float groundY;
    }

    /// <summary>悬浮平台：左端 x、平台面 y、宽 width（跳跃路线/BOSS 房走位用）。</summary>
    [Serializable]
    public class PlatformSpec
    {
        public float x;
        public float y;
        public float width;
    }

    /// <summary>怪物配置点：位置 + 类型字符串（映射 LevelDirector 的 prefab 绑定表）+ 可选 tint（精英换色）。</summary>
    [Serializable]
    public class MonsterSpawn
    {
        public Vector2 position;
        public string type;
        public Color tint = Color.white;
    }

    /// <summary>陷阱配置点：Spike / FallingRock / FlameVent（字符串类型保持可序列化，构建期免反射）。</summary>
    [Serializable]
    public class TrapSpawn
    {
        public Vector2 position;
        public string type;
    }

    /// <summary>
    /// 关卡静态规格（纯 C# 可序列化类，嵌在 LevelDirector 内随场景存取）。
    /// 由 LevelBuilder 在编辑器期构造注入；运行期只读。
    /// </summary>
    [Serializable]
    public class LevelSpec
    {
        public int levelIndex;                  // 关卡号（1 起）
        public string displayName;              // 关卡名（灰烬森林…）
        public float widthUnits;                // 关卡宽度（1 格 = 1 世界单位）
        public float baseGroundY;               // 主地面高度（相机 Y 锁定基准）

        [Header("主题")]
        public Color backdropColor = new Color(0.05f, 0.06f, 0.1f); // 相机背景色
        public Color lightColor = new Color(0.95f, 0.93f, 0.88f);   // 全局光颜色
        public Color groundTint = Color.white;                      // 地形 tile 染色

        [Header("内容")]
        public List<GroundSegment> groundSegments = new();  // 地面高度曲线段（缺口=断桥/深渊）
        public List<PlatformSpec> platforms = new();
        public List<MonsterSpawn> monsters = new();
        public List<TrapSpawn> traps = new();

        [Header("目标与流转")]
        public LevelGoalKind goalKind;      // 目标：击杀数 / 精英 / BOSS
        public int killTarget;              // KillCount 目标值（其余目标忽略）
        public float portalX;               // 传送门生成位置
        public string nextSceneName;        // 下一关场景名（空 = 最终关，通关直接结算）
        public float bossMaxHp = 600f;      // BOSS 关血条初值（编辑器期从 BossKing prefab 读出）
    }

    // ============================================================ 关卡状态机 ============================================================

    /// <summary>
    /// 关卡状态机（单例；场景常驻、不 DDOL——每关随场景加载重建，旧关随场景卸载销毁）。
    /// 流程：Init(spec) → 布置关卡（注入的构建委托优先，否则内部按 spec 摆放怪物/陷阱）
    ///   → 广播"战斗中" → 监听目标达成 → "传送门开启"（勇者触碰 → 下一关）
    ///   → BOSS 关直接 BossHpChanged(0,0) 结束 → 广播"通关"。
    /// 面试点：目标判定走"事件 + 轮询兜底"双通道——击杀数用 EnemyDied 事件累加；
    /// 精英/BOSS 用 IsDead 轮询兜底（死亡结算到对象销毁/回池之间可能有时序缝隙，
    /// 轮询 Unity 假 null 兼容 Destroy 路径，IsDead 兼容池化不销毁路径）。
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public static LevelDirector Instance { get; private set; }

        public enum Phase { Idle, Fighting, PortalOpen, Clear }
        public Phase CurrentPhase { get; private set; } = Phase.Idle;

        private LevelSpec _spec;
        private HeroController _hero;
        private SideCamera _sideCamera;

        [SerializeField] private LevelSpec _specSerialized;      // 场景序列化槽（builder 注入，Start 时转正）
        [SerializeField] private List<PrefabBinding> _monsterPrefabs = new();
        [SerializeField] private List<PrefabBinding> _projectilePrefabs = new();

        [Header("陷阱/传送门视觉精灵（LevelBuilder 注入；空则运行时纯色兜底）")]
        [SerializeField] private Sprite _spikeSprite;
        [SerializeField] private Sprite _rockSprite;
        [SerializeField] private Sprite _flameSprite;
        [SerializeField] private Sprite _portalSprite;
        [SerializeField] private Sprite _tileSprite;

        [Header("像素机关表现（LevelBuilder 注入 Assets/Art/Pixel/Fx 的序列帧；空则走上面的旧精灵）")]
        [SerializeField] private Sprite _ventSprite;      // 喷火口铁栅
        [SerializeField] private Sprite _ventHotSprite;   // 预热：栅缝透出火光
        [SerializeField] private Sprite[] _flameFrames;   // 火柱循环帧
        [SerializeField] private Sprite[] _portalFrames;  // 传送门电光循环帧
        [SerializeField] private Sprite[] _sparkFrames;   // 传送门火花

        /// <summary>key → prefab 的通用绑定（怪物与弹丸共用一种序列化结构）。</summary>
        [Serializable]
        public class PrefabBinding
        {
            public string key;
            public GameObject prefab;
        }

        // 运行期布置覆盖委托：默认 null → 内部按 spec 摆放；测试/变体关卡可注入自定义布置
        private Action<LevelSpec> _layoutOverride;

        // 目标引用与计数
        private int _kills;                 // 本关击杀数（EnemyDied 事件累加）
        private EnemyBase _eliteRef;        // 精英目标
        private EnemyBase _bossRef;         // BOSS
        private GameObject _portal;
        private Color _portalBaseColor;

        private static Sprite _whiteFallback; // 运行期纯色方块兜底（内存对象，不落盘，无序列化问题）

        // 层数字契约（与 DemoSceneBuilder.Settings / HeroController.groundMask 对齐；
        // 用数字不用 LayerMask.NameToLayer——层名可能被旧项目设置占用，数字才是唯一事实）
        private const int GroundLayer = 8;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            // 勇者死亡 / 通关 / 场景卸载最终都会走到这里：清订阅 + 清弹丸注册表，
            // 防止跨场景残留引用（GameEvents 是静态事件，忘退订就是经典的跨场景内存泄漏）
            if (Instance == this) Instance = null;
            GameEvents.EnemyDied -= OnEnemyDied;
            ProjectilePool.Clear();
        }

        private void Start()
        {
            // 场景内注入的 spec 在这里转正；外部若已手动 Init（测试流程）则跳过
            if (_specSerialized != null && CurrentPhase == Phase.Idle)
                Init(_specSerialized);
        }

        // ---------- 注入入口（LevelBuilder 编辑器期调用；运行期测试也可用） ----------

        /// <summary>注入关卡规格与 prefab 绑定（场景保存前调用，随场景序列化）。</summary>
        public void InjectForBuild(LevelSpec spec, List<PrefabBinding> monsterBindings, List<PrefabBinding> projectileBindings)
        {
            _specSerialized = spec;
            _monsterPrefabs = monsterBindings ?? new List<PrefabBinding>();
            _projectilePrefabs = projectileBindings ?? new List<PrefabBinding>();
        }

        /// <summary>注入陷阱/传送门视觉精灵（编辑器期调用；全部可空）。</summary>
        public void InjectVisuals(Sprite spike, Sprite rock, Sprite flame, Sprite portal, Sprite tile)
        {
            _spikeSprite = spike;
            _rockSprite = rock;
            _flameSprite = flame;
            _portalSprite = portal;
            _tileSprite = tile;
        }

        /// <summary>注入像素机关表现（编辑器期调用）：地刺/落石单帧，火柱/传送门/火花为序列帧。</summary>
        public void InjectPixelVisuals(Sprite spike, Sprite rock, Sprite vent, Sprite ventHot,
            Sprite[] flame, Sprite[] portal, Sprite[] spark)
        {
            _spikeSprite = spike;
            _rockSprite = rock;
            _ventSprite = vent;
            _ventHotSprite = ventHot;
            _flameFrames = flame;
            _portalFrames = portal;
            _sparkFrames = spark;
        }

        private static bool Has(Sprite[] frames) => frames != null && frames.Length > 0;

        /// <summary>注入自定义布置委托（覆盖 LayoutFromSpec；用于测试或程序化变体关卡）。</summary>
        public void SetLayoutOverride(Action<LevelSpec> hook) => _layoutOverride = hook;

        // ---------- 主流程 ----------

        /// <summary>运行期入口：布置关卡 → 广播"战斗中"。</summary>
        public void Init(LevelSpec spec, Action<LevelSpec> layoutOverride = null)
        {
            if (spec == null) { Debug.LogWarning("[LevelDirector] Init 收到空 LevelSpec，关卡闲置"); return; }
            _spec = spec;
            _layoutOverride = layoutOverride ?? _layoutOverride;

            _hero = FindFirstObjectByType<HeroController>();
            _sideCamera = FindFirstObjectByType<SideCamera>();
            if (_sideCamera != null)
            {
                _sideCamera.SetTarget(_hero != null ? _hero.transform : null);
                _sideCamera.SetLevelBounds(0f, _spec.widthUnits, _spec.baseGroundY);
                _sideCamera.SnapToTarget();
            }

            // 弹丸池注册（勇者剑气/火球/箭走 ProjectilePool；prefab 缺失时对应武器静默降级）
            foreach (var b in _projectilePrefabs)
                if (b != null && !string.IsNullOrEmpty(b.key) && b.prefab != null)
                    ProjectilePool.Register(b.key, b.prefab);

            // 布置关卡
            if (_layoutOverride != null) _layoutOverride(_spec);
            else LayoutFromSpec();

            // 进入战斗阶段
            CurrentPhase = Phase.Fighting;
            _kills = 0;
            // 先退订再订阅：Init 被重复调用（测试流程）不会叠加重复计数
            GameEvents.EnemyDied -= OnEnemyDied;
            GameEvents.EnemyDied += OnEnemyDied;
            GameEvents.PublishLevelPhaseChanged(_spec.levelIndex, "战斗中");

            if (_bossRef != null)
                GameEvents.PublishBossHpChanged(_spec.bossMaxHp, _spec.bossMaxHp);
        }

        /// <summary>按 spec 摆放怪物与陷阱（构建委托未注入时的默认布置）。</summary>
        private void LayoutFromSpec()
        {
            if (_spec.monsters != null)
                foreach (var m in _spec.monsters) SpawnMonster(m);
            if (_spec.traps != null)
                foreach (var t in _spec.traps) SpawnTrap(t);
        }

        private void Update()
        {
            if (CurrentPhase != Phase.Fighting || _spec == null) return;
            switch (_spec.goalKind)
            {
                case LevelGoalKind.KillCount:
                    // killTarget<=0 视为配置错误：永不触发，防止开局即开传送门
                    if (_spec.killTarget > 0 && _kills >= _spec.killTarget) OpenPortal();
                    break;
                case LevelGoalKind.Elite:
                    if (IsDefeated(_eliteRef)) OpenPortal();
                    break;
                case LevelGoalKind.Boss:
                    if (IsDefeated(_bossRef)) OnBossDefeated();
                    break;
            }
        }

        /// <summary>目标是否已击败：null（被 Destroy）或 IsDead（池化未销毁）都算。</summary>
        private static bool IsDefeated(EnemyBase enemy) => enemy == null || enemy.IsDead;

        // ---------- 事件 ----------

        private void OnEnemyDied(int xp, Vector3 pos)
        {
            _kills++;
            // 经验桥接：怪物死亡的经验直接喂给勇者升级链（AddXp → RequestLevelUps 三选一）。
            // 若成长系统后续接入宝石拾取流程，移除这一行即可，事件计数不受影响。
            if (_hero != null && !_hero.IsDead) _hero.AddXp(xp);
        }

        // ---------- 怪物布置 ----------

        private GameObject ResolveMonsterPrefab(string type)
        {
            foreach (var b in _monsterPrefabs)
                if (b != null && b.prefab != null && b.key == type) return b.prefab;
            return null;
        }

        private void SpawnMonster(MonsterSpawn m)
        {
            if (string.IsNullOrEmpty(m.type))
            {
                Debug.LogWarning($"[LevelDirector] 关卡{_spec.levelIndex} 存在空类型怪物配置点，跳过");
                return;
            }
            var prefab = ResolveMonsterPrefab(m.type);
            if (prefab == null)
            {
                Debug.LogWarning($"[LevelDirector] 关卡{_spec.levelIndex} 缺少怪物 prefab 绑定：{m.type}，跳过 ({m.position})");
                return;
            }

            var go = Instantiate(prefab, m.position, Quaternion.identity, transform);
            // prefab 本体未设层（Default 层与碰撞矩阵互不相撞会掉出世界）——实例补挂 Enemy 层
            go.layer = StarVortex.Gameplay.World.TrapLayers.Enemy;

            var enemy = go.GetComponent<EnemyBase>();
            if (enemy == null)
            {
                Debug.LogWarning($"[LevelDirector] prefab {m.type} 缺少 EnemyBase 组件，跳过");
                Destroy(go);
                return;
            }
            enemy.Init(_hero != null ? _hero.transform : null);

            // tint 必须在 Init 之后：Init 会把 SpriteRenderer 颜色复位成白色（精英换色不丢）
            if (m.tint != Color.white)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.color = m.tint;
            }

            if (m.type == "EliteGuard") _eliteRef = enemy;
            else if (m.type == "BossKing") _bossRef = enemy;
        }

        // ---------- 陷阱布置 ----------

        private void SpawnTrap(TrapSpawn t)
        {
            switch (t.type)
            {
                case "Spike": SpawnSpike(t.position); break;
                case "FallingRock": SpawnFallingRock(t.position); break;
                case "FlameVent": SpawnFlameVent(t.position); break;
                default:
                    Debug.LogWarning($"[LevelDirector] 未知陷阱类型：{t.type}，跳过");
                    break;
            }
        }

        /// <summary>尖刺：2 格宽 Trigger 盒，视觉 tile 平铺（缺精灵时纯色方块兜底）。</summary>
        private void SpawnSpike(Vector2 pos)
        {
            var go = new GameObject($"SpikeTrap_{pos.x:F0}");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.layer = GroundLayer; // 触发走 Player↔Ground 矩阵

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = _spikeSprite != null ? _spikeSprite : WhiteFallback();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(2f, 0.5f);
            sr.sortingOrder = 5;
            if (_spikeSprite == null) sr.color = new Color(0.78f, 0.8f, 0.86f, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2f, 0.6f);
            col.offset = new Vector2(0f, 0.3f);

            go.AddComponent<SpikeTrap>();
        }

        /// <summary>落石：地面到生成点的高感应柱 + FallingRock 行为组件。</summary>
        private void SpawnFallingRock(Vector2 pos)
        {
            var go = new GameObject($"FallingRock_{pos.x:F0}");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.layer = GroundLayer;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(3f, 4f);
            col.offset = new Vector2(0f, 2f);

            var trap = go.AddComponent<FallingRock>();
            trap.rockSprite = _rockSprite;
        }

        /// <summary>喷火口：底座 + 火柱（喷发期显示）+ 触发区域。</summary>
        private void SpawnFlameVent(Vector2 pos)
        {
            var go = new GameObject($"FlameVent_{pos.x:F0}");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.layer = GroundLayer;

            bool pixel = _ventSprite != null && Has(_flameFrames);

            var ventGo = new GameObject("VentBase");
            ventGo.transform.SetParent(go.transform, false);
            var ventSr = ventGo.AddComponent<SpriteRenderer>();
            ventSr.sortingOrder = 4;
            if (pixel)
            {
                // 像素铁栅：16px = 0.5u，底边贴地
                ventSr.sprite = _ventSprite;
                ventGo.transform.localPosition = new Vector3(0f, _ventSprite.bounds.extents.y, 0f);
            }
            else
            {
                ventGo.transform.localPosition = new Vector3(0f, 0.17f, 0f);
                ventSr.sprite = _tileSprite != null ? _tileSprite : WhiteFallback();
                ventSr.drawMode = SpriteDrawMode.Tiled;
                ventSr.size = new Vector2(1f, 0.35f);
                ventSr.color = new Color(0.35f, 0.33f, 0.3f, 1f);
            }

            var flameGo = new GameObject("Flame");
            flameGo.transform.SetParent(go.transform, false);
            var flameSr = flameGo.AddComponent<SpriteRenderer>();
            flameSr.sortingOrder = 9;
            if (pixel)
            {
                // 火柱底部压在铁栅顶上；颜色略超 1 给 Bloom 一点发光
                float ventTop = _ventSprite.bounds.size.y;
                flameGo.transform.localPosition = new Vector3(0f, ventTop + _flameFrames[0].bounds.extents.y - 0.1f, 0f);
                flameSr.color = new Color(1.3f, 1.15f, 1.05f, 1f);
                flameGo.AddComponent<SpriteLoop>().Configure(_flameFrames, 14f);
            }
            else
            {
                flameGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                flameSr.sprite = _flameSprite != null ? _flameSprite : WhiteFallback();
                flameSr.color = new Color(1.15f, 0.6f, 0.18f, 0.9f); // HDR>1 喂 Bloom 发光
            }
            flameGo.SetActive(false); // 初始休眠，喷发段才显示

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.2f, 2.6f);
            col.offset = new Vector2(0f, 1.3f);

            var vent = go.AddComponent<FlameVent>();
            vent.flameRenderer = flameSr;
            vent.ventRenderer = ventSr;
            vent.pixelFrames = pixel;
            vent.ventHotSprite = _ventHotSprite;
        }

        // ---------- 传送门阶段 ----------

        private void OpenPortal()
        {
            if (CurrentPhase != Phase.Fighting) return;
            CurrentPhase = Phase.PortalOpen;
            GameEvents.PublishLevelPhaseChanged(_spec.levelIndex, "传送门开启");
            AudioService.Instance?.Play(SfxId.LevelUp);

            _portal = BuildPortal(new Vector2(_spec.portalX, GroundYAt(_spec.portalX)));
            StartCoroutine(PortalBlinkRoutine());
        }

        /// <summary>传送门视觉：竖长发光柱（HDR 颜色喂 Bloom）+ 触发盒。</summary>
        private GameObject BuildPortal(Vector2 pos)
        {
            var go = new GameObject("Portal");
            go.transform.SetParent(transform, false);
            go.transform.position = pos + new Vector2(0f, 2f); // 柱体中心抬到半高处
            go.layer = GroundLayer;

            SpriteRenderer sr;
            if (Has(_portalFrames))
            {
                // 像素电光：96px = 3u 高，内容底边在第 88 行——把内容底边放到地面上
                var visual = new GameObject("Visual");
                visual.transform.SetParent(go.transform, false);
                var frame = _portalFrames[0];
                float contentBottomFromCenter = (88f - frame.rect.height * 0.5f) / frame.pixelsPerUnit;
                visual.transform.localPosition = new Vector3(0f, contentBottomFromCenter - 2f, 0f);
                sr = visual.AddComponent<SpriteRenderer>();
                visual.AddComponent<SpriteLoop>().Configure(_portalFrames, 10f);
                _portalBaseColor = new Color(1.25f, 1.35f, 1.6f, 1f); // 略超 1：Bloom 下发蓝光
            }
            else
            {
                sr = go.AddComponent<SpriteRenderer>();
                _portalBaseColor = new Color(0.45f, 1.15f, 1.35f, 0.85f);
            }
            sr.color = _portalBaseColor;
            sr.sortingOrder = 10;
            if (Has(_portalFrames))
            {
                // 视觉已在子物体上配好
            }
            else if (_portalSprite != null)
            {
                sr.sprite = _portalSprite;
                go.transform.localScale = new Vector3(1.3f, 2f, 1f); // 96×192 占位图 → 约 1.2×3.8 柱体
            }
            else
            {
                sr.sprite = WhiteFallback();
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(1.2f, 3.8f);
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.4f, 3.8f);

            var touch = go.AddComponent<PortalTouch>();
            touch.Init(OnPortalEnter);
            return go;
        }

        /// <summary>传送门闪烁：柱体正弦呼吸 + 每 0.5s 弹一枚 FadeOutAndDie 火花（一次性视觉淡出自毁）。</summary>
        private IEnumerator PortalBlinkRoutine()
        {
            var sr = _portal != null ? _portal.GetComponentInChildren<SpriteRenderer>() : null;
            while (_portal != null && CurrentPhase == Phase.PortalOpen)
            {
                if (sr != null)
                {
                    float k = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
                    sr.color = new Color(_portalBaseColor.r * k, _portalBaseColor.g * k, _portalBaseColor.b * k, _portalBaseColor.a);
                }

                var spark = new GameObject("PortalSpark");
                spark.transform.position = _portal.transform.position + new Vector3(
                    UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-1.7f, 1.7f), 0f);
                var sparkSr = spark.AddComponent<SpriteRenderer>();
                if (Has(_sparkFrames))
                    sparkSr.sprite = _sparkFrames[UnityEngine.Random.Range(0, _sparkFrames.Length)]; // 像素星芒，1 倍大小
                else
                {
                    spark.transform.localScale = Vector3.one * 0.35f;
                    sparkSr.sprite = _portalSprite != null ? _portalSprite : WhiteFallback();
                }
                sparkSr.color = new Color(0.7f, 1.4f, 1.6f, 0.85f);
                sparkSr.sortingOrder = 11;
                spark.AddComponent<FadeOutAndDie>().life = 0.4f;

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void OnPortalEnter()
        {
            if (CurrentPhase != Phase.PortalOpen) return;
            ClearLevel(loadNext: true);
        }

        // ---------- 通关结算 ----------

        /// <summary>BOSS 战胜利：按合约以 BossHpChanged(0,0) 收尾血条，直接进结算（无传送门）。</summary>
        private void OnBossDefeated()
        {
            if (CurrentPhase != Phase.Fighting) return;
            GameEvents.PublishBossHpChanged(0f, 0f);
            ClearLevel(loadNext: false);
        }

        /// <summary>
        /// 通关：广播"通关" → 清理订阅 → 移交 GameManager 收口
        /// （LevelClear 状态 + 结算面板由成长系统提供；timeScale 的修改收口在 GameManager）。
        /// </summary>
        private void ClearLevel(bool loadNext)
        {
            CurrentPhase = Phase.Clear;
            GameEvents.PublishLevelPhaseChanged(_spec.levelIndex, "通关");
            GameEvents.EnemyDied -= OnEnemyDied; // 通关即清理订阅（勇者死亡/通关 → 清理订阅）

            var gm = GameManager.Instance;
            if (loadNext && !string.IsNullOrEmpty(_spec.nextSceneName))
            {
                // 移交 GameManager：进入 LevelClear 状态弹结算面板，"下一关"按钮走 ContinueToNextLevel
                if (gm != null) gm.HandleLevelClear(_spec.nextSceneName);
                else SceneManager.LoadScene(_spec.nextSceneName);
            }
            else
            {
                // BOSS 关/最终关：全通关结算
                if (gm != null) gm.CompleteRun();
                else SceneManager.LoadScene("MainMenu");
            }
        }

        // ---------- 工具 ----------

        /// <summary>x 处的地表高度（用于陷阱/传送门贴地；不在任何段内则回主地面高度）。</summary>
        public float GroundYAt(float x)
        {
            if (_spec?.groundSegments != null)
                foreach (var s in _spec.groundSegments)
                    if (x >= s.startX && x < s.endX) return s.groundY;
            return _spec != null ? _spec.baseGroundY : 0f;
        }

        /// <summary>运行期纯色方块兜底（Texture2D.whiteTexture 内存 Sprite；只用于运行时生成物，不进场景文件）。</summary>
        private static Sprite WhiteFallback()
        {
            if (_whiteFallback == null)
            {
                var tex = Texture2D.whiteTexture;
                _whiteFallback = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), tex.width); // PPU = 纹理宽 → 恰好 1×1 世界单位
            }
            return _whiteFallback;
        }
    }

    /// <summary>传送门触发器（极小组件：命中勇者回调 LevelDirector，不值得单独开文件）。</summary>
    public class PortalTouch : MonoBehaviour
    {
        private Action _onEnter;

        public void Init(Action onEnter) => _onEnter = onEnter;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_onEnter == null) return;
            if (other.GetComponentInParent<HeroController>() == null) return;
            _onEnter();
        }
    }
}
