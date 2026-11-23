using System.Collections;
using StarVortex.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// HUD（横版闯关版）：血条+残影 / 蓝条 / 经验条 / 计时 / 击杀 / 等级
    /// + 天赋点角标（可分配点数呼吸提示）/ BOSS 血条（上方大条）/ 操作提示条（开局 14s 后淡出，
    /// 第二行补成长引导：打败怪物得经验 → 升级选天赋/爽点）
    /// / 关卡目标文本（第 N 关 · 阶段）。
    /// 面试点：事件驱动刷新——HUD 订阅 GameEvents，只在数值变化时更新；
    /// 血条宽度用 SmoothDamp 缓动（掉血有"流动感"而不是瞬间跳变）；
    /// 全部显示逻辑不感知暂停状态，动画一律 unscaled time（升级/结算时 timeScale=0 照常跑）。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("血条")]
        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _hpGhostFill; // 白色残影：延迟跟随，掉血反馈更"疼"
        [SerializeField] private TMP_Text _hpLabel;

        [Header("蓝条（血条下方细条：气功普攻免费，蓝供 K 气爆新星）")]
        [SerializeField] private Image _manaFill;

        [Header("天赋点角标（pendingTalentPoints>0 时显示，呼吸提醒去升级面板加点）")]
        [SerializeField] private GameObject _talentBadgeRoot;
        [SerializeField] private TMP_Text _talentBadgeLabel;

        [Header("BOSS 血条（屏幕上方大条；BossHpChanged(0,0) 视为死亡契约，隐藏）")]
        [SerializeField] private GameObject _bossRoot;
        [SerializeField] private Image _bossFill;

        [Header("经验条（底部通栏）")]
        [SerializeField] private Image _xpFill;

        [Header("数字区")]
        [SerializeField] private TMP_Text _timerLabel;
        [SerializeField] private TMP_Text _killsLabel;
        [SerializeField] private TMP_Text _levelLabel;

        [Header("操作提示条（开局显示，14 秒后淡出，unscaled）")]
        [SerializeField] private CanvasGroup _hintGroup;
        [SerializeField] private GameObject _hintRoot;

        [Header("关卡目标（订阅 LevelPhaseChanged）")]
        [SerializeField] private TMP_Text _objectiveLabel;

        // 血条/蓝条缓动状态
        private float _hpDisplay = 1f;
        private float _hpTarget = 1f;
        private float _hpGhost = 1f;
        private float _manaTarget = 1f;
        private float _manaDisplay = 1f;
        private int _lastMinute = -1;
        private bool _hintShown;          // 操作提示只展示一次（重开局随场景重建重置）
        private bool _hintExpanded;       // 提示条第二行（成长引导）只追加一次
        private int _lastLevel = 1;       // 关卡目标文本缓存（BOSS 阶段广播的 level=-1 时沿用）
        private Coroutine _hintRoutine;

        private void OnEnable()
        {
            GameEvents.PlayerHpChanged += OnHpChanged;
            GameEvents.PlayerManaChanged += OnManaChanged;
            GameEvents.XpChanged += OnXpChanged;
            GameEvents.RunClockTick += OnClockTick;
            GameEvents.KillCountChanged += OnKills;
            GameEvents.LevelUp += OnLevelUp;
            GameEvents.TalentPointsChanged += OnTalentPointsChanged;
            GameEvents.BossHpChanged += OnBossHpChanged;
            GameEvents.LevelPhaseChanged += OnLevelPhaseChanged;
            GameEvents.GameStateChanged += OnGameStateChanged;
        }

        private void OnDisable()
        {
            GameEvents.PlayerHpChanged -= OnHpChanged;
            GameEvents.PlayerManaChanged -= OnManaChanged;
            GameEvents.XpChanged -= OnXpChanged;
            GameEvents.RunClockTick -= OnClockTick;
            GameEvents.KillCountChanged -= OnKills;
            GameEvents.LevelUp -= OnLevelUp;
            GameEvents.TalentPointsChanged -= OnTalentPointsChanged;
            GameEvents.BossHpChanged -= OnBossHpChanged;
            GameEvents.LevelPhaseChanged -= OnLevelPhaseChanged;
            GameEvents.GameStateChanged -= OnGameStateChanged;
        }

        // ---------- 各事件刷新 ----------

        private void OnHpChanged(float cur, float max)
        {
            _hpTarget = max > 0f ? cur / max : 0f;
            _hpLabel.text = $"{Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}";
        }

        private void OnManaChanged(float cur, float max)
        {
            _manaTarget = max > 0f ? cur / max : 0f;
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
            // 每进入新的一分钟，计时器脉冲一次（提示难度升级）；脉冲后必须回落，否则永久停在 1.25 倍
            if (minute != _lastMinute && minute > 0)
            {
                _lastMinute = minute;
                StartCoroutine(TimerPulse());
            }
        }

        private void OnKills(int kills) => _killsLabel.text = kills.ToString();

        private void OnLevelUp(int level) => _levelLabel.text = $"Lv.{level}";

        private void OnTalentPointsChanged(int points)
        {
            bool visible = points > 0;
            _talentBadgeRoot.SetActive(visible);
            if (!visible) return;
            _talentBadgeLabel.text = $"可分配 {points} 点";
            Tween.ScaleIn(this, _talentBadgeRoot.transform, 1f, 0.3f); // 来点数时弹一下，视觉"有事发生"
        }

        private void OnBossHpChanged(float cur, float max)
        {
            // BOSS 死亡契约：(0,0) → 收起血条；出场 (max,max) → 弹入
            if (max <= 0f || cur <= 0f)
            {
                _bossRoot.SetActive(false);
                return;
            }
            if (!_bossRoot.activeSelf)
            {
                _bossRoot.SetActive(true);
                Tween.ScaleIn(this, _bossRoot.transform, 1f, 0.35f);
            }
            _bossFill.fillAmount = cur / max;
        }

        private void OnLevelPhaseChanged(int level, string phase)
        {
            // BOSS 阶段广播用 level=-1（BossKing 阶段切换），沿用上次关卡号、只换阶段文案
            if (level >= 1) _lastLevel = level;
            _objectiveLabel.text = level >= 1 ? $"第 {level} 关 · {phase}" : phase;
        }

        private void OnGameStateChanged(GameState state)
        {
            // 开局（武器选完进入第一关）展示操作提示，14 秒后淡出——只展示一次
            if (state == GameState.Playing && !_hintShown)
            {
                _hintShown = true;
                if (_hintRoutine != null) StopCoroutine(_hintRoutine);
                _hintRoutine = StartCoroutine(HintFadeRoutine());
            }
        }

        /// <summary>
        /// 提示条第二行：成长引导（打败怪物得经验 → 升级选天赋与爽点）。运行时追加而非改 UI 构建器，
        /// 所有用字都核对过 ChineseChars 烘焙集（打败怪物得经验升级可选天赋与爽点·全部在集内），
        /// 不需要补烘字形；字号缩到 70% + 灰蓝配色，作主操作行下的次级信息。
        /// </summary>
        private void EnsureHintSubtitle()
        {
            if (_hintExpanded) return;
            _hintExpanded = true;
            var text = _hintRoot != null ? _hintRoot.GetComponentInChildren<TMP_Text>(true) : null;
            if (text == null) return;
            text.text += "\n<size=70%><color=#8fa3c8>打败怪物得经验 · 升级可选天赋与爽点</color></size>";
            // 单行条变两行：底边不动、顶边向上扩 34px 容两行（提示条悬在经验条上方，向下长会压条）
            if (_hintRoot.transform is RectTransform rt)
                rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMax.y + 34f);
        }

        /// <summary>提示条：立即显示 → 停 14s（真实时间，暂停/升级冻结不影响）→ 1.2s 淡出收起。</summary>
        private IEnumerator HintFadeRoutine()
        {
            EnsureHintSubtitle(); // 首次显示前补成长引导行（HintBar 初始隐藏，inactive 下也可安全改文本）
            _hintRoot.SetActive(true);
            _hintGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(14f);
            yield return Tween.Start(this, 1.2f, k => _hintGroup.alpha = 1f - k, Tween.EaseInQuad);
            _hintRoot.SetActive(false);
        }

        private IEnumerator TimerPulse()
        {
            Tween.ScaleIn(this, _timerLabel.transform, 1.25f, 0.18f);
            yield return new WaitForSecondsRealtime(0.55f);
            Tween.ScaleIn(this, _timerLabel.transform, 1f, 0.25f);
        }

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

            // 蓝条平滑跟随（回复是连续值，直接 lerp 就有"回蓝流动感"）
            if (!Mathf.Approximately(_manaDisplay, _manaTarget))
            {
                _manaDisplay = Mathf.MoveTowards(_manaDisplay, _manaTarget, Time.unscaledDeltaTime * 0.8f);
                _manaFill.fillAmount = _manaDisplay;
            }

            // 天赋角标呼吸：有可分配点数时持续提醒（unscaled，暂停界面下方也在呼吸）
            if (_talentBadgeRoot.activeSelf)
            {
                float s = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 3.2f);
                _talentBadgeRoot.transform.localScale = Vector3.one * s;
            }
        }
    }
}
