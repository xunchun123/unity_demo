using StarVortex.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>升级三选一的单张卡片：图标 + 名称 + 描述 + 稀有度边框 + 快捷键角标。</summary>
    public class LevelUpCard : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _descLabel;
        [SerializeField] private TMP_Text _hotkeyLabel;
        [SerializeField] private Image _border;
        [SerializeField] private Button _button;

        public UpgradeData Data { get; private set; }

        private static readonly Color[] RarityColors =
        {
            new Color(0.55f, 0.85f, 1f, 1f),    // Common 青蓝
            new Color(0.6f, 1f, 0.62f, 1f),     // Rare 翠绿
            new Color(1.8f, 1.2f, 0.45f, 1f),   // Epic 橙金（HDR 喂 Bloom）
        };

        /// <summary>面板打开时由 LevelUpPanel 调用，完成视觉填充与点击绑定。</summary>
        public void Init(UpgradeData data, int index, System.Action<UpgradeData> onPicked)
        {
            Data = data;
            _icon.sprite = data.icon ? data.icon : null;
            _icon.color = data.icon ? data.iconColor : Color.white;
            _nameLabel.text = data.displayName;
            _descLabel.text = data.description;
            _hotkeyLabel.text = (index + 1).ToString();
            _border.color = RarityColors[(int)data.rarity];

            // 重新绑定点击（卡片会被反复重建，避免残留上次闭包）
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => onPicked?.Invoke(data));
        }
    }
}
