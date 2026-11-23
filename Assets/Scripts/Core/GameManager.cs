using System;
using StarVortex.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarVortex.Core
{
    /// <summary>
    /// 游戏全局状态（横版闯关版）：
    /// MainMenu → WeaponSelect（选人）→ Playing ⇄ LevelUp/Paused → LevelClear（关卡结算）→ Playing（下一关）…
    /// 三关全过后在 LevelClear 里走"回主菜单"收尾；GameOver 由 HandlePlayerDeath 触发。
    /// </summary>
    public enum GameState { MainMenu, WeaponSelect, Playing, LevelUp, Paused, LevelClear, GameOver }

    /// <summary>
    /// 游戏总控：单例 + 状态机 + 场景流转。
    /// 面试点：状态机保证"升级暂停、手动暂停、死亡结算"三个时间冻结来源互斥，
    /// 所有 timeScale 的修改都收口在这里，避免多系统互相抢 timeScale 的经典 bug。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.MainMenu;
        public float RunTime { get; private set; }
        public int Kills { get; private set; }

        // ---------- 横版闯关：关卡进度（LevelClearPanel / 关卡目标 HUD 的数据源） ----------
        /// <summary>当前关号（1 起，ContinueToNextLevel 时 +1）。</summary>
        public int CurrentLevel { get; private set; } = 1;
        /// <summary>本关击杀数（每关结算后清零）。</summary>
        public int LevelKills { get; private set; }
        /// <summary>关卡总数：通关最后一关后 LevelClearPanel 显示全通关结算。</summary>
        public const int MaxLevel = 3;
        /// <summary>是否已通关全部关卡（LevelClear 面板区分"过关/通关"两种版式）。</summary>
        public bool AllLevelsCleared => CurrentLevel >= MaxLevel;

        /// <summary>上一局是否刷新了最高纪录（结算界面直接读，不用反推）。</summary>
        public bool LastRunWasNewRecord { get; private set; }

        /// <summary>事件节流计时器（RunClockTick 每 0.25 秒推送一次）。</summary>
        private float _clockAccumulator;

        private const string MenuScene = "MainMenu";
        // 横版闯关版：每关独立场景（Level1/2/3），新开局从第一关进入（选人界面在 Level1 里弹）
        private const string FirstLevelScene = "Level1";

        private void Awake()
        {
            // 单例 + 跨场景常驻；重复进入场景时销毁多余副本
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Time.timeScale = 1f;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; GameEvents.ClearAllSubscribers(); }
        }

        private void Update()
        {
            // Paused 也要响应 Esc：暂停恢复是高频操作，键盘/手柄玩家不能只能鼠标点"继续"
            if (State == GameState.Paused)
            {
                if (InputEscapePressed()) TogglePause();
                return;
            }
            if (State != GameState.Playing) return;
            RunTime += Time.deltaTime;

            // 节流推送时间给 HUD（每 0.25s），UI 不做每帧刷新
            _clockAccumulator += Time.deltaTime;
            if (_clockAccumulator >= 0.25f)
            {
                _clockAccumulator -= 0.25f;
                GameEvents.PublishRunClockTick(RunTime);
            }

            if (InputEscapePressed()) TogglePause();
        }

        /// <summary>兼容键盘/手柄的开始键（新版 Input System 轮询）。</summary>
        private static bool InputEscapePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && (kb.escapeKey.wasPressedThisFrame ||
                                  kb.pKey.wasPressedThisFrame && kb.leftCtrlKey.isPressed);
        }

        // ---------- 状态流转 ----------

        /// <summary>过关时抓的勇者进度快照（等级/经验/天赋/爽点/血蓝/职业），下一关引导器恢复；新开一局清空。</summary>
        public Gameplay.HeroController.Progress CarriedProgress { get; private set; }

        // 升级弹窗打开时达成了过关目标（典型：最后一击杀死 BOSS 的经验同时触发升级）：
        // 先记下，升级选完再进结算。否则 HandleLevelClear 在 LevelUp 状态被直接忽略，
        // 而 LevelDirector 已经进入 Clear 阶段不会再调第二次 → 永远停在打完 BOSS 的空房间里
        private bool _clearQueued;
        private string _queuedNextScene;

        public void StartNewRun()
        {
            CarriedProgress = null;
            _clearQueued = false;
            RunTime = 0f;
            Kills = 0;
            CurrentLevel = 1;
            LevelKills = 0;
            _pendingLevelUps = 0;
            _pendingNextScene = null;
            // 旧场景还活跃的对象先全部回池：否则它们挂在跨场景池根上变成"僵尸"
            // （不再被 Director 管理却占着 Enemy.AliveCount，几局后刷怪永久停摆）
            PoolService.Instance?.DespawnAllActive();
            // 状态切换到加载完成之后再做：加载过渡帧里旧场景仍以当前状态冻结，
            // 避免"已结算的场景突然恢复 Playing 开始刷怪/可移动"的脏帧
            var op = SceneManager.LoadSceneAsync(FirstLevelScene);
            if (op != null) op.completed += _ => BeginWeaponSelect();
            else BeginWeaponSelect();
        }

        /// <summary>
        /// 场景加载完成后先进入选人态：勇者冻结在出生点（timeScale=0，物理不模拟），
        /// 等玩家点武器卡注入 WeaponKit 后才正式开打。
        /// </summary>
        private void BeginWeaponSelect()
        {
            SetState(GameState.WeaponSelect);
            Time.timeScale = 0f;
        }

        /// <summary>选人确认（WeaponSelectPanel 点卡注入 WeaponKit 后调用）：进入第一关。</summary>
        public void ConfirmWeaponSelect()
        {
            if (State != GameState.WeaponSelect) return;
            SetState(GameState.Playing);
            Time.timeScale = 1f;
            // 广播关卡目标初值（LevelDirector 随后可用更具体的阶段文案覆盖）
            GameEvents.PublishLevelPhaseChanged(CurrentLevel, "战斗开始");
        }

        // 下一关场景名（HandleLevelClear 由 LevelDirector 携带 spec.nextSceneName 注入；
        // 空表示同场景续打或无下一关——ContinueToNextLevel 直接回 Playing）
        private string _pendingNextScene;

        /// <summary>
        /// 关卡目标达成（LevelDirector 在传送门进入 / BOSS 击败时调用）：冻结时间并弹出关卡结算。
        /// nextScene：下一关场景名（LevelSpec.nextSceneName），结算面板"下一关"按钮据此加载；
        /// 传 null/空 表示没有下一关（最终关），面板切换为全通关结算 + 回主菜单。
        /// </summary>
        public void HandleLevelClear(string nextScene = null)
        {
            if (State == GameState.LevelUp)
            {
                _clearQueued = true;
                _queuedNextScene = nextScene;
                return;
            }
            if (State != GameState.Playing) return;
            _pendingNextScene = nextScene;
            SetState(GameState.LevelClear);
            Time.timeScale = 0f;
        }

        /// <summary>
        /// 全通关入口（LevelDirector 最后一关的目标达成调用，替代其临时的 FinalClearRoutine）：
        /// 不带下一关场景的 HandleLevelClear——LevelClearPanel 读 AllLevelsCleared 自动切"远征完成"版式。
        /// </summary>
        public void CompleteRun() => HandleLevelClear(null);

        /// <summary>关卡结算面板点"下一关"：关号 +1、清本关击杀，回到 Playing（需要时加载下一关场景）。</summary>
        public void ContinueToNextLevel()
        {
            if (State != GameState.LevelClear) return;
            CurrentLevel++;
            LevelKills = 0;
            string next = _pendingNextScene;
            _pendingNextScene = null;

            if (!string.IsNullOrEmpty(next))
            {
                var hero = FindFirstObjectByType<Gameplay.HeroController>();
                if (hero != null) CarriedProgress = hero.CaptureProgress();

                // 与 StartNewRun 同一收口原则：状态切换放加载完成之后，避免加载过渡帧里的脏输入
                var op = SceneManager.LoadSceneAsync(next);
                if (op != null) op.completed += _ => ResumeFromLevelClear();
                else ResumeFromLevelClear();
                return;
            }
            ResumeFromLevelClear();
        }

        private void ResumeFromLevelClear()
        {
            SetState(GameState.Playing);
            Time.timeScale = 1f;
            GameEvents.PublishLevelPhaseChanged(CurrentLevel, "战斗开始");
        }

        public void BackToMenu()
        {
            PoolService.Instance?.DespawnAllActive();
            var op = SceneManager.LoadSceneAsync(MenuScene);
            if (op != null) op.completed += _ => { SetState(GameState.MainMenu); Time.timeScale = 1f; };
            else { SetState(GameState.MainMenu); Time.timeScale = 1f; }
        }

        /// <summary>
        /// 升级队列：一次经验可能连升多级，把次数排进队列逐个弹三选一。
        /// 面试点：升级弹窗与死亡/暂停的 timeScale 竞争在这里统一仲裁。
        /// </summary>
        private int _pendingLevelUps;

        public void RequestLevelUps(int count)
        {
            if (count <= 0) return;
            if (State == GameState.Playing)
            {
                _pendingLevelUps = count;
                SetState(GameState.LevelUp);
                Time.timeScale = 0f;
            }
            else if (State == GameState.LevelUp)
            {
                _pendingLevelUps += count; // 弹窗已开，追加排队
            }
            // 其他状态（死亡/暂停/菜单）忽略——此时不该弹出升级
        }

        public void FinishLevelUp()
        {
            if (State != GameState.LevelUp) return;
            _pendingLevelUps--;
            if (_pendingLevelUps > 0)
            {
                // 还有排队的升级：广播真实等级（连升时每补一次三选一，HUD 都要拿到当前等级，
                // 否则连升 3 级 HUD 永远显示 Lv.1）
                var hero = FindFirstObjectByType<Gameplay.HeroController>();
                GameEvents.PublishLevelUp(hero != null ? hero.Level : 1);
                return;
            }
            _pendingLevelUps = 0;
            SetState(GameState.Playing);
            Time.timeScale = 1f;

            if (_clearQueued)
            {
                _clearQueued = false;
                HandleLevelClear(_queuedNextScene);
            }
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) { SetState(GameState.Paused); Time.timeScale = 0f; }
            else if (State == GameState.Paused) { SetState(GameState.Playing); Time.timeScale = 1f; }
        }

        public void AddKill()
        {
            Kills++;
            LevelKills++; // 本关击杀（LevelClearPanel "本关击杀" 的数据源）
            GameEvents.PublishKillCountChanged(Kills);
        }

        /// <summary>玩家死亡：结算并存档最高纪录。</summary>
        public void HandlePlayerDeath()
        {
            if (State == GameState.GameOver) return;
            SetState(GameState.GameOver);
            Time.timeScale = 0f;

            // 等级直接从勇者组件读取（死亡时场景仍在，组件未销毁）
            var hero = FindFirstObjectByType<Gameplay.HeroController>();
            LastRunWasNewRecord = SaveSystem.TryUpdateBestRecord(new BestRecord
            {
                survivalSeconds = RunTime,
                kills = Kills,
                level = hero != null ? hero.Level : 1,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            });
            GameEvents.PublishPlayerDied();
        }

        public void RestartRun()
        {
            _pendingLevelUps = 0;
            StartNewRun();
        }

        private void SetState(GameState s)
        {
            if (State == s) return;
            State = s;
            Debug.Log($"[GM] State → {s}（timeScale={Time.timeScale}）");
            GameEvents.PublishGameStateChanged(s);
        }
    }
}
