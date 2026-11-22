using System;
using StarVortex.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarVortex.Core
{
    /// <summary>游戏全局状态。</summary>
    public enum GameState { MainMenu, Playing, LevelUp, Paused, GameOver }

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

        /// <summary>上一局是否刷新了最高纪录（结算界面直接读，不用反推）。</summary>
        public bool LastRunWasNewRecord { get; private set; }

        /// <summary>事件节流计时器（RunClockTick 每 0.25 秒推送一次）。</summary>
        private float _clockAccumulator;

        private const string MenuScene = "MainMenu";
        private const string GameScene = "GameScene";

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

        public void StartNewRun()
        {
            RunTime = 0f;
            Kills = 0;
            SetState(GameState.Playing);
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(GameScene);
        }

        public void BackToMenu()
        {
            SetState(GameState.MainMenu);
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(MenuScene);
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
                // 还有排队的升级：再次广播，面板重新抽卡
                GameEvents.PublishLevelUp(1);
                return;
            }
            _pendingLevelUps = 0;
            SetState(GameState.Playing);
            Time.timeScale = 1f;
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) { SetState(GameState.Paused); Time.timeScale = 0f; }
            else if (State == GameState.Paused) { SetState(GameState.Playing); Time.timeScale = 1f; }
        }

        public void AddKill()
        {
            Kills++;
            GameEvents.PublishKillCountChanged(Kills);
        }

        /// <summary>玩家死亡：结算并存档最高纪录。</summary>
        public void HandlePlayerDeath()
        {
            if (State == GameState.GameOver) return;
            SetState(GameState.GameOver);
            Time.timeScale = 0f;

            // 等级直接从玩家组件读取（死亡时场景仍在，组件未销毁）
            var player = FindFirstObjectByType<Gameplay.PlayerController>();
            LastRunWasNewRecord = SaveSystem.TryUpdateBestRecord(new BestRecord
            {
                survivalSeconds = RunTime,
                kills = Kills,
                level = player != null ? player.Stats.Level : 1,
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
            GameEvents.PublishGameStateChanged(s);
        }
    }
}
