using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
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

        private static Canvas CreateCanvas(string name, int sortingOrder)
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

        private static void CreateEventSystem()
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

        // ============================ HUD ============================

        private static void BuildHud(Transform canvasRoot)
        {
            var hud = new GameObject("HUD", typeof(RectTransform), typeof(CanvasGroup));
            hud.transform.SetParent(canvasRoot, false);
            Rect(hud, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 左上：血条（残影条 = 延迟跟随的白条，掉血"先掉血再流血"）
            var hpGroup = new GameObject("HpGroup", typeof(RectTransform));
            hpGroup.transform.SetParent(hud.transform, false);
            Rect(hpGroup, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -96f), new Vector2(480f, -28f));

            var hpBg = AddImage(hpGroup, "HpBg", new Color(0.1f, 0.13f, 0.22f, 0.85f));
            Rect(hpBg.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0.55f), Vector2.zero, Vector2.zero);

            var ghost = AddImage(hpBg.gameObject, "HpGhost", new Color(1f, 1f, 1f, 0.45f));
            Rect(ghost.gameObject, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var hpFill = AddImage(hpBg.gameObject, "HpFill", Palette.Red);
            Rect(hpFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(-2f, -2f));

            var hpLabel = AddText(hpGroup, "HpLabel", "100 / 100", 22f, Palette.TextPrimary, TextAlignmentOptions.Left);
            Rect(hpLabel.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(2f, 0f), Vector2.zero);

            // 顶中：计时器（等宽数字观感：TMP 的 tabular figures 默认够用）
            var timer = AddText(hud, "Timer", "00:00", 46f, Palette.TextPrimary);
            Rect(timer.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-160f, -110f), new Vector2(160f, -30f));

            // 右上：击杀 / 等级
            var kills = AddText(hud, "Kills", "击杀 0", 26f, Palette.TextPrimary, TextAlignmentOptions.Right);
            Rect(kills.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-360f, -86f), new Vector2(-28f, -34f));

            var level = AddText(hud, "Level", "Lv.1", 26f, Palette.CyanHdr, TextAlignmentOptions.Right);
            Rect(level.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-360f, -134f), new Vector2(-28f, -82f));

            // 底部：经验条（全宽薄片，VS 式布局）
            var xp = AddImage(hud, "XpBg", new Color(0.1f, 0.13f, 0.22f, 0.9f));
            Rect(xp.gameObject, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            var xpHolder = AddImage(xp.gameObject, "XpHolder", new Color(0f, 0f, 0f, 0f));
            Rect(xpHolder.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var xpFill = AddImage(xpHolder.gameObject, "XpFill", Palette.Magenta);
            Rect(xpFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));

            // 注入 HUDController
            var hudObj = hud.AddComponent<HUDController>();
            SetField(hudObj, "_hpFill", hpFill);
            SetField(hudObj, "_hpGhostFill", ghost);
            SetField(hudObj, "_hpLabel", hpLabel);
            SetField(hudObj, "_xpFill", xpFill);
            SetField(hudObj, "_timerLabel", timer);
            SetField(hudObj, "_killsLabel", kills);
            SetField(hudObj, "_levelLabel", level);
        }

        // ============================ 升级三选一 ============================

        private static GameObject BuildLevelUpPanel(Transform canvasRoot)
        {
            var panel = new GameObject("LevelUpPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.88f);
            panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

            var header = AddText(panel, "Header", "升级！选择一项强化", 54f, Palette.TextHdrGold);
            Rect(header.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-400f, -220f), new Vector2(400f, -120f));

            // 三张卡片的横排容器
            var cards = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cards.transform.SetParent(panel.transform, false);
            Rect(cards, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-820f, -160f), new Vector2(820f, 160f));
            var layout = cards.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 44f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var levelPanel = panel.AddComponent<LevelUpPanel>();
            SetField(levelPanel, "_group", panel.GetComponent<CanvasGroup>());
            SetField(levelPanel, "_cardsRoot", (RectTransform)cards.transform);
            SetField(levelPanel, "_cardPrefab", BuildLevelUpCardPrefab());
            SetField(levelPanel, "_headerLabel", header);
            SetField(levelPanel, "_upgradePool", _upgrades);
            return panel;
        }

        /// <summary>升级卡片预制体（纯 UGUI 嵌套）。</summary>
        private static LevelUpCard BuildLevelUpCardPrefab()
        {
            var card = new GameObject("LevelUpCard", typeof(RectTransform), typeof(Image), typeof(Button));
            var img = card.GetComponent<Image>();
            img.color = new Color(0.07f, 0.09f, 0.16f, 0.97f);

            // 稀有度边框：底层一个放大 6px 的 Image 当描边
            var border = AddImage(card, "Border", Palette.Cyan);
            Rect(border.gameObject, Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
            border.transform.SetAsFirstSibling();
            var borderImg = border.GetComponent<Image>();
            borderImg.color = Palette.Cyan;

            // 快捷键角标（右上角小方块）
            var hotkeyBg = AddImage(card, "HotkeyBg", new Color(1f, 1f, 1f, 0.08f));
            Rect(hotkeyBg.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-58f, -58f), Vector2.zero);
            var hotkey = AddText(hotkeyBg.gameObject, "Hotkey", "1", 26f, Palette.TextDim);
            Rect(hotkey.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 图标
            var icon = AddImage(card, "Icon", Color.white);
            Rect(icon.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-70f, -190f), new Vector2(70f, -50f));

            var nameLabel = AddText(card, "Name", "强化名称", 34f, Palette.TextPrimary);
            Rect(nameLabel.gameObject, new Vector2(0f, 0.52f), new Vector2(1f, 0.68f), new Vector2(24f, 0f), new Vector2(-24f, 0f));

            var descLabel = AddText(card, "Desc", "效果描述", 26f, Palette.TextDim, TextAlignmentOptions.Top);
            Rect(descLabel.gameObject, new Vector2(0f, 0.08f), new Vector2(1f, 0.5f), new Vector2(28f, 0f), new Vector2(-28f, 0f));

            var cardScript = card.AddComponent<LevelUpCard>();
            SetField(cardScript, "_icon", icon);
            SetField(cardScript, "_nameLabel", nameLabel);
            SetField(cardScript, "_descLabel", descLabel);
            SetField(cardScript, "_hotkeyLabel", hotkey);
            SetField(cardScript, "_border", borderImg);
            SetField(cardScript, "_button", card.GetComponent<Button>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(card, $"{PrefabDir}/LevelUpCard.prefab");
            Object.DestroyImmediate(card);
            return prefab.GetComponent<LevelUpCard>();
        }

        // ============================ 结算 / 暂停 ============================

        private static GameObject BuildGameOverPanel(Transform canvasRoot)
        {
            var panel = new GameObject("GameOverPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.05f, 0.94f);
            panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

            var title = AddText(panel, "Title", "死亡结算", 64f, Palette.Red);
            Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -200f), new Vector2(300f, -100f));

            // 三行数据：左标签列 + 右数值列（各自独立，数字逐个滚动）
            string[] statNames = { "存活时间", "击杀数", "最终等级" };
            var timeLabels = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float yTop = -60f + i * -78f;
                var statLabel = AddText(panel, $"Stat{i}", statNames[i], 30f, Palette.TextDim, TextAlignmentOptions.Right);
                Rect(statLabel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-430f, yTop - 40f), new Vector2(-40f, yTop));
                var valueLabel = AddText(panel, $"Value{i}", i == 0 ? "00:00" : i == 1 ? "0" : "Lv.1", 34f,
                    Palette.TextPrimary, TextAlignmentOptions.Left);
                Rect(valueLabel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(40f, yTop - 40f), new Vector2(430f, yTop));
                timeLabels[i] = valueLabel;
            }

            var newRecord = AddText(panel, "NewRecord", "★ 新纪录 ★", 40f, Palette.TextHdrGold);
            Rect(newRecord.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200f, 170f), new Vector2(200f, 240f));

            var restart = AddButton(panel, "再来一局", new Vector2(300f, 76f), Palette.Cyan);
            Rect(restart.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-330f, 160f), new Vector2(-30f, 236f));
            var menu = AddButton(panel, "返回菜单", new Vector2(300f, 76f), Palette.TextDim);
            Rect(menu.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(30f, 160f), new Vector2(330f, 236f));

            var script = panel.AddComponent<GameOverPanel>();
            SetField(script, "_group", panel.GetComponent<CanvasGroup>());
            SetField(script, "_timeLabel", timeLabels[0]);
            SetField(script, "_killsLabel", timeLabels[1]);
            SetField(script, "_levelLabel", timeLabels[2]);
            SetField(script, "_newRecordLabel", newRecord);
            SetField(script, "_restartButton", restart);
            SetField(script, "_menuButton", menu);
            return panel;
        }

        private static GameObject BuildPausePanel(Transform canvasRoot)
        {
            var panel = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.9f);

            var title = AddText(panel, "Title", "已暂停", 64f, Palette.TextPrimary);
            Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -300f), new Vector2(300f, -180f));

            var hint = AddText(panel, "Hint", "按 Esc 继续", 26f, Palette.TextDim);
            Rect(hint.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -360f), new Vector2(300f, -310f));

            var resume = AddButton(panel, "继续游戏", new Vector2(300f, 76f), Palette.Cyan);
            Rect(resume.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160f, -40f), new Vector2(140f, 36f));
            var menu = AddButton(panel, "回主菜单", new Vector2(300f, 76f), Palette.TextDim);
            Rect(menu.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160f, -140f), new Vector2(140f, -64f));

            var script = panel.AddComponent<PausePanel>();
            SetField(script, "_group", panel.GetComponent<CanvasGroup>());
            SetField(script, "_resumeButton", resume);
            SetField(script, "_menuButton", menu);
            return panel;
        }

        // ============================ 主菜单场景 ============================

        private static void BuildMainMenuScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Bg;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);

            var lightGo = new GameObject("GlobalLight");
            var global = lightGo.AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 1f;
            global.color = new Color(0.86f, 0.9f, 1f);
            CreateBloomVolume();
            BuildBackground(cameraGo.transform);

            // 管理器（音频在菜单就开播，跨场景常驻）
            var managers = new GameObject("Managers");
            managers.AddComponent<GameManager>();
            managers.AddComponent<PoolService>();
            managers.AddComponent<Juice>();
            var audio = managers.AddComponent<AudioService>();
            BindAudio(audio);

            // ---------- 菜单 UI ----------
            var canvas = CreateCanvas("Canvas_Menu", 0);
            CreateEventSystem();

            var title = AddText(canvas.gameObject, "Title", "星涡幸存者", 128f, Palette.TextPrimary);
            Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-560f, -320f), new Vector2(560f, -120f));
            title.color = Palette.CyanHdr; // HDR 青，Bloom 下发光

            var subtitle = AddText(canvas.gameObject, "Subtitle", "STARVORTEX SURVIVORS", 30f, Palette.TextDim);
            Rect(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-400f, -380f), new Vector2(400f, -335f));

            var best = AddText(canvas.gameObject, "BestRecord", "暂无纪录", 26f, Palette.TextDim);
            Rect(best.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460f, -40f), new Vector2(460f, 40f));

            var start = AddButton(canvas.gameObject, "开始游戏", new Vector2(360f, 92f), Palette.Cyan);
            Rect(start.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, -150f), new Vector2(170f, -58f));
            var quit = AddButton(canvas.gameObject, "退出游戏", new Vector2(360f, 92f), Palette.TextDim);
            Rect(quit.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, -260f), new Vector2(170f, -168f));

            var footer = AddText(canvas.gameObject, "Footer", "Unity 2022.3 LTS · URP · AI-Assisted Development", 20f, Palette.TextDim);
            Rect(footer.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-420f, 24f), new Vector2(420f, 64f));

            var menuController = cameraGo.AddComponent<MainMenuController>();
            SetField(menuController, "_startButton", start);
            SetField(menuController, "_quitButton", quit);
            SetField(menuController, "_bestRecordLabel", best);
            SetField(menuController, "_titleTransform", (RectTransform)title.transform);

            EnsureDir("Assets/Scenes");
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, SceneMainMenu);
            Debug.Log($"[Scene] MainMenu 构建完成 → {SceneMainMenu}");
        }
    }
}
