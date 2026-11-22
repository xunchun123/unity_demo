using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;
using StarVortex.Gameplay;
using StarVortex.UI;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>一键搭建器（UGUI 部分）：HUD/升级/结算/暂停四套界面 + 主菜单场景。</summary>
    public static partial class DemoSceneBuilder
    {
        // ============================ UGUI 助手 ============================

        internal static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        internal static void CreateEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(); // 新版 Input System 的 UI 模块
        }

        private static RectTransform Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static Image AddImage(GameObject parent, string name, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent.transform, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        private static TMP_Text AddText(GameObject parent, string name, string content, float size,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent.transform, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            if (_cjkFont != null) tmp.font = _cjkFont; // 显式中文字体（不依赖 TMP 全局设置）
            return tmp;
        }

        /// <summary>带底和填充的进度条（血条/经验条通用）。</summary>
        private static (Image bg, Image fill, Image ghost) AddBar(GameObject parent, string name, Color fillColor, float height)
        {
            var bg = AddImage(parent, name + "_Bg", new Color(0.1f, 0.13f, 0.22f, 0.85f));
            Rect(bg.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, height));
            var fill = AddImage(bg.gameObject, name + "_Fill", fillColor);
            Rect(fill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            Image ghost = null;
            return (bg, fill, ghost);
        }

        private static Button AddButton(GameObject parent, string label, Vector2 size, Color accent)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.09f, 0.12f, 0.2f, 0.95f);
            var btn = go.GetComponent<Button>();

            var tmp = AddText(go, "Label", label, 30f, accent);
            Rect(tmp.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 按钮边框感：底部一条 accent 色高亮线（现代 UI 的轻量描边）
            var underline = AddImage(go, "Underline", accent);
            Rect(underline.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 0f), new Vector2(-10f, 4f));

            // hover 色变
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.3f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.8f);
            btn.colors = colors;
            return btn;
        }

    }
}
