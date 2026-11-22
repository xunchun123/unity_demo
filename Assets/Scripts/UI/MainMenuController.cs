using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 主菜单：开始 / 最高纪录展示 / 退出。
    /// 面试点：最高纪录读 Json 存档展示——给"再来一局"提供目标感（留存钩子）。
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private TMP_Text _bestRecordLabel;
        [SerializeField] private RectTransform _titleTransform;

        private void Start()
        {
            _startButton.onClick.AddListener(OnStart);
            _quitButton.onClick.AddListener(OnQuit);
            ShowBestRecord();
            // 标题呼吸浮动：菜单"活着"的第一要素
            LeanTweenLike.FloatingLoop(this, _titleTransform, 14f, 0.5f);
        }

        private void ShowBestRecord()
        {
            var best = SaveSystem.LoadBest();
            if (best == null)
            {
                _bestRecordLabel.text = "暂无纪录 —— 来创造第一个";
                return;
            }
            int m = Mathf.FloorToInt(best.survivalSeconds / 60f);
            int s = Mathf.FloorToInt(best.survivalSeconds % 60f);
            _bestRecordLabel.text = $"最佳纪录  存活 {m:00}:{s:00} · 击杀 {best.kills} · 等级 {best.level}";
        }

        private void OnStart()
        {
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.StartNewRun();
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
