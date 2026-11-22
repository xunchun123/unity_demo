using StarVortex.Core;
using StarVortex.Gameplay;
using StarVortex.UI;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// GameScene 引导器：场景加载后的运行时装配（编辑器期由 DemoSceneBuilder 完成静态接线，
    /// 这里只做"必须等场景跑起来才能做"的事）。
    /// 面试点：引导器模式让场景内组件保持"哑组件"——依赖在装配期注入，运行期零查找成本。
    /// </summary>
    public class GameSceneBootstrap : MonoBehaviour
    {
        [Header("场景关键节点")]
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private PlayerController _player;
        [SerializeField] private PausePanel _pausePanel;
        [SerializeField] private LevelUpPanel _levelUpPanel;
        [SerializeField] private GameOverPanel _gameOverPanel;
        [SerializeField] private Canvas _hudCanvas;

        private void Start()
        {
            _cameraRig.SetTarget(_player.transform);

            // 面板显隐统一由状态机事件驱动（面板自己是纯视图，避免 OnEnable 订阅死锁）
            _pausePanel.gameObject.SetActive(GameManager.Instance.State == GameState.Paused);
            _levelUpPanel.gameObject.SetActive(GameManager.Instance.State == GameState.LevelUp);
            _gameOverPanel.gameObject.SetActive(GameManager.Instance.State == GameState.GameOver);
            GameEvents.GameStateChanged += OnStateChanged;
        }

        private void OnDestroy() => GameEvents.GameStateChanged -= OnStateChanged;

        private void OnStateChanged(GameState state)
        {
            SetActiveIfChanged(_pausePanel, state == GameState.Paused);
            SetActiveIfChanged(_levelUpPanel, state == GameState.LevelUp);
            SetActiveIfChanged(_gameOverPanel, state == GameState.GameOver);
        }

        private static void SetActiveIfChanged(MonoBehaviour panel, bool show)
        {
            if (panel != null && panel.gameObject.activeSelf != show)
                panel.gameObject.SetActive(show);
        }
    }
}
