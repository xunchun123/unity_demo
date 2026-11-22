using System.Collections;
using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 结算面板：数字滚动 + 新纪录徽章 + 重开/回菜单。
    /// 面试点：结算数字用 CountTo 滚动动画——数据相同，"滚动 2 秒"和"瞬间显示"的情绪价值完全不同。
    /// </summary>
    public class GameOverPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _timeLabel;
        [SerializeField] private TMP_Text _killsLabel;
        [SerializeField] private TMP_Text _levelLabel;
        [SerializeField] private TMP_Text _newRecordLabel;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _menuButton;

        private bool _showing; // 防止"激活时补触发"和事件推送双重进入结算

        private void Awake()
        {
            _restartButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
                GameManager.Instance.RestartRun();
            });
            _menuButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
                GameManager.Instance.BackToMenu();
            });
        }

        private void OnEnable()
        {
            GameEvents.PlayerDied += OnPlayerDied;
            // 引导器在 GameOver 状态激活本面板时（死亡事件可能先于激活发布），直接进入结算
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.GameOver)
                OnPlayerDied();
        }

        private void OnDisable() => GameEvents.PlayerDied -= OnPlayerDied;

        private void OnPlayerDied()
        {
            if (_showing) return;
            _showing = true;
            StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            // 玩家死亡瞬间不做弹窗（让死亡粒子播完），延迟 0.9s（真实时间）再结算
            yield return new WaitForSecondsRealtime(0.9f);

            var gm = GameManager.Instance;
            bool isNewRecord = gm.LastRunWasNewRecord; // 存档系统直接给结论，不再反推

            _group.alpha = 1f;
            gameObject.SetActive(true);
            _newRecordLabel.gameObject.SetActive(isNewRecord);
            _timeLabel.text = "00:00";
            _killsLabel.text = "0";
            _levelLabel.text = "1";

            AudioService.Instance?.Play(SfxId.GameOver);

            Tween.CountTo(this, 1.2f, 0, Mathf.FloorToInt(gm.RunTime),
                v => _timeLabel.text = $"{Mathf.FloorToInt(v / 60f):00}:{v % 60:00}");
            Tween.CountTo(this, 1.2f, 0, gm.Kills, v => _killsLabel.text = v.ToString());
            var player = FindFirstObjectByType<StarVortex.Gameplay.PlayerController>();
            _levelLabel.text = $"Lv.{(player != null ? player.Stats.Level : 1)}";

            if (isNewRecord) Tween.ScaleIn(this, _newRecordLabel.transform, 1f, 0.4f);
        }
    }
}
