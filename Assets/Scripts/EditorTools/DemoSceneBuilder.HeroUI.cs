using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;
using StarVortex.Gameplay;
using StarVortex.UI;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 《勇者远征》UI 搭建（partial DemoSceneBuilder）：
    /// 勇者 HUD（血/蓝/经验/天赋角标/BOSS 血条/操作提示/关卡目标）+ 选人面板 + 升级双栏面板 + 关卡结算面板。
    /// 武器 Kit SO 由 DemoSceneBuilder.Hero.cs 的 CreateWeaponKits 负责（本文件只消费）；
    /// 场景接入方式（两选一）：
    /// ① 关卡场景搭建脚本调用 BuildHeroHud / BuildWeaponSelectPanel / BuildHeroLevelUpPanel / BuildLevelClearPanel
    ///    四个方法后自行接线 HeroGameBootstrap；
    /// ② 打开目标场景直接跑菜单 StarVortex/HeroUI/搭建勇者界面（本文件）：自动建 Canvas 与引导器并全部接线。
    /// </summary>
    public static partial class DemoSceneBuilder
    {
        // ============================ HUD（勇者版） ============================

        /// <summary>勇者 HUD：旧版四件套 + 蓝条 + 天赋角标 + BOSS 血条 + 操作提示 + 关卡目标。</summary>
        private static GameObject BuildHeroHud(Transform canvasRoot)
        {
            var hud = new GameObject("HeroHUD", typeof(RectTransform), typeof(CanvasGroup));
            hud.transform.SetParent(canvasRoot, false);
            Rect(hud, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 左上：血条（残影条 = 延迟跟随的白条，掉血"先掉血再流血"）
            var hpGroup = new GameObject("HpGroup", typeof(RectTransform));
            hpGroup.transform.SetParent(hud.transform, false);
            Rect(hpGroup, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -86f), new Vector2(480f, -30f));

            var hpBg = AddImage(hpGroup, "HpBg", new Color(0.1f, 0.13f, 0.22f, 0.85f));
            Rect(hpBg.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ghost = AddImage(hpBg.gameObject, "HpGhost", new Color(1f, 1f, 1f, 0.45f));
            Rect(ghost.gameObject, Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var hpFill = AddImage(hpBg.gameObject, "HpFill", Palette.Red);
            Rect(hpFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var hpLabel = AddText(hpGroup, "HpLabel", "100 / 100", 22f, Palette.TextPrimary, TextAlignmentOptions.Left);
            Rect(hpLabel.gameObject, new Vector2(0f, 0.9f), new Vector2(1f, 1.55f), new Vector2(2f, 0f), Vector2.zero);

            // 血条下方：蓝条（细条，回蓝/耗蓝流动感由 HUDController 缓动）
            var manaBg = AddImage(hud, "ManaBg", new Color(0.1f, 0.13f, 0.22f, 0.85f));
            Rect(manaBg.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -98f), new Vector2(480f, -88f));
            var manaFill = AddImage(manaBg.gameObject, "ManaFill", Palette.Cyan);
            Rect(manaFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(-2f, -2f));

            // 蓝条下方：天赋点角标（pendingTalentPoints>0 时呼吸提醒）
            var badge = new GameObject("TalentBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(hud.transform, false);
            Rect(badge, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -134f), new Vector2(300f, -104f));
            badge.GetComponent<Image>().color = new Color(0.16f, 0.12f, 0.04f, 0.9f);
            var badgeLabel = AddText(badge, "BadgeLabel", "可分配 0 点", 22f, Palette.TextHdrGold, TextAlignmentOptions.Left);
            Rect(badgeLabel.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), new Vector2(-8f, 0f));
            badge.SetActive(false); // 初始无点数，HeroGameBootstrap.Start 推真实值后由 HUD 决定显隐

            // 顶中：计时器
            var timer = AddText(hud, "Timer", "00:00", 46f, Palette.TextPrimary);
            Rect(timer.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-160f, -110f), new Vector2(160f, -30f));

            // 计时器下方：BOSS 血条（BossHpChanged(0,0) 隐藏；(max,max) 弹入）
            var bossRoot = new GameObject("BossBar", typeof(RectTransform), typeof(Image));
            bossRoot.transform.SetParent(hud.transform, false);
            Rect(bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-520f, -138f), new Vector2(520f, -106f));
            bossRoot.GetComponent<Image>().color = new Color(0.1f, 0.04f, 0.08f, 0.9f);
            var bossFill = AddImage(bossRoot, "BossFill", Palette.Red);
            Rect(bossFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));
            var bossLabel = AddText(bossRoot, "BossLabel", "BOSS", 20f, Palette.TextPrimary);
            Rect(bossLabel.gameObject, new Vector2(0f, -0.9f), new Vector2(1f, 0.9f), Vector2.zero, Vector2.zero);
            bossRoot.SetActive(false);

            // BOSS 血条下方：关卡目标（第 N 关 · 战斗中/传送门开启/…）
            var objective = AddText(hud, "Objective", "第 1 关 · 战斗开始", 26f, Palette.TextDim);
            Rect(objective.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-360f, -178f), new Vector2(360f, -142f));

            // 右上：击杀 / 等级
            var kills = AddText(hud, "Kills", "击杀 0", 26f, Palette.TextPrimary, TextAlignmentOptions.Right);
            Rect(kills.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-360f, -86f), new Vector2(-28f, -34f));
            var level = AddText(hud, "Level", "Lv.1", 26f, Palette.CyanHdr, TextAlignmentOptions.Right);
            Rect(level.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-360f, -134f), new Vector2(-28f, -82f));

            // 底部：经验条（全宽薄片）
            var xp = AddImage(hud, "XpBg", new Color(0.1f, 0.13f, 0.22f, 0.9f));
            Rect(xp.gameObject, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            var xpHolder = AddImage(xp.gameObject, "XpHolder", new Color(0f, 0f, 0f, 0f));
            Rect(xpHolder.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var xpFill = AddImage(xpHolder.gameObject, "XpFill", Palette.Acid);
            Rect(xpFill.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));

            // 经验条上方：操作提示条（开局显示，8s 后淡出；root 初始隐藏，首次 Playing 由 HUD 激活）
            var hintRoot = new GameObject("HintBar", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            hintRoot.transform.SetParent(hud.transform, false);
            Rect(hintRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-430f, 64f), new Vector2(430f, 118f));
            hintRoot.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.8f);
            var hintText = AddText(hintRoot, "HintText", "WASD 移动 · 空格跳(×2) · J 攻击 · K 特殊 · L 冲刺", 26f, Palette.TextPrimary);
            Rect(hintText.gameObject, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            hintRoot.SetActive(false);

            // 注入 HUDController（新字段全量接线）
            var hudObj = hud.AddComponent<HUDController>();
            SetField(hudObj, "_hpFill", hpFill);
            SetField(hudObj, "_hpGhostFill", ghost);
            SetField(hudObj, "_hpLabel", hpLabel);
            SetField(hudObj, "_manaFill", manaFill);
            SetField(hudObj, "_talentBadgeRoot", badge);
            SetField(hudObj, "_talentBadgeLabel", badgeLabel);
            SetField(hudObj, "_bossRoot", bossRoot);
            SetField(hudObj, "_bossFill", bossFill);
            SetField(hudObj, "_xpFill", xpFill);
            SetField(hudObj, "_timerLabel", timer);
            SetField(hudObj, "_killsLabel", kills);
            SetField(hudObj, "_levelLabel", level);
            SetField(hudObj, "_hintGroup", hintRoot.GetComponent<CanvasGroup>());
            SetField(hudObj, "_hintRoot", hintRoot);
            SetField(hudObj, "_objectiveLabel", objective);
            return hud;
        }

        // ============================ 选人面板 ============================

        /// <summary>选人面板：三张武器卡（名称+形态标签+描述+专属爽点池列表），点卡注入 WeaponKit 开局。</summary>
        private static GameObject BuildWeaponSelectPanel(Transform canvasRoot)
        {
            var panel = new GameObject("WeaponSelectPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.88f);
            panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

            var title = AddText(panel, "Title", "选择你的武器", 64f, Palette.TextHdrGold);
            Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-400f, -190f), new Vector2(400f, -90f));
            var subtitle = AddText(panel, "Subtitle", "武器决定主攻击形态与专属爽点池（三选一只从所选武器池 + 通用池刷出）", 26f, Palette.TextDim);
            Rect(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-620f, -232f), new Vector2(620f, -196f));

            // 三张卡横排
            var cards = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cards.transform.SetParent(panel.transform, false);
            Rect(cards, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-760f, -250f), new Vector2(760f, 330f));
            var layout = cards.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var cardRoots = new GameObject[3];
            var cardButtons = new Button[3];
            var nameLabels = new TMP_Text[3];
            var descLabels = new TMP_Text[3];
            var statLabels = new TMP_Text[3];
            var poolLabels = new TMP_Text[3];
            var hotkeyLabels = new TMP_Text[3];

            for (int i = 0; i < 3; i++)
            {
                var card = new GameObject($"WeaponCard{i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
                card.transform.SetParent(cards.transform, false);
                card.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.97f);
                var btn = card.GetComponent<Button>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.3f);
                colors.pressedColor = new Color(0.75f, 0.75f, 0.8f);
                btn.colors = colors;

                // 边框：底层放大 4px 的描边
                var border = AddImage(card, "Border", Palette.Cyan);
                Rect(border.gameObject, Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
                border.transform.SetAsFirstSibling();

                var name = AddText(card, "Name", "武器名", 44f, Palette.TextPrimary);
                Rect(name.gameObject, new Vector2(0f, 0.78f), new Vector2(1f, 0.94f), Vector2.zero, Vector2.zero);
                var stat = AddText(card, "Stat", "近战连击 · 攻速 0.35s", 24f, Palette.CyanHdr);
                Rect(stat.gameObject, new Vector2(0f, 0.66f), new Vector2(1f, 0.76f), Vector2.zero, Vector2.zero);
                var desc = AddText(card, "Desc", "描述", 24f, Palette.TextDim, TextAlignmentOptions.Top);
                Rect(desc.gameObject, new Vector2(0.06f, 0.34f), new Vector2(0.94f, 0.64f), Vector2.zero, Vector2.zero);
                var divider = AddImage(card, "Divider", new Color(1f, 1f, 1f, 0.12f));
                Rect(divider.gameObject, new Vector2(0f, 0.315f), new Vector2(1f, 0.325f), Vector2.zero, Vector2.zero);
                var poolCaption = AddText(card, "PoolCaption", "专属爽点池", 22f, Palette.TextHdrGold, TextAlignmentOptions.Left);
                Rect(poolCaption.gameObject, new Vector2(0.06f, 0.255f), new Vector2(0.94f, 0.31f), Vector2.zero, Vector2.zero);
                var pool = AddText(card, "Pool", "· 爽点", 22f, Palette.TextDim, TextAlignmentOptions.TopLeft);
                Rect(pool.gameObject, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.25f), Vector2.zero, Vector2.zero);

                // 快捷键角标（右上角）
                var hotkeyBg = AddImage(card, "HotkeyBg", new Color(1f, 1f, 1f, 0.08f));
                Rect(hotkeyBg.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -60f), Vector2.zero);
                var hotkey = AddText(hotkeyBg.gameObject, "Hotkey", (i + 1).ToString(), 26f, Palette.TextDim);
                Rect(hotkey.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                cardRoots[i] = card;
                cardButtons[i] = btn;
                nameLabels[i] = name;
                descLabels[i] = desc;
                statLabels[i] = stat;
                poolLabels[i] = pool;
                hotkeyLabels[i] = hotkey;
            }

            var script = panel.AddComponent<WeaponSelectPanel>();
            SetField(script, "_group", panel.GetComponent<CanvasGroup>());
            SetObjectArrayField(script, "_cardRoots", cardRoots);
            SetObjectArrayField(script, "_cardButtons", cardButtons);
            SetObjectArrayField(script, "_nameLabels", nameLabels);
            SetObjectArrayField(script, "_descLabels", descLabels);
            SetObjectArrayField(script, "_statLabels", statLabels);
            SetObjectArrayField(script, "_poolLabels", poolLabels);
            SetObjectArrayField(script, "_hotkeyLabels", hotkeyLabels);
            SetObjectArrayField(script, "_kits", LoadHeroWeaponKits());
            panel.SetActive(false); // 运行时由 HeroGameBootstrap 按 WeaponSelect 状态激活
            return panel;
        }

        /// <summary>载入三把武器 Kit 资产（DemoSceneBuilder.Hero.CreateWeaponKits 的产物）。</summary>
        private static WeaponKit[] LoadHeroWeaponKits()
        {
            var kits = new[]
            {
                AssetDatabase.LoadAssetAtPath<WeaponKit>($"{HeroDataDir}/Weapon_Sword.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponKit>($"{HeroDataDir}/Weapon_Staff.asset"),
                AssetDatabase.LoadAssetAtPath<WeaponKit>($"{HeroDataDir}/Weapon_Bow.asset"),
            };
            for (int i = 0; i < kits.Length; i++)
                if (kits[i] == null)
                    Debug.LogError($"[HeroUI] 缺少武器 Kit 资产（先运行 StarVortex/Hero/构建勇者资产）：{HeroDataDir}/Weapon_*.asset 第 {i + 1} 个为空");
            return kits;
        }

        // ============================ 升级双栏面板 ============================

        /// <summary>升级双栏面板：左栏天赋五行（+按钮），右栏爽点三选一，底部"继续战斗"。</summary>
        private static GameObject BuildHeroLevelUpPanel(Transform canvasRoot)
        {
            var panel = new GameObject("HeroLevelUpPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.88f);
            panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

            var header = AddText(panel, "Header", "升级！Lv.1 — 分配天赋并选择爽点", 54f, Palette.TextHdrGold);
            Rect(header.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-560f, -110f), new Vector2(560f, -40f));
            var warning = AddText(panel, "Warning", "请先选择一张爽点卡！", 30f, Palette.Red);
            Rect(warning.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-300f, -158f), new Vector2(300f, -116f));
            warning.gameObject.SetActive(false);

            // ---------- 左栏：天赋点 ----------
            var left = new GameObject("TalentColumn", typeof(RectTransform));
            left.transform.SetParent(panel.transform, false);
            Rect(left, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-880f, -320f), new Vector2(-60f, 250f));

            var talentTitle = AddText(left, "TalentTitle", "天赋点", 34f, Palette.TextPrimary, TextAlignmentOptions.Left);
            Rect(talentTitle.gameObject, new Vector2(0f, 0.86f), new Vector2(0.4f, 1f), Vector2.zero, Vector2.zero);
            var pointsLabel = AddText(left, "PointsLabel", "可分配 0 点", 28f, Palette.TextHdrGold, TextAlignmentOptions.Right);
            Rect(pointsLabel.gameObject, new Vector2(0.4f, 0.86f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            string[] talentNames =
            {
                "生命强化 +20/点", "攻击强化 +10%/点", "攻速强化 +8%/点", "移速强化 +5%/点", "蓝量强化 +20/点",
            };
            var invested = new TMP_Text[5];
            var values = new TMP_Text[5];
            var plusButtons = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                float y = 8f + i * 94f; // 列高 570：五行每行 84 高 + 10 间距
                var row = new GameObject($"TalentRow{i}", typeof(RectTransform), typeof(Image));
                row.transform.SetParent(left.transform, false);
                Rect(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, y), new Vector2(-8f, y + 84f));
                row.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.92f);

                var nameLabel = AddText(row, "Name", talentNames[i], 26f, Palette.TextPrimary, TextAlignmentOptions.Left);
                Rect(nameLabel.gameObject, new Vector2(0.02f, 0f), new Vector2(0.4f, 1f), new Vector2(10f, 0f), Vector2.zero);
                var inv = AddText(row, "Invested", "未投入", 22f, Palette.TextDim);
                Rect(inv.gameObject, new Vector2(0.4f, 0f), new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero);
                var val = AddText(row, "Value", "100", 26f, Palette.CyanHdr, TextAlignmentOptions.Right);
                Rect(val.gameObject, new Vector2(0.62f, 0f), new Vector2(0.86f, 1f), Vector2.zero, Vector2.zero);

                var plus = AddButton(row, "+", new Vector2(56f, 56f), Palette.Acid);
                Rect(plus.gameObject, new Vector2(0.88f, 0.12f), new Vector2(0.98f, 0.88f), Vector2.zero, Vector2.zero);
                invested[i] = inv;
                values[i] = val;
                plusButtons[i] = plus;
            }

            // ---------- 右栏：爽点三选一 ----------
            var right = new GameObject("BoonColumn", typeof(RectTransform));
            right.transform.SetParent(panel.transform, false);
            Rect(right, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60f, -320f), new Vector2(880f, 250f));

            var boonTitle = AddText(right, "BoonTitle", "爽点 · 三选一", 34f, Palette.TextPrimary, TextAlignmentOptions.Left);
            Rect(boonTitle.gameObject, new Vector2(0f, 0.86f), new Vector2(0.6f, 1f), Vector2.zero, Vector2.zero);
            var boonNote = AddText(right, "BoonNote", "选 1 张 · 必选", 22f, Palette.TextDim, TextAlignmentOptions.Right);
            Rect(boonNote.gameObject, new Vector2(0.6f, 0.86f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var boonRoots = new GameObject[3];
            var boonNames = new TMP_Text[3];
            var boonDescs = new TMP_Text[3];
            var boonBorders = new Image[3];
            var boonHotkeys = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float y = 8f + i * 156f; // 列高 570：三卡每卡 144 高 + 12 间距
                var card = new GameObject($"BoonCard{i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
                card.transform.SetParent(right.transform, false);
                Rect(card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, y), new Vector2(-8f, y + 144f));
                card.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.97f);
                var btnColors = card.GetComponent<Button>().colors;
                btnColors.highlightedColor = new Color(1.25f, 1.25f, 1.3f);
                card.GetComponent<Button>().colors = btnColors;

                var border = AddImage(card, "Border", Palette.Cyan);
                Rect(border.gameObject, Vector2.zero, Vector2.one, new Vector2(-3f, -3f), new Vector2(3f, 3f));
                border.transform.SetAsFirstSibling();

                var name = AddText(card, "Name", "爽点名称", 30f, Palette.TextPrimary, TextAlignmentOptions.Left);
                Rect(name.gameObject, new Vector2(0.03f, 0.52f), new Vector2(0.78f, 0.9f), new Vector2(10f, 0f), Vector2.zero);
                var desc = AddText(card, "Desc", "效果描述", 24f, Palette.TextDim, TextAlignmentOptions.TopLeft);
                Rect(desc.gameObject, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.5f), new Vector2(10f, 0f), Vector2.zero);

                var hotkeyBg = AddImage(card, "HotkeyBg", new Color(1f, 1f, 1f, 0.08f));
                Rect(hotkeyBg.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -56f), Vector2.zero);
                var hotkey = AddText(hotkeyBg.gameObject, "Hotkey", (i + 1).ToString(), 26f, Palette.TextDim);
                Rect(hotkey.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                boonRoots[i] = card;
                boonNames[i] = name;
                boonDescs[i] = desc;
                boonBorders[i] = border.GetComponent<Image>();
                boonHotkeys[i] = hotkey;
            }

            // ---------- 底部：继续战斗 ----------
            var cont = AddButton(panel, "继续战斗 Enter", new Vector2(340f, 80f), Palette.Cyan);
            Rect(cont.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 120f), new Vector2(170f, 200f));

            var script = panel.AddComponent<LevelUpPanel>();
            SetField(script, "_group", panel.GetComponent<CanvasGroup>());
            SetField(script, "_headerLabel", header);
            SetField(script, "_warningLabel", warning);
            SetField(script, "_continueButton", cont);
            SetField(script, "_pointsLabel", pointsLabel);
            SetObjectArrayField(script, "_talentInvested", invested);
            SetObjectArrayField(script, "_talentValue", values);
            SetObjectArrayField(script, "_talentButtons", plusButtons);
            SetObjectArrayField(script, "_boonRoots", boonRoots);
            SetObjectArrayField(script, "_boonNameLabels", boonNames);
            SetObjectArrayField(script, "_boonDescLabels", boonDescs);
            SetObjectArrayField(script, "_boonBorders", boonBorders);
            SetObjectArrayField(script, "_boonHotkeyLabels", boonHotkeys);
            panel.SetActive(false); // 运行时由 HeroGameBootstrap 按 LevelUp 状态激活
            return panel;
        }

        // ============================ 关卡结算面板 ============================

        /// <summary>关卡结算面板：过关版式（本关击杀 + 下一关）与全通关版式（总击杀/用时/等级 + 回主菜单）共用。</summary>
        private static GameObject BuildLevelClearPanel(Transform canvasRoot)
        {
            var panel = new GameObject("LevelClearPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasRoot, false);
            Rect(panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.92f);
            panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

            var title = AddText(panel, "Title", "第 1 关 通过！", 60f, Palette.GhostGreen);
            Rect(title.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-450f, 170f), new Vector2(450f, 260f));

            string[] rowNames = { "本关击杀", "总击杀", "用时", "等级" };
            var rows = new GameObject[4];
            var values = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                float top = 90f - i * 78f; // 居中向下排四行
                var row = new GameObject($"StatRow{i}", typeof(RectTransform));
                row.transform.SetParent(panel.transform, false);
                Rect(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-360f, top - 56f), new Vector2(360f, top));

                var name = AddText(row, "Name", rowNames[i], 30f, Palette.TextDim, TextAlignmentOptions.Right);
                Rect(name.gameObject, new Vector2(0f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero);
                var value = AddText(row, "Value", i == 2 ? "00:00" : i == 3 ? "Lv.1" : "0", 34f, Palette.TextPrimary, TextAlignmentOptions.Left);
                Rect(value.gameObject, new Vector2(0.52f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

                rows[i] = row;
                values[i] = value;
            }

            var cont = AddButton(panel, "下一关", new Vector2(300f, 76f), Palette.Cyan);
            Rect(cont.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-340f, -170f), new Vector2(-40f, -94f));
            var menu = AddButton(panel, "回主菜单", new Vector2(300f, 76f), Palette.TextDim);
            Rect(menu.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40f, -170f), new Vector2(340f, -94f));
            menu.gameObject.SetActive(false); // 过关版式默认只有"下一关"，全通关时面板自己切换

            var script = panel.AddComponent<LevelClearPanel>();
            SetField(script, "_group", panel.GetComponent<CanvasGroup>());
            SetField(script, "_titleLabel", title);
            SetObjectArrayField(script, "_rows", rows);
            SetObjectArrayField(script, "_valueLabels", values);
            SetField(script, "_continueButton", cont);
            SetField(script, "_menuButton", menu);
            panel.SetActive(false); // 运行时由 HeroGameBootstrap 按 LevelClear 状态激活
            return panel;
        }

        // ============================ 场景接线 ============================

        /// <summary>
        /// 把勇者全家 UI 接进当前场景：建 Canvas（缺则补）、四套面板、HeroGameBootstrap 引导器并全量接线。
        /// 勇者/弹丸预制体引用从 DemoSceneBuilder.Hero 的产物读取（Hero.prefab / proj_*.prefab）。
        /// </summary>
        [MenuItem("StarVortex/HeroUI/搭建勇者界面（活动场景）", priority = 20)]
        public static void BuildHeroUIIntoActiveScene()
        {
            // Canvas：关卡场景自带就复用，没有就建（先 HUD 后面板，层级天然盖住 HUD）
            var canvasGo = GameObject.Find("Canvas_HUD");
            if (canvasGo == null)
            {
                var canvas = CreateCanvas("Canvas_HUD", 0);
                canvasGo = canvas.gameObject;
                CreateEventSystem();
            }

            var hud = BuildHeroHud(canvasGo.transform);
            var weaponPanel = BuildWeaponSelectPanel(canvasGo.transform);
            var levelUpPanel = BuildHeroLevelUpPanel(canvasGo.transform);
            var levelClearPanel = BuildLevelClearPanel(canvasGo.transform);

            // 引导器：已存在就复用（重复执行菜单不叠加组件）
            var bootstrapGo = GameObject.Find("HeroGameBootstrap");
            if (bootstrapGo == null) bootstrapGo = new GameObject("HeroGameBootstrap");
            var bootstrap = bootstrapGo.GetComponent<HeroGameBootstrap>() ?? bootstrapGo.AddComponent<HeroGameBootstrap>();
            SetField(bootstrap, "_hero", FindSceneHero());
            SetField(bootstrap, "_fireballPrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_fireball.prefab"));
            SetField(bootstrap, "_swordWavePrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_swordwave.prefab"));
            SetField(bootstrap, "_arrowPrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_arrow.prefab"));
            SetField(bootstrap, "_weaponSelectPanel", weaponPanel);
            SetField(bootstrap, "_levelUpPanel", levelUpPanel);
            SetField(bootstrap, "_levelClearPanel", levelClearPanel);
            SetField(bootstrap, "_gameOverPanel", GameObject.Find("GameOverPanel")); // 旧场景已有则接上，没有留空
            SetField(bootstrap, "_pausePanel", GameObject.Find("PausePanel"));

            if (FindSceneHero() == null)
                Debug.LogWarning("[HeroUI] 场景里没有 HeroController——引导器 _hero 留空，运行时会自动查找（找不到则 HUD 初始值跳过）");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[HeroUI] 勇者界面搭建完成（HUD/选人/升级/结算 + HeroGameBootstrap）——记得 Ctrl+S 保存场景");
        }

        /// <summary>场景里的勇者（编辑器期查找，含未激活对象）。</summary>
        private static HeroController FindSceneHero()
        {
            foreach (var hero in Object.FindObjectsByType<HeroController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                return hero;
            return null;
        }

        /// <summary>供关卡场景搭建脚本整体调用：一次建全四套 UI 并返回引导器（接线面板字段已在本方法内完成）。</summary>
        public static HeroGameBootstrap BuildHeroUiSet(Transform canvasRoot)
        {
            BuildHeroHud(canvasRoot);
            var weaponPanel = BuildWeaponSelectPanel(canvasRoot);
            var levelUpPanel = BuildHeroLevelUpPanel(canvasRoot);
            var levelClearPanel = BuildLevelClearPanel(canvasRoot);

            var bootstrapGo = new GameObject("HeroGameBootstrap");
            var bootstrap = bootstrapGo.AddComponent<HeroGameBootstrap>();
            SetField(bootstrap, "_hero", FindSceneHero());
            SetField(bootstrap, "_fireballPrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_fireball.prefab"));
            SetField(bootstrap, "_swordWavePrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_swordwave.prefab"));
            SetField(bootstrap, "_arrowPrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/proj_arrow.prefab"));
            SetField(bootstrap, "_weaponSelectPanel", weaponPanel);
            SetField(bootstrap, "_levelUpPanel", levelUpPanel);
            SetField(bootstrap, "_levelClearPanel", levelClearPanel);
            return bootstrap;
        }

        /// <summary>
        /// 主菜单场景构建：标题 + 开始远征/退出 + 最佳纪录。
        /// 开始远征 → GameManager.StartNewRun（加载 Level1，进选人界面）。
        /// </summary>
        [MenuItem("StarVortex/HeroUI/搭建主菜单场景", priority = 12)]
        public static void BuildHeroMenuScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.06f, 1f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.AddComponent<CameraRig>();

            var lightGo = new GameObject("GlobalLight");
            var global = lightGo.AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 1f;
            global.color = new Color(0.86f, 0.9f, 1f);
            CreateBloomVolume();

            // 背景层（星云/星空——Hifi 奇幻背景到位后按同名替换）
            var bgGo = new GameObject("MenuBackdrop");
            var bgSr = bgGo.AddComponent<SpriteRenderer>();
            var bgSprite = Art("menu_bg") ?? Art("nebula_tile") ?? Art("star_bg_0");
            bgSr.sprite = bgSprite;
            bgSr.sortingOrder = -50;
            if (bgSprite != null)
            {
                float viewH = cam.orthographicSize * 2f;
                float viewW = viewH * cam.aspect;
                float s = Mathf.Max(viewW / bgSprite.bounds.size.x, viewH / bgSprite.bounds.size.y) * 1.02f;
                bgGo.transform.localScale = Vector3.one * s;
            }

            // 管理器（音频常驻；菜单不建 Juice，避免空粒子单例挤掉游戏内的绑定版）
            var managers = new GameObject("Managers");
            managers.AddComponent<GameManager>();
            managers.AddComponent<PoolService>();
            var audio = managers.AddComponent<AudioService>();
            BindAudio(audio);

            // ---------- 菜单 UI ----------
            var canvas = CreateCanvas("Canvas_Menu", 0);
            CreateEventSystem();

            var title = AddText(canvas.gameObject, "Title", "勇者远征", 128f, Palette.TextPrimary);
            Rect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-560f, -320f), new Vector2(560f, -120f));
            title.color = Palette.CyanHdr;

            var subtitle = AddText(canvas.gameObject, "Subtitle", "HERO OF STARVORTEX", 30f, Palette.TextDim);
            Rect(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-400f, -380f), new Vector2(400f, -335f));

            var best = AddText(canvas.gameObject, "BestRecord", "暂无纪录 —— 来创造第一个", 26f, Palette.TextDim);
            Rect(best.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460f, -60f), new Vector2(460f, 20f));

            var start = AddButton(canvas.gameObject, "开始远征", new Vector2(360f, 92f), Palette.Cyan);
            Rect(start.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, -150f), new Vector2(170f, -58f));
            var quit = AddButton(canvas.gameObject, "退出游戏", new Vector2(360f, 92f), Palette.TextDim);
            Rect(quit.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, -260f), new Vector2(170f, -168f));

            var footer = AddText(canvas.gameObject, "Footer", "Unity 2022.3 LTS · URP · AI-Assisted Development", 20f, Palette.TextDim);
            Rect(footer.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-420f, 24f), new Vector2(420f, 64f));

            var menuController = cameraGo.AddComponent<HeroMenuController>();
            SetField(menuController, "_startButton", start);
            SetField(menuController, "_quitButton", quit);
            SetField(menuController, "_bestRecordLabel", best);

            EnsureDir("Assets/Scenes");
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, SceneMainMenu);
            Debug.Log($"[Scene] 主菜单构建完成 → {SceneMainMenu}");
        }
    }
}
