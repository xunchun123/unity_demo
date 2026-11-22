using StarVortex.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 暂停面板：纯视图，只负责展示；显隐由 GameSceneBootstrap 根据状态事件驱动。
    /// Esc/P 的监听在 GameManager（状态机统一入口），面板不重复监听。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _menuButton;

        private void Awake()
        {
            _group.alpha = 1f;
            _resumeButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
                GameManager.Instance.TogglePause();
            });
            _menuButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
                GameManager.Instance.BackToMenu();
            });
        }
    }
}
