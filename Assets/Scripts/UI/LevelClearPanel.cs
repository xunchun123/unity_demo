using StarVortex.Core;
using StarVortex.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 关卡结算面板：LevelClear 状态显示"第 N 关 通过！"+ 本关击杀 + "下一关"按钮；
    /// 通关第 3 关（GameManager.MaxLevel）切换为全通关结算：总击杀 / 用时 / 等级 + 回主菜单。
    /// 触发链：LevelDirector 目标达成 → GameManager.HandleLevelClear（LevelClear 态、timeScale=0）
    /// → HeroGameBootstrap 激活本面板 → 玩家点按钮 → ContinueToNextLevel / BackToMenu。
    /// 数字滚动走 Tween（默认 unscaled），暂停态动画正常播放。
    /// </summary>
    public class LevelClearPanel : MonoBehaviour
    {
        [Header("整体")]
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _titleLabel;   // "第 N 关 通过！" / "远征完成！"

        [Header("数据行（0=本关击杀 1=总击杀 2=用时 3=等级；构建脚本注入标签与数值）")]
        [SerializeField] private GameObject[] _rows;         // 整行根（两种版式按行显隐）
        [SerializeField] private TMP_Text[] _valueLabels;    // 数值列（滚动动画写这里）

        [Header("按钮（过关显示『下一关』；全通关显示『回主菜单』）")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _menuButton;

        private bool _showing; // 防止状态事件与"激活时补触发"双重进入

        private void Awake()
        {
            // 显隐统一由 HeroGameBootstrap 管理（自我隐藏会在激活瞬间把自己关掉，同 WeaponSelectPanel）
            _continueButton.onClick.AddListener(OnContinueClicked);
            _menuButton.onClick.AddListener(OnMenuClicked);
        }

        private void OnEnable()
        {
            GameEvents.GameStateChanged += OnGameStateChanged;
            // 引导器激活面板时状态事件可能已广播完，按当前状态直接结算
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.LevelClear)
                Open();
        }

        private void OnDisable() => GameEvents.GameStateChanged -= OnGameStateChanged;

        private void OnGameStateChanged(GameState state)
        {
            if (state == GameState.LevelClear) Open();
            else _showing = false; // 离开结算态（继续/回菜单/死亡）后允许下一关复用
        }

        private void Open()
        {
            if (_showing) return;
            _showing = true;

            var gm = GameManager.Instance;
            bool final = gm.AllLevelsCleared;

            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _titleLabel.text = final ? "远征完成！" : $"第 {gm.CurrentLevel} 关 通过！";
            _titleLabel.color = final ? Palette.TextHdrGold : Palette.GhostGreen;

            // 两种版式的行显隐：过关只看本关/累计击杀；全通关看总击杀/用时/等级
            SetRowVisible(0, !final); // 本关击杀
            SetRowVisible(1, true);   // 总击杀
            SetRowVisible(2, final);  // 用时
            SetRowVisible(3, final);  // 等级
            _continueButton.gameObject.SetActive(!final);
            _menuButton.gameObject.SetActive(final);

            // 数值从 0 滚到真值（unscaled：结算时 timeScale=0）
            Tween.CountTo(this, 0.9f, 0, gm.LevelKills, v => SetValue(0, v.ToString()));
            Tween.CountTo(this, 1.1f, 0, gm.Kills, v => SetValue(1, v.ToString()));
            Tween.CountTo(this, 1.1f, 0, Mathf.FloorToInt(gm.RunTime),
                v => SetValue(2, $"{Mathf.FloorToInt(v / 60f):00}:{v % 60:00}"));
            var hero = FindFirstObjectByType<HeroController>();
            int level = hero != null ? hero.Level : 1;
            Tween.CountTo(this, 0.8f, 0, level, v => SetValue(3, $"Lv.{v}"));

            Tween.ScaleIn(this, _titleLabel.transform, 1f, 0.35f);
            AudioService.Instance?.Play(SfxId.LevelUp);
        }

        private void SetRowVisible(int index, bool visible)
        {
            if (index < _rows.Length && _rows[index] != null) _rows[index].SetActive(visible);
            if (!visible) SetValue(index, string.Empty);
        }

        private void SetValue(int index, string text)
        {
            if (index < _valueLabels.Length && _valueLabels[index] != null)
                _valueLabels[index].text = text;
        }

        private void OnContinueClicked()
        {
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.ContinueToNextLevel(); // 加载下一关场景后回到 Playing
        }

        private void OnMenuClicked()
        {
            AudioService.Instance?.PlayUiClick();
            GameManager.Instance.BackToMenu();
        }
    }
}
