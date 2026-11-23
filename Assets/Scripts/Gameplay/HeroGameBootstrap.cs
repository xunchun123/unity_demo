using System.Reflection;
using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 横版场景引导器（每关场景各挂一个，随场景加载/卸载，不 DDOL）。
    /// 职责：
    /// ① 注册 ProjectilePool（火球/剑气/箭）——LevelDirector.Init 也会按其绑定表注册（按 key 覆盖，
    ///    幂等），这里是"选人/升级面板在 Director 就绪前也不踩空"的兜底；
    /// ② 初始化 HUD 真实初始值：读 HeroController 的真实 HP/蓝/经验/等级推给事件总线
    ///    （修复旧版"占位符 0/100"教训——UI 初始显示必须来自组件真实状态，而非写死的假数据）；
    /// ③ 把勇者引用喂给 LevelDirector（它 Init 时也会自查，这里兜底补注，双保险不抢权限）；
    /// ④ 提供选人界面的 WeaponKit 注入入口（HeroController 是契约文件不改，反射写私有字段）。
    /// 注意：旧 scripts/Gameplay/GameSceneBootstrap.cs 是太空版遗留，本文件是横版替代品，
    /// 面板显隐也由它统一驱动（订阅 GameStateChanged，面板自己不重复订阅状态事件）。
    /// </summary>
    public class HeroGameBootstrap : MonoBehaviour
    {
        [Header("勇者引用（构建脚本注入；空则运行时自动查找）")]
        [SerializeField] private HeroController _hero;

        [Header("弹丸池（构建脚本注入 Assets/Prefabs/Hero/proj_*.prefab；空项跳过）")]
        [SerializeField] private GameObject _fireballPrefab;   // 气功波（复用火球弹体）
        [SerializeField] private GameObject _swordWavePrefab;  // 长剑剑气
        [SerializeField] private GameObject _arrowPrefab;      // 火铳子弹（池键沿用 Arrow）

        [Header("面板（构建脚本注入；按状态事件驱动显隐）")]
        [SerializeField] private GameObject _weaponSelectPanel;
        [SerializeField] private GameObject _levelUpPanel;
        [SerializeField] private GameObject _levelClearPanel;
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private GameObject _pausePanel;

        /// <summary>当前勇者（选人/升级面板等 UI 兜底查找用，避免每面板各查一遍）。</summary>
        public static HeroController CurrentHero { get; private set; }

        /// <summary>当前武器 Kit（选人注入后生效；升级面板按它取专属爽点池）。</summary>
        public static WeaponKit CurrentKit { get; private set; }

        private void Awake()
        {
            CurrentHero = _hero != null ? _hero : FindFirstObjectByType<HeroController>();

            // 弹丸池注册：先清跨场景残留注册表，再补注册三个勇者弹丸（key 与 ProjectilePool 常量一致）
            ProjectilePool.Clear();
            if (_fireballPrefab != null) ProjectilePool.Register(ProjectilePool.Fireball, _fireballPrefab);
            if (_swordWavePrefab != null) ProjectilePool.Register(ProjectilePool.SwordWave, _swordWavePrefab);
            if (_arrowPrefab != null) ProjectilePool.Register(ProjectilePool.Arrow, _arrowPrefab);
        }

        private void OnEnable()
        {
            // 场景加载完成回调（SetState）可能早于本对象 OnEnable——先按当前状态摆一遍面板，
            // 再订阅后续切换（与旧面板"激活时补触发"是同一个坑的正确解法）
            GameEvents.GameStateChanged += ApplyState;
            if (GameManager.Instance != null) ApplyState(GameManager.Instance.State);
        }

        private void OnDisable() => GameEvents.GameStateChanged -= ApplyState;

        private void Start()
        {
            if (CurrentHero == null)
                CurrentHero = FindFirstObjectByType<HeroController>();
            TryFeedHeroToLevelDirector();

            // HUD 初始值：全部读勇者真实状态（HP/蓝/经验/等级/天赋点），不再有占位符假数据
            var hero = CurrentHero;
            if (hero == null)
            {
                Debug.LogWarning("[HeroBootstrap] 场景里没有 HeroController，HUD 初始值跳过");
                return;
            }
            GameEvents.PublishPlayerHpChanged(hero.CurrentHp, hero.Stats.MaxHp);
            GameEvents.PublishPlayerManaChanged(hero.CurrentMana, hero.Stats.MaxMana);
            GameEvents.PublishXpChanged(hero.Xp, hero.XpToNext);
            GameEvents.PublishLevelUp(hero.Level);
            GameEvents.PublishTalentPointsChanged(hero.Stats.pendingTalentPoints);
            GameEvents.PublishKillCountChanged(GameManager.Instance != null ? GameManager.Instance.Kills : 0);
            // BossHpChanged / LevelPhaseChanged 归 LevelDirector / BossKing 发布，这里不代发
        }

        private void OnDestroy()
        {
            // 随关卡场景卸载：清静态引用 + 弹丸注册表（LevelDirector.OnDestroy 也清，双保险），
            // 防止下一关读到旧勇者引用或旧预制体
            CurrentHero = null;
            CurrentKit = null;
            ProjectilePool.Clear();
        }

        // ---------- 状态 → 面板显隐（HUD 常驻不归这里管） ----------

        private void ApplyState(GameState state)
        {
            Debug.Log("[Bootstrap] ApplyState " + state + " weaponSelectWired=" + (_weaponSelectPanel != null));
            SetPanelActive(_weaponSelectPanel, state == GameState.WeaponSelect);
            SetPanelActive(_levelUpPanel, state == GameState.LevelUp);
            SetPanelActive(_levelClearPanel, state == GameState.LevelClear);
            SetPanelActive(_gameOverPanel, state == GameState.GameOver);
            SetPanelActive(_pausePanel, state == GameState.Paused);
        }

        private static void SetPanelActive(GameObject panel, bool active)
        {
            if (panel == null) { Debug.LogWarning("[Bootstrap] SetPanelActive：面板引用为空"); return; }
            Debug.Log($"[Bootstrap] SetPanelActive {panel.name} → {active}（当前 {panel.activeSelf}）");
            if (panel.activeSelf != active) panel.SetActive(active);
        }

        // ---------- WeaponKit 注入（选人界面调用） ----------

        /// <summary>
        /// 把武器 Kit 装备给勇者（同时切换职业外观），并缓存到 CurrentKit，升级面板据此抽"专属池 + 通用池"。
        /// </summary>
        public static void InjectWeaponKit(HeroController hero, WeaponKit kit)
        {
            if (hero == null || kit == null) return;
            hero.EquipKit(kit);
            CurrentKit = kit;
        }

        /// <summary>勇者引用兜底喂给 LevelDirector（它 Init 时自查为主，这里只在它还没找到时补注）。</summary>
        private void TryFeedHeroToLevelDirector()
        {
            var director = World.LevelDirector.Instance;
            if (director == null || CurrentHero == null) return;

            var field = typeof(World.LevelDirector).GetField("_hero",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.GetValue(director) != null) return; // 已找到就不抢
            field.SetValue(director, CurrentHero);
        }
    }
}
