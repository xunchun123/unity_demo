using StarVortex.Core;
using StarVortex.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 死亡结算面板：GameOver 状态显示"勇者倒下了"+ 本局击杀 / 用时 / 等级 / 是否新纪录，
    /// 提供"重新开始"（回第一关重新选武器）与"回主菜单"。
    /// 触发链：HeroController 血量归零 → GameManager.HandlePlayerDeath（GameOver 态、timeScale=0、存最佳纪录）
    /// → HeroGameBootstrap 激活本面板。
    /// 体验细节：面板延迟 1 秒再淡入，先让玩家看清倒地动作（勇者 Animator 死亡时切到真实时间播放）；
    /// 淡入完成前不接受输入，防止玩家死前连按的攻击键直接把结算跳过。
    /// </summary>
    public class GameOverPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _statsLabel;
        [SerializeField] private TMP_Text _recordLabel;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _menuButton;

        private const float RevealDelay = 1.0f;
        private const float FadeDuration = 0.4f;

        private bool _interactive; // 淡入完成后才响应按钮与按键
        private bool _leaving;     // 防连点：场景加载期间只处理一次

        private void Awake()
        {
            _restartButton.onClick.AddListener(Restart);
            _menuButton.onClick.AddListener(BackToMenu);
        }

        private void OnEnable()
        {
            _interactive = false;
            _leaving = false;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            Fill();
            Tween.Start(this, RevealDelay, _ => { }, onComplete: Reveal);
        }

        private void Fill()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            var hero = HeroGameBootstrap.CurrentHero != null ? HeroGameBootstrap.CurrentHero : FindFirstObjectByType<HeroController>();
            int level = hero != null ? hero.Level : 1;
            int seconds = Mathf.FloorToInt(gm.RunTime);

            _titleLabel.text = "勇者倒下了";
            _statsLabel.text = $"第 {gm.CurrentLevel} 关   击杀 {gm.Kills}   用时 {seconds / 60:00}:{seconds % 60:00}   Lv.{level}";
            _recordLabel.text = gm.LastRunWasNewRecord ? "★ 新纪录！" : string.Empty;
        }

        private void Reveal()
        {
            Tween.Start(this, FadeDuration, k => _group.alpha = k, onComplete: () =>
            {
                _group.blocksRaycasts = true;
                _interactive = true;
            });
            Tween.ScaleIn(this, _titleLabel.transform, 1f, 0.35f);
            if (_recordLabel.text.Length > 0) Tween.ScaleIn(this, _recordLabel.transform, 1f, 0.5f);
        }

        private void Update()
        {
            if (!_interactive) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame) Restart();
            else if (kb.escapeKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame) BackToMenu();
        }

        private void Restart()
        {
            if (!_interactive || _leaving) return;
            _leaving = true;
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.RestartRun();
        }

        private void BackToMenu()
        {
            if (!_interactive || _leaving) return;
            _leaving = true;
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.BackToMenu();
        }
    }
}
