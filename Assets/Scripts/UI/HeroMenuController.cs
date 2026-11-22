using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>主菜单控制器（横版版）：开始远征 → GameManager.StartNewRun（进 Level1 选人）。</summary>
    public class HeroMenuController : MonoBehaviour
    {
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private TMP_Text _bestRecordLabel;

        private void Awake()
        {
            _startButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
                GameManager.Instance.StartNewRun();
            });
            _quitButton.onClick.AddListener(() =>
            {
                AudioService.Instance?.PlayUiClick();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }

        private void Start()
        {
            var record = SaveSystem.LoadBest();
            _bestRecordLabel.text = record != null
                ? $"最佳纪录：{Mathf.FloorToInt(record.survivalSeconds / 60f):00}:{Mathf.FloorToInt(record.survivalSeconds % 60f):00} · 击杀 {record.kills} · Lv.{record.level}"
                : "暂无纪录 —— 来创造第一个";
        }
    }
}
