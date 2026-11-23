using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
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

        /// <summary>逐级创建目录（AssetDatabase.CreateFolder 不支持多级递归；partial 各文件共享）。</summary>
        internal static void EnsureDir(string path)
        {
            var parts = path.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        /// <summary>
        /// 打包 Windows EXE（64 位 Mono）。
        /// 面试点：会打包是"完整交付"的基本功——场景列表来自 BuildSettings，
        /// 输出到 Builds/ 目录（已 gitignore）。
        /// </summary>
        [MenuItem("StarVortex/② 打包 Windows EXE", priority = 1)]
        public static void BuildWindowsExe()
        {
            const string outputDir = "Builds/Windows";
            string outputPath = outputDir + "/StarVortex.exe";
            Directory.CreateDirectory(outputDir);

            var sceneList = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (sceneList.Count == 0)
            {
                Debug.LogError("[Build] BuildSettings 为空，先运行一键搭建");
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(sceneList.ToArray(), s => s.path),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] 打包成功：{outputPath}  大小 {report.summary.totalSize / 1048576}MB  用时 {report.summary.totalTime.TotalSeconds:F0}s");
            }
            else
            {
                Debug.LogError($"[Build] 打包失败：{report.summary.result}");
            }
        }

        /// <summary>
        /// 批量校验 + 重建：先在编辑器里打开两个场景确认源头可加载（产物损坏时区分"源场景坏"还是"构建坏"），
        /// 再补烘字体缺失字符（TMP 下划线渲染取 '_' 字形），最后重建 EXE。
        /// 用法：Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///      -executeMethod StarVortex.EditorTools.DemoSceneBuilder.VerifyAndBuild
        /// </summary>
        [MenuItem("StarVortex/③ 校验场景并重建 EXE", priority = 2)]
        public static void VerifyAndBuild()
        {
            foreach (string path in new[] { SceneMainMenu, "Assets/Scenes/Level1.unity", "Assets/Scenes/Level2.unity", "Assets/Scenes/Level3.unity" })
            {
                if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                        path.Replace('/', Path.DirectorySeparatorChar))))
                {
                    Debug.LogWarning($"[Verify] 场景不存在（跳过）：{path}");
                    continue;
                }
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path,
                    UnityEditor.SceneManagement.OpenSceneMode.Single);
                Debug.Log($"[Verify] {path} 加载成功，根对象 {scene.rootCount} 个");
            }

            EnsureFontExtraChars();
            BuildWindowsExe();
        }

        /// <summary>TMP 的下划线/删除线用 '_' 字形渲染，静态图集缺它只会打警告不影响运行；补烘后重新定格。</summary>
        private static void EnsureFontExtraChars()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetPath);
            if (fontAsset == null) return;

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.TryAddCharacters("_—…★", out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[Font] 字体源不含以下字符：{missing}");

            // 追加的字符同样要嵌入资产本体，否则打包后引用悬空（与 EnsureCjkFont 同一个坑）
            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }
            if (fontAsset.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fontAsset.material)))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Font] 补烘下划线字符完成");
        }

        /// <summary>Hifi 目录的 AI 成品贴图统一导入为 Sprite（每像素单位 100，与程序化图一致）。</summary>
        private static void ImportHifiArt()
        {
            if (!AssetDatabase.IsValidFolder(HifiArtDir)) return;
            bool changed = false;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { HifiArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 100f)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100f;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = path.Contains("nebula") ? TextureWrapMode.Repeat : importer.wrapMode;
                    importer.SaveAndReimport();
                    changed = true;
                }
            }
            if (changed) Debug.Log("[Hifi] AI 成品贴图导入为 Sprite 完成");
        }

        [MenuItem("StarVortex/① 一键搭建 Demo", priority = 0)]
        public static void GenerateAll()
        {
            Debug.Log("[Build] ========== 勇者远征 一键搭建开始 ==========");
            EditorUtility.DisplayProgressBar("勇者远征", "生成美术资源...", 0.05f);
            NeonArtGenerator.GenerateAll();

            // Hifi 目录（AI 成品贴图）必须显式导入为 Sprite——Unity 默认导入类型是
            // Texture2D，LoadAssetAtPath<Sprite> 会全部拿 null，场景回退到程序化占位图
            AssetDatabase.Refresh();
            ImportHifiArt();

            // 横版占位美术：勇者/怪物/平台/陷阱/弹丸
            NeonArtGenerator.GenerateFantasy();

            // 环境美术套件：三关地形瓦片/装饰物/漩涡传送门/金属尖刺/雾带/暗角（同一导入管线，PPU 100）
            NeonArtGenerator.GenerateTileArt();

            EditorUtility.DisplayProgressBar("勇者远征", "合成音效...", 0.3f);
            SfxSynth.GenerateAll();

            EditorUtility.DisplayProgressBar("勇者远征", "烘焙中文字体...", 0.5f);
            EnsureCjkFont();

            EditorUtility.DisplayProgressBar("勇者远征", "创建勇者资产...", 0.6f);
            AssetDatabase.Refresh();
            CreateHeroAssets();

            EditorUtility.DisplayProgressBar("勇者远征", "搭建关卡场景...", 0.8f);
            SetupProjectSettings();
            InvokeLevelBuilder();

            EditorUtility.ClearProgressBar();
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, MarkerFile),
                System.DateTime.Now.ToString("F"));
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] ========== 搭建完成！打开 MainMenu 开始游戏 ==========");
        }

        /// <summary>
        /// 反射调用关卡构建器（关卡系统独立文件，反射解耦——它没落地时搭建流程照常走）。
        /// </summary>
        private static void InvokeLevelBuilder()
        {
            var type = System.Type.GetType("StarVortex.EditorTools.LevelBuilder, StarVortex.EditorTools");
            var method = type?.GetMethod("BuildAllLevels",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method != null)
                method.Invoke(null, null);
            else
                Debug.LogWarning("[Build] LevelBuilder.BuildAllLevels 尚未就绪，跳过关卡构建");
        }

        /// <summary>
        /// 首次打开项目时自动引导（弹窗确认后执行搭建）。
        /// InitializeOnLoad 会在每次编译后触发，用 Marker 文件防止重复执行。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoSetupOnFirstOpen()
        {
            // 批量模式（CI/自动化编译）不弹窗，显式用 -executeMethod 调 GenerateAll
            if (Application.isBatchMode) return;

            EditorApplication.delayCall += () =>
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string marker = Path.Combine(projectRoot, MarkerFile);
                string level1File = Path.Combine(projectRoot, "Assets/Scenes/Level1.unity".Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(marker) || File.Exists(level1File)) return;

                bool ok = EditorUtility.DisplayDialog(
                    "星涡幸存者 StarVortex",
                    "检测到项目尚未初始化。\n\n是否自动搭建全部资源？（美术/音效/场景/预制体，约 1-2 分钟）",
                    "开始搭建", "稍后手动执行（菜单 StarVortex/① 一键搭建 Demo）");
                if (ok) GenerateAll();
            };
        }

        /// <summary>
        /// 中文字体资产：从项目内字体文件生成 TMP SDF 字体并烘焙游戏内全部汉字。
        /// 面试点：① TMP 默认字体不含 CJK——中文游戏的字体管线是真实生产问题；
        /// ② 编辑器期把用到的字符全部烘进静态图集，打包后自包含；
        /// ③ 字体源文件不入库（.gitignore）——系统字体禁止随仓库再分发，构建期从系统目录
        ///   自动复制（Assets/Fonts 里有任何字体文件则优先用）。
        /// 注意：TMP Settings 资产未导入时 TMP_Settings.* 会 NRE，这里全程不碰它。
        /// </summary>
        private const string CjkFontAssetPath = "Assets/Fonts/TMP_CJK.asset";

        private static void EnsureCjkFont()
        {
            // 字表 = 手工字表 ∪ 脚本里的全部中文文案：改了文案不会漏字
            string charset = GameTextCharset.Collect(ChineseChars);

            // 资产已存在：只补烘新出现的字（原地追加，资产 GUID 不变，场景里的字体引用不会断）
            _cjkFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetPath);
            if (_cjkFont != null)
            {
                BakeMissingCharacters(_cjkFont, charset);
                return;
            }

            EnsureDir("Assets/Fonts");

            // 找字体源文件：项目内已有（任意 otf/ttf/ttc）→ 系统目录复制微软雅黑
            string fontProjectPath = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets/Fonts" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".otf" || ext == ".ttf" || ext == ".ttc") { fontProjectPath = p; break; }
            }
            if (fontProjectPath == null)
            {
                // 优先 simhei.ttf（单 face TTF，导入稳定）；msyh.ttc 多 face 集合在批量导入下会崩
                string sysFont = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "Fonts", "simhei.ttf");
                if (File.Exists(sysFont))
                {
                    string dest = "Assets/Fonts/simhei.ttf";
                    File.Copy(sysFont, Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                        dest.Replace('/', Path.DirectorySeparatorChar)), overwrite: true);
                    AssetDatabase.ImportAsset(dest);
                    fontProjectPath = dest;
                    Debug.Log("[Font] 已从系统复制黑体（字体文件不入库，许可更干净）");
                }
            }
            if (fontProjectPath == null)
            {
                Debug.LogWarning("[Font] 找不到任何中文字体源文件，中文将显示为方块");
                return;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(fontProjectPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[Font] 字体资产导入失败：{fontProjectPath}");
                return;
            }

            // TMP 的字体图集材质依赖 TextMeshPro/Sprite 着色器，它随 Essential 资源包分发：
            // 未导入 Essential 时 Shader.Find 返回 null，CreateFontAsset 内部直接 ArgumentNullException。
            // 这里程序化导入等价于"Window → TextMeshPro → Import TMP Essential Resources"。
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                string pkgRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "PackageCache");
                string essentials = Directory.GetFiles(pkgRoot, "TMP Essential Resources.unitypackage", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (essentials != null)
                {
                    AssetDatabase.ImportPackage(essentials, false); // 非交互模式：批量环境直接导入
                    AssetDatabase.Refresh();
                    Debug.Log("[Font] 已导入 TMP Essential Resources（含 Sprite 着色器与设置资产）");
                }
                else
                {
                    Debug.LogError("[Font] 找不到 TMP Essential Resources.unitypackage");
                    return;
                }
            }

            // 末参是 AtlasPopulationMode（不是 bool）：动态模式才能在编辑器期烘焙字符。
            // 字号 48 + 2048² 单页：732 字全量字符集单页装下——超过单页容量会触发
            // TMP 3.0.6 多页图集路径的 SetupNewAtlasTexture NRE（实测坑）
            var fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, 48, 5, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic);
            fontAsset.isMultiAtlasTexturesEnabled = true; // 兜底：即使溢出也允许新页
            if (fontAsset == null)
            {
                Debug.LogError($"[Font] CreateFontAsset 返回 null（字体面加载失败：{fontProjectPath}）");
                return;
            }
            fontAsset.name = "TMP_CJK_Dynamic";
            AssetDatabase.CreateAsset(fontAsset, CjkFontAssetPath);

            // 烘焙项目内全部用到的字符（含标点）
            fontAsset.TryAddCharacters(charset, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[Font] 以下字符不在字体中：{missing}");
            EmbedFontSubAssets(fontAsset);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static; // 烘完定格，打包自包含
            EditorUtility.SetDirty(fontAsset);
            _cjkFont = fontAsset;

            // 不依赖 TMP 全局设置（defaultFontAsset 只读、且 Settings 资产可能未导入）：
            // 构建器给每个 TMP 文本显式赋字体（AddText / DamagePop 预制体统一走 _cjkFont）
            AssetDatabase.SaveAssets();
            Debug.Log("[Font] CJK 字体烘焙完成：Assets/Fonts/TMP_CJK.asset");
        }

        /// <summary>
        /// 给已烘焙的静态字体补字：临时切回动态模式追加缺失字符，再定格为静态。
        /// 比删掉重建好在资产 GUID 不变，所有场景和预制体里的字体引用继续有效。
        /// </summary>
        private static void BakeMissingCharacters(TMP_FontAsset font, string charset)
        {
            if (font.HasCharacters(charset, out List<char> missingChars) || missingChars == null || missingChars.Count == 0)
                return;

            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.TryAddCharacters(new string(missingChars.ToArray()), out string notInFont);
            EmbedFontSubAssets(font);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            if (!string.IsNullOrEmpty(notInFont))
                Debug.LogWarning($"[Font] 以下字符不在字体中：{notInFont}");
        }

        /// <summary>
        /// 动态烘焙产生的图集纹理与材质都是内存对象，必须全部嵌入字体资产本体（子资产），
        /// 否则资产重载后引用变成悬空 guid（表现为中文全部显示方块）。
        /// </summary>
        private static void EmbedFontSubAssets(TMP_FontAsset font)
        {
            if (font.atlasTextures != null)
            {
                foreach (var tex in font.atlasTextures)
                {
                    if (tex == null) continue;
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex))) AssetDatabase.AddObjectToAsset(tex, font);
                    EditorUtility.SetDirty(tex); // 追加字形会改写已有图集页，同样要标脏才会落盘
                }
            }
            if (font.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(font.material)))
                AssetDatabase.AddObjectToAsset(font.material, font);
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

        /// <summary>写入 Object 引用数组字段（SO 资产池等）。</summary>
        internal static void SetObjectArrayField(Object target, string fieldName, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.ClearArray();
            for (int i = 0; i < values.Length; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>写入自定义结构数组字段（如视差层），逐子字段填值。</summary>
        internal static void SetStructArrayField(Object target, string fieldName,
            System.Action<SerializedProperty, int> fill, int count)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.ClearArray();
            for (int i = 0; i < count; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                fill(prop.GetArrayElementAtIndex(i), i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
