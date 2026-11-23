using StarVortex.Core;
using StarVortex.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 选人界面（游戏开局）：三张武器卡（长剑/法杖/弓箭），点击选中后把对应
    /// WeaponKit 注入 HeroController，再进入第一关。
    /// 触发链：主菜单"开始游戏" → GameManager.StartNewRun（加载 GameScene 后进入 WeaponSelect 态，
    /// timeScale=0 勇者冻结）→ HeroGameBootstrap 激活本面板 → 点卡注入 → GameManager.ConfirmWeaponSelect()。
    /// 卡片文案全部由搭建脚本按 WeaponKit SO 预生成，运行时只填 .text（字体显式走 TMP 中文字体）。
    /// </summary>
    public class WeaponSelectPanel : MonoBehaviour
    {
        [Header("整体")]
        [SerializeField] private CanvasGroup _group;

        [Header("三张武器卡（序号 = 卡位，构建脚本按 Kit 数组对齐注入）")]
        [SerializeField] private GameObject[] _cardRoots;
        [SerializeField] private Button[] _cardButtons;
        [SerializeField] private TMP_Text[] _nameLabels;    // 武器名
        [SerializeField] private TMP_Text[] _descLabels;    // 一句话描述（Kit.description）
        [SerializeField] private TMP_Text[] _statLabels;    // 攻击形态/节奏标签
        [SerializeField] private TMP_Text[] _poolLabels;    // 专属爽点池列表
        [SerializeField] private TMP_Text[] _hotkeyLabels;  // 快捷键 1/2/3

        [Header("武器 Kit SO（构建脚本注入 Assets/Data/Hero/Weapon_*.asset）")]
        [SerializeField] private WeaponKit[] _kits;

        private bool _starting; // 防连点：一帧内双卡同选会注入两次

        /// <summary>开放的职业数：三个职业都有了完整的像素帧动画，全部开放（改回 1 即只开放长剑）。</summary>
        private const int UnlockedCount = 3;

        private void Awake()
        {
            // 显隐统一由 HeroGameBootstrap 管理——这里不再自我隐藏：
            // 场景里保存的初始状态就是隐藏，运行时引导器 SetActive(true) 激活瞬间
            // 才触发 Awake，若在此处再 SetActive(false) 会把自己关掉（实测坑：
            // 面板永远停在激活前一帧，OnEnable 不触发）
        }

        private void OnEnable()
        {
            Debug.Log("[WeaponSelect] 面板激活");
            // 状态事件可能先于激活发布（StartNewRun 完成回调先于面板 Start），按当前状态直接填充
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.WeaponSelect)
                Populate();
        }

        /// <summary>把三个 Kit 的文案填进卡片；每次激活都重填（换局重进时 Kit 资产可能被改）。</summary>
        private void Populate()
        {
            Debug.Log($"[WeaponSelect] Populate kits={(_kits != null ? _kits.Length : 0)} cardRoots={(_cardRoots != null ? _cardRoots.Length : -1)} nameLabels={(_nameLabels != null ? _nameLabels.Length : -1)}");
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _starting = false;

            int count = Mathf.Min(_kits != null ? _kits.Length : 0, _cardRoots.Length);
            for (int i = 0; i < _cardRoots.Length; i++)
            {
                if (_cardRoots[i] != null) _cardRoots[i].SetActive(i < count);
                if (i >= count) continue;
                FillCard(i, _kits[i]);
            }

            // 卡片弹入（unscaled：选人态 timeScale=0，动画必须走真实时间）
            for (int i = 0; i < count; i++)
                if (_cardRoots[i] != null)
                    Tween.ScaleIn(this, _cardRoots[i].transform, 1f, 0.3f);
        }

        private void FillCard(int i, WeaponKit kit)
        {
            Debug.Log($"[WeaponSelect] FillCard {i}: kit={kit.displayName} nameLabel={(_nameLabels[i] != null)} cardRoot={(_cardRoots[i] != null)}");
            bool locked = i >= UnlockedCount;

            if (_nameLabels[i] != null) _nameLabels[i].text = kit.displayName;
            if (_descLabels[i] != null) _descLabels[i].text = locked ? "后续版本开放" : kit.description;
            if (_statLabels[i] != null) _statLabels[i].text = locked ? "未解锁" : StatTag(kit);
            // 爽点池不上明面（用户要求）：未解锁卡一句话，解锁卡只留一句抽取说明
            //（文案避开"敬/待"等不在 732 字烘焙集的汉字，防 TMP 缺字方框）
            if (_poolLabels[i] != null)
                _poolLabels[i].text = locked ? "· 后续版本开放" : "升级时三选一随机抽取\n（含本武器专属强化）";
            if (_hotkeyLabels[i] != null) _hotkeyLabels[i].text = (i + 1).ToString();

            if (_cardButtons[i] != null)
            {
                _cardButtons[i].interactable = !locked; // 未解锁卡置灰且不可点
                _cardButtons[i].onClick.RemoveAllListeners(); // 重入安全：清掉上局残留闭包
                int index = i;                                // 闭包捕获局部变量，避免晚绑定踩 i
                _cardButtons[i].onClick.AddListener(() => Select(index));
            }
        }

        /// <summary>攻击形态标签：把"这武器手感如何"压缩成一行（气功特意注明普攻免费，蓝只供新星）。</summary>
        private static string StatTag(WeaponKit kit)
        {
            switch (kit.kind)
            {
                case WeaponKind.Sword: return "近战连击 · 攻速 0.35s";
                case WeaponKind.Staff: return "远程气功波 0.45s · 普攻免费，蓝供 K 新星";
                case WeaponKind.Bow: return "远程点射 0.5s · 可蓄力";
                default: return kit.attackInterval.ToString("0.00") + "s";
            }
        }

        /// <summary>点卡选中：注入 WeaponKit → 确认进入第一关（GameManager 切 Playing 并恢复时间流）。</summary>
        public void Select(int index)
        {
            if (_starting) return;
            if (_kits == null || index < 0 || index >= _kits.Length) return;
            if (index >= UnlockedCount)
            {
                // 未锁定武器：低音"咚"提示，不进入游戏
                AudioService.Instance?.PlayWithPitch(SfxId.UiClick, 0.5f);
                return;
            }

            _starting = true;
            AudioService.Instance?.PlayUiClick();

            var hero = FindFirstObjectByType<HeroController>();
            if (hero == null)
            {
                Debug.LogError("[WeaponSelectPanel] 场景里找不到 HeroController，无法注入武器");
                _starting = false;
                return;
            }
            HeroGameBootstrap.InjectWeaponKit(hero, _kits[index]);

            Tween.ScaleIn(this, _cardRoots[index].transform, 1.06f, 0.2f);
            GameManager.Instance.ConfirmWeaponSelect(); // → Playing：引导器随即隐藏本面板
        }

        private void Update()
        {
            // 键盘 1/2/3 快选
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) Select(0);
            if (kb.digit2Key.wasPressedThisFrame) Select(1);
            if (kb.digit3Key.wasPressedThisFrame) Select(2);
        }
    }
}
