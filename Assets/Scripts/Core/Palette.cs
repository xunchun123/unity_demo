using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>
    /// 全局调色板：霓虹风格的色板单一事实来源（编辑器搭建与运行时共用）。
    /// 面试点：色彩规范集中管理——"审美一致性"是工程约束出来的，不是靠感觉随手调的。
    /// </summary>
    public static class Palette
    {
        /// <summary>深空底色（相机背景/UI 面板底）。</summary>
        public static readonly Color Bg = new Color(0.043f, 0.055f, 0.102f);        // #0B0E1A
        public static readonly Color PanelBg = new Color(0.043f, 0.055f, 0.102f, 0.92f);
        public static readonly Color PanelBorder = new Color(0.25f, 0.88f, 1f, 0.5f);

        /// <summary>玩家/主色：霓虹青。</summary>
        public static readonly Color Cyan = new Color(0.25f, 0.88f, 1f);
        public static readonly Color CyanHdr = new Color(0.35f, 1.25f, 1.4f);

        /// <summary>经验/强调色：品红。</summary>
        public static readonly Color Magenta = new Color(1f, 0.31f, 0.85f);
        public static readonly Color MagentaHdr = new Color(1.35f, 0.4f, 1.15f);

        /// <summary>敌对色。</summary>
        public static readonly Color Red = new Color(1f, 0.35f, 0.35f);
        public static readonly Color Orange = new Color(1f, 0.62f, 0.26f);
        public static readonly Color Purple = new Color(0.61f, 0.42f, 1f);

        /// <summary>通用文本。</summary>
        public static readonly Color TextPrimary = new Color(0.9f, 0.92f, 0.95f);
        public static readonly Color TextDim = new Color(0.55f, 0.6f, 0.7f);
        public static readonly Color TextHdrGold = new Color(1.8f, 1.45f, 0.35f);  // HDR 金（暴击/新纪录）
    }
}
