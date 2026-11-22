using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 一键搭建总入口：美术 → 音效 → 字体 → 数据 → 预制体 → 场景。
    /// 面试点：整个项目从空仓库到可玩，只需要"打开 Unity 点一次菜单"——
    /// 工程全部资产由代码生成，Git 仓库干净且可复现。
    /// </summary>
    public static partial class DemoSceneBuilder
    {
        private const string MarkerFile = "Library/StarVortex_Built.marker";

        [MenuItem("StarVortex/① 一键搭建 Demo", priority = 0)]
        public static void GenerateAll()
        {
            Debug.Log("[Build] ========== 星涡幸存者 一键搭建开始 ==========");
            EditorUtility.DisplayProgressBar("星涡幸存者", "生成美术资源...", 0.05f);
            NeonArtGenerator.GenerateAll();

            EditorUtility.DisplayProgressBar("星涡幸存者", "合成音效...", 0.3f);
            SfxSynth.GenerateAll();

            EditorUtility.DisplayProgressBar("星涡幸存者", "烘焙中文字体...", 0.5f);
            EnsureCjkFont();

            EditorUtility.DisplayProgressBar("星涡幸存者", "创建数据资产与预制体...", 0.6f);
            AssetDatabase.Refresh();
            CreateDataAssets();
            CreatePrefabs();

            EditorUtility.DisplayProgressBar("星涡幸存者", "搭建场景与动画...", 0.8f);
            SetupProjectSettings();
            BuildScenes();
            BuildSpriteAtlas();

            EditorUtility.ClearProgressBar();
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, MarkerFile),
                System.DateTime.Now.ToString("F"));
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] ========== 搭建完成！按 Play 开始游戏 ==========");
        }

        /// <summary>
        /// 首次打开项目时自动引导（弹窗确认后执行搭建）。
        /// InitializeOnLoad 会在每次编译后触发，用 Marker 文件防止重复执行。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoSetupOnFirstOpen()
        {
            EditorApplication.delayCall += () =>
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string marker = Path.Combine(projectRoot, MarkerFile);
                string gameSceneFile = Path.Combine(projectRoot, SceneGame.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(marker) || File.Exists(gameSceneFile)) return;

                bool ok = EditorUtility.DisplayDialog(
                    "星涡幸存者 StarVortex",
                    "检测到项目尚未初始化。\n\n是否自动搭建全部资源？（美术/音效/场景/预制体，约 1-2 分钟）",
                    "开始搭建", "稍后手动执行（菜单 StarVortex/① 一键搭建 Demo）");
                if (ok) GenerateAll();
            };
        }

        /// <summary>
        /// 中文字体资产：从系统字体（微软雅黑）生成 TMP SDF 字体并烘焙游戏内全部汉字。
        /// 面试点：TMP 默认字体不含 CJK——中文游戏的字体管线是个真实生产问题；
        /// 编辑器期把用到的字符全部烘进静态图集，打包后不依赖目标机字体。
        /// </summary>
        private static void EnsureCjkFont()
        {
            // 已配置过则跳过
            if (TMP_Settings.defaultFontAsset != null &&
                TMP_Settings.defaultFontAsset.name.Contains("CJK"))
            {
                _cjkFont = TMP_Settings.defaultFontAsset;
                return;
            }

            EnsureDir("Assets/Fonts");
            string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei" };
            Font osFont = null;
            var installed = Font.GetOSInstalledFontNames();
            foreach (var name in candidates)
            {
                if (System.Array.IndexOf(installed, name) >= 0)
                {
                    osFont = Font.CreateDynamicFontFromOSFont(name, 90);
                    break;
                }
            }
            if (osFont == null)
            {
                Debug.LogWarning("[Font] 未找到系统中文字体，中文将显示为方块（英文正常）");
                return;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(osFont, 72, 6, TMPro.GlyphRenderMode.SDFAA, 1024, 1024, true);
            fontAsset.name = "TMP_CJK_Dynamic";
            AssetDatabase.CreateAsset(fontAsset, "Assets/Fonts/TMP_CJK.asset");

            // 烘焙项目内全部用到的字符（含标点）
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.TryAddCharacters(ChineseChars, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[Font] 以下字符不在系统字体中：{missing}");

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static; // 烘完定格，打包自包含
            EditorUtility.SetDirty(fontAsset);
            _cjkFont = fontAsset;

            // 设为 TMP 全局默认字体（此后所有 TMP 文本自动获得中文支持）
            var settings = TMP_Settings.instance;
            settings.defaultFontAsset = fontAsset;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[Font] CJK 字体烘焙完成：Assets/Fonts/TMP_CJK.asset");
        }

        /// <summary>精灵图集：Generated 目录全部精灵打进一个 Atlas（合批渲染的前提）。</summary>
        private static void BuildSpriteAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<UnityEditor.U2D.SpriteAtlas>("Assets/Art/Generated/Atlas_Neon.spriteatlas");
            if (atlas == null)
            {
                atlas = new UnityEditor.U2D.SpriteAtlas();
                AssetDatabase.CreateAtlas(atlas, "Atlas_Neon", "spriteatlas", "Assets/Art/Generated");
            }
            var packables = atlas.GetPackables();
            if (packables == null || packables.Length == 0)
            {
                atlas.Add(new Object[] { AssetDatabase.LoadAssetAtPath<Object>(ArtDir) });
                var settings = atlas.GetPackingSettings();
                settings.padding = 4;
                var plat = new UnityEditor.U2D.SpriteAtlasTextureSettings
                {
                    format = TextureImporterFormat.Automatic,
                    maxTextureSize = 2048,
                    compression = UnityEditor.U2D.SpriteAtlasCompression.None, // 编辑器期无压缩，画质优先
                };
                atlas.SetPlatformSettings(plat);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Atlas] 精灵图集 Atlas_Neon 完成");
        }

        /// <summary>反射注入 serialized 字段（搭建脚本里大量接线用，比手写 SerializedObject 简洁）。</summary>
        internal static void SetField(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetField(Object target, string fieldName, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
