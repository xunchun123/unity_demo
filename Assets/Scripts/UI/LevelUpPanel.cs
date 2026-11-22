using System.Collections.Generic;
using StarVortex.Core;
using StarVortex.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 升级双栏面板（重做版）：左栏天赋点分配 + 右栏爽点三选一，底部"继续战斗"统一确认。
    /// 触发链：HeroController.AddXp → GameManager.RequestLevelUps（LevelUp 状态、timeScale=0）
    /// → HeroGameBootstrap 按状态激活本面板 → 玩家决策 → GameManager.FinishLevelUp()。
    /// 规则：
    /// - 左栏每行"+"消耗 1 天赋点（HeroStats.SpendTalent），实时刷新已投入与当前总值；点数可以攒着不加；
    /// - 右栏从"当前武器池 + 通用池"不放回抽 3 张（本局已拥有的不再出现，
    ///   目录里 maxRank&gt;1 的卡重复获取会标等级 II/III——当前爽点全是行为开关，默认不放回）；
    /// - 爽点必须选一张，天赋可以留着 → 两栏都确认后才能继续战斗；
    /// - 全部动画跑 unscaled time（升级暂停时 timeScale=0，Tween 默认 unscaled）。
    /// </summary>
    public class LevelUpPanel : MonoBehaviour
    {
        [Header("整体")]
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _headerLabel;
        [SerializeField] private TMP_Text _warningLabel;   // "请先选择一张爽点！"（未选卡时点继续会闪现）
        [SerializeField] private Button _continueButton;

        [Header("左栏·天赋点（行序 = TalentKind 枚举序：生命/攻击/攻速/移速/蓝量）")]
        [SerializeField] private TMP_Text _pointsLabel;          // "可分配 N 点"
        [SerializeField] private TMP_Text[] _talentInvested;     // "已投入 ×2"
        [SerializeField] private TMP_Text[] _talentValue;        // 当前总值（HP/蓝为数值，其余为百分比）
        [SerializeField] private Button[] _talentButtons;        // 每行"+"

        [Header("右栏·爽点三选一")]
        [SerializeField] private GameObject[] _boonRoots;        // 卡片根（高亮/变暗用）
        [SerializeField] private TMP_Text[] _boonNameLabels;
        [SerializeField] private TMP_Text[] _boonDescLabels;
        [SerializeField] private Image[] _boonBorders;           // 卡池归属染色：武器色 / 通用金
        [SerializeField] private TMP_Text[] _boonHotkeyLabels;   // 快捷键 1/2/3

        private HeroController _hero;
        private readonly List<string> _rolls = new();            // 本轮抽到的爽点 ID（长度 ≤3）
        private readonly List<int> _rollRanks = new();           // 与 _rolls 对齐：本次获取后的等级（1 起）
        private readonly Dictionary<string, int> _ranksThisRun = new(); // 本局已获取次数（不放回/II III 显示依据）
        private string _pickedBoonId;                            // 本轮已选爽点（null = 未选）
        private bool _boonRequired;                              // 卡池抽空时允许不选直接继续

        private static readonly Color UniversalBorder = Palette.TextHdrGold; // 通用池金边
        private static readonly Color PickedBorder = new Color(1.6f, 1.35f, 0.5f); // 选中高亮

        private void Awake()
        {
            // 显隐统一由 HeroGameBootstrap 管理（同 WeaponSelectPanel：Awake 自我隐藏
            // 会在运行时激活瞬间把自己关掉，OnEnable 不触发）
        }

        private void OnEnable()
        {
            GameEvents.LevelUp += OnLevelUp;
            GameEvents.TalentPointsChanged += OnTalentPointsChanged;
            // 引导器激活面板时升级事件可能已经广播完（AddXp 先于状态切换），直接按当前状态开抽
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.LevelUp)
            {
                var hero = Hero();
                Open(hero != null ? hero.Level : 1);
            }
        }

        private void OnDisable()
        {
            GameEvents.LevelUp -= OnLevelUp;
            GameEvents.TalentPointsChanged -= OnTalentPointsChanged;
        }

        private HeroController Hero()
        {
            // 每次打开都重新查找：跨关卡场景重载后勇者是新实例
            if (_hero == null) _hero = FindFirstObjectByType<HeroController>();
            return _hero;
        }

        // ---------- 打开 / 刷新 ----------

        private void OnLevelUp(int level) => Open(level);

        private void OnTalentPointsChanged(int _) => RefreshTalentRows();

        private void Open(int level)
        {
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            _headerLabel.text = $"升级！Lv.{level} — 分配天赋并选择爽点";
            _pickedBoonId = null;
            _warningLabel.gameObject.SetActive(false);

            RefreshTalentRows();
            RollBoons();

            AudioService.Instance?.Play(SfxId.LevelUp);
        }

        /// <summary>刷新左栏五行：剩余点数 / 已投入 / 当前总值；没点时"+"置灰（点数可攒着不加）。</summary>
        private void RefreshTalentRows()
        {
            var hero = Hero();
            if (hero == null) return;
            var stats = hero.Stats;

            _pointsLabel.text = stats.pendingTalentPoints > 0
                ? $"可分配 {stats.pendingTalentPoints} 点"
                : "点数已用完（也可以攒着下次加）";

            int[] invested = { stats.talentHp, stats.talentAttack, stats.talentAttackSpeed, stats.talentMove, stats.talentMana };
            string[] current =
            {
                $"{stats.MaxHp:0}",                                  // 生命：当前上限
                $"+{stats.talentAttack * 10}%",                      // 攻击加成
                $"+{stats.talentAttackSpeed * 8}%",                  // 攻速加成
                $"+{stats.talentMove * 5}%",                         // 移速加成
                $"{stats.MaxMana:0}",                                // 蓝量：当前上限
            };

            for (int i = 0; i < 5; i++)
            {
                if (i < _talentInvested.Length && _talentInvested[i] != null)
                    _talentInvested[i].text = invested[i] > 0 ? $"已投入 ×{invested[i]}" : "未投入";
                if (i < _talentValue.Length && _talentValue[i] != null)
                    _talentValue[i].text = current[i];
                if (i < _talentButtons.Length && _talentButtons[i] != null)
                    _talentButtons[i].interactable = stats.pendingTalentPoints > 0;
            }
        }

        /// <summary>
        /// 右栏抽卡：当前武器池 + 通用池 → 移除已达上限的（不放回）→ 洗牌取 3。
        /// 卡池抽空（后期全收集）时允许不选直接继续，不卡死升级流程。
        /// </summary>
        private void RollBoons()
        {
            _rolls.Clear();
            _rollRanks.Clear();

            var kit = HeroGameBootstrap.CurrentKit;
            var pool = new List<string>();
            if (kit != null && kit.boonPool != null) pool.AddRange(kit.boonPool);
            pool.AddRange(BoonCatalog.UniversalPool);
            if (kit == null)
                Debug.LogWarning("[LevelUpPanel] 未找到 WeaponKit（选人流程未走通？），仅从通用池抽卡");

            var hero = Hero();
            var owned = hero != null ? hero.Boons.Owned : null;
            pool.RemoveAll(id =>
            {
                int rank = _ranksThisRun.TryGetValue(id, out int r) ? r : 0;
                if (rank >= BoonCatalog.MaxRank(id)) return true;    // 已到叠加上限（不放回）
                if (_ranksThisRun.ContainsKey(id)) return false;     // 有等级记录但未到上限 → 可再刷
                return owned != null && OwnedHas(owned, id);         // 面板外的已拥有（防御性兜底）
            });

            // 简单洗牌不放回抽 3（同帧随机去重）
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int idx = Random.Range(0, pool.Count);
                string id = pool[idx];
                pool.RemoveAt(idx);
                _ranksThisRun.TryGetValue(id, out int rank);
                _rolls.Add(id);
                _rollRanks.Add(rank + 1);
            }

            _boonRequired = _rolls.Count > 0;
            FillBoonCards();
        }

        /// <summary>把抽卡结果写进三张卡：名称（含等级）/ 描述 / 边框色（武器色或通用金）/ 快捷键。</summary>
        private void FillBoonCards()
        {
            var kit = HeroGameBootstrap.CurrentKit;
            var weaponBorder = kit != null ? WeaponBorderColor(kit.kind) : Palette.Cyan;

            for (int i = 0; i < 3; i++)
            {
                bool has = i < _rolls.Count;
                if (_boonRoots[i] != null) _boonRoots[i].SetActive(has);
                if (!has) continue;

                string id = _rolls[i];
                _boonNameLabels[i].text = BoonCatalog.DisplayName(id, _rollRanks[i]);
                _boonDescLabels[i].text = BoonCatalog.Desc(id);
                bool universal = System.Array.IndexOf(BoonCatalog.UniversalPool, id) >= 0;
                _boonBorders[i].color = universal ? UniversalBorder : weaponBorder;
                _boonHotkeyLabels[i].text = (i + 1).ToString();

                // 卡片弹入（unscaled：升级暂停中 timeScale=0）
                Tween.ScaleIn(this, _boonRoots[i].transform, 1f, 0.24f);
            }
        }

        /// <summary>边框颜色按武器分池：一眼区分"本武器专属"与"通用"。</summary>
        private static Color WeaponBorderColor(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Staff: return Palette.Purple;
                case WeaponKind.Bow: return Palette.Acid;
                default: return Palette.Cyan;
            }
        }

        /// <summary>IReadOnlyCollection 的 Contains 兼容查找（不引 LINQ，集合很小）。</summary>
        private static bool OwnedHas(IReadOnlyCollection<string> owned, string id)
        {
            foreach (var ownedId in owned)
                if (ownedId == id) return true;
            return false;
        }

        // ---------- 交互 ----------

        /// <summary>天赋行"+"：成功扣 1 点则广播事件并刷新（HP/蓝上限变化同步给 HUD）。</summary>
        public void OnTalentPlus(int kindIndex)
        {
            var hero = Hero();
            if (hero == null) return;
            var kind = (TalentKind)kindIndex;

            if (!hero.Stats.SpendTalent(kind))
            {
                AudioService.Instance?.PlayWithPitch(SfxId.UiClick, 0.5f); // 没点了：低音"咚"提示
                return;
            }

            // HeroStats 是纯数据（契约不改它），事件由面板负责广播
            GameEvents.PublishTalentSpent(kind);
            GameEvents.PublishTalentPointsChanged(hero.Stats.pendingTalentPoints);

            // 体验细节：加生命上限顺手回等量血，加蓝量上限同步蓝条——升级要立刻"感觉到变强"
            if (kind == TalentKind.Hp)
            {
                hero.Heal(HeroStats.HpPerTalent);
                GameEvents.PublishPlayerHpChanged(hero.CurrentHp, hero.Stats.MaxHp);
            }
            else if (kind == TalentKind.Mana)
            {
                GameEvents.PublishPlayerManaChanged(hero.CurrentMana, hero.Stats.MaxMana);
            }

            AudioService.Instance?.PlayUiClick();
            RefreshTalentRows();
        }

        /// <summary>爽点卡点击 / 键盘 1-3：选 1 张 → Boons.Add 广播爽点事件，其余卡置灰锁定。</summary>
        public void OnBoonPicked(int index)
        {
            if (_pickedBoonId != null) return;                 // 一轮只能选一张
            if (index < 0 || index >= _rolls.Count) return;

            var hero = Hero();
            if (hero == null) return;

            _pickedBoonId = _rolls[index];
            _ranksThisRun[_pickedBoonId] = _rollRanks[index]; // 记录等级（II/III 显示用）
            hero.Boons.Add(_pickedBoonId);                     // BoonInventory 负责广播 BoonAcquired

            for (int i = 0; i < _rolls.Count; i++)
            {
                bool picked = i == index;
                _boonBorders[i].color = picked ? PickedBorder : Dim(_boonBorders[i].color);
                if (_boonRoots[i] != null)
                    _boonRoots[i].transform.localScale = Vector3.one * (picked ? 1.04f : 0.97f);
            }
            _warningLabel.gameObject.SetActive(false);
            AudioService.Instance?.Play(SfxId.Pickup);
        }

        private static Color Dim(Color c) => new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, c.a * 0.6f);

        /// <summary>底部"继续战斗"：爽点必须选一张（卡池抽空除外），天赋可以留着。</summary>
        public void OnContinueClicked()
        {
            if (_boonRequired && _pickedBoonId == null)
            {
                _warningLabel.gameObject.SetActive(true);
                _warningLabel.text = "请先选择一张爽点卡！";
                Tween.ScaleIn(this, _warningLabel.transform, 1f, 0.3f); // unscaled 弹跳提醒
                AudioService.Instance?.PlayWithPitch(SfxId.UiClick, 0.6f);
                return;
            }
            AudioService.Instance?.PlayUiClick();
            // 连升多级时 FinishLevelUp 不会切回 Playing，而是重新广播 LevelUp → 本面板自动重开新一轮
            GameManager.Instance.FinishLevelUp();
        }

        private void Update()
        {
            // 键盘快捷流：1/2/3 选爽点，Enter 确认（老玩家全程不碰鼠标）
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) OnBoonPicked(0);
            if (kb.digit2Key.wasPressedThisFrame) OnBoonPicked(1);
            if (kb.digit3Key.wasPressedThisFrame) OnBoonPicked(2);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) OnContinueClicked();
        }
    }
}
