using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 暂停面板：Paused 状态显示"继续 / 重新开始 / 回主菜单"和操作说明。
    /// Esc 开关暂停由 GameManager 统一处理（它是 timeScale 的唯一写入者），本面板不重复响应 Esc，
    /// 否则同一帧会被切换两次（打开即关闭）。键盘：R 重新开始，M 回主菜单。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _menuButton;

        private bool _leaving;

        private void Awake()
        {
            _resumeButton.onClick.AddListener(Resume);
            _restartButton.onClick.AddListener(Restart);
            _menuButton.onClick.AddListener(BackToMenu);
        }

        private void OnEnable()
        {
            _leaving = false;
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            Tween.ScaleIn(this, _titleLabel.transform, 1f, 0.25f);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) Restart();
            else if (kb.mKey.wasPressedThisFrame) BackToMenu();
        }

        private void Resume()
        {
            if (_leaving) return;
            AudioService.Instance?.PlayUiClick();
            if (GameManager.Instance.State == GameState.Paused) GameManager.Instance.TogglePause();
        }

        private void Restart()
        {
            if (_leaving) return;
            _leaving = true;
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.RestartRun();
        }

        private void BackToMenu()
        {
            if (_leaving) return;
            _leaving = true;
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.BackToMenu();
        }
    }
}
