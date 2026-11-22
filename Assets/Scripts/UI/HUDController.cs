using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// HUD：血条 / 经验条 / 计时 / 击杀数 / 等级。
    /// 面试点：事件驱动刷新——HUD 订阅 GameEvents，只在数值变化时更新；
    /// 血条宽度用 SmoothDamp 缓动（掉血有"流动感"而不是瞬间跳变）。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("血条")]
        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _hpGhostFill; // 白色残影：延迟跟随，掉血反馈更"疼"
        [SerializeField] private TMP_Text _hpLabel;

        [Header("经验条（底部通栏）")]
        [SerializeField] private Image _xpFill;

        [Header("数字区")]
        [SerializeField] private TMP_Text _timerLabel;
        [SerializeField] private TMP_Text _killsLabel;
        [SerializeField] private TMP_Text _levelLabel;

        // 血条残影缓动状态
        private float _hpDisplay = 1f;
        private float _hpTarget = 1f;
        private float _hpGhost = 1f;
        private int _lastMinute = -1;

        private void OnEnable()
        {
            GameEvents.PlayerHpChanged += OnHpChanged;
            GameEvents.XpChanged += OnXpChanged;
            GameEvents.RunClockTick += OnClockTick;
            GameEvents.KillCountChanged += OnKills;
            GameEvents.LevelUp += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.PlayerHpChanged -= OnHpChanged;
            GameEvents.XpChanged -= OnXpChanged;
            GameEvents.RunClockTick -= OnClockTick;
            GameEvents.KillCountChanged -= OnKills;
            GameEvents.LevelUp -= OnLevelUp;
        }

        private void OnHpChanged(float cur, float max)
        {
            _hpTarget = max > 0f ? cur / max : 0f;
            _hpLabel.text = $"{Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}";
        }

        private void OnXpChanged(int cur, int need)
        {
            _xpFill.fillAmount = need > 0f ? (float)cur / need : 0f;
        }

        private void OnClockTick(float seconds)
        {
            int minute = Mathf.FloorToInt(seconds / 60f);
            int sec = Mathf.FloorToInt(seconds % 60f);
            _timerLabel.text = $"{minute:00}:{sec:00}";
            // 每进入新的一分钟，计时器脉冲一次（提示难度升级）
            if (minute != _lastMinute && minute > 0)
            {
                _lastMinute = minute;
                Tween.ScaleIn(this, _timerLabel.transform, 1.25f, 0.18f);
            }
        }

        private void OnKills(int kills) => _killsLabel.text = kills.ToString();

        private void OnLevelUp(int level) => _levelLabel.text = $"Lv.{level}";

        private void Update()
        {
            // 血条残影动画：主条立刻跟随，残影条慢速追赶（视觉上"先掉血、再流血"）
            if (!Mathf.Approximately(_hpGhost, _hpTarget))
            {
                _hpGhost = Mathf.MoveTowards(_hpGhost, _hpTarget, Time.unscaledDeltaTime * 0.35f);
                _hpGhostFill.fillAmount = _hpGhost;
            }
            _hpDisplay = Mathf.MoveTowards(_hpDisplay, _hpTarget, Time.unscaledDeltaTime * 2.5f);
            _hpFill.fillAmount = _hpDisplay;
        }
    }
}
