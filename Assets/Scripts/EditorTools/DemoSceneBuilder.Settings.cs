using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 一键搭建器（Settings 部分）：项目设置层的自动化——
    /// URP 资产、层与碰撞矩阵、图形/质量/玩家设置、构建场景列表。
    /// 面试点：把"新建项目要手点 30 个设置"变成一条命令，是工程化思维的体现。
    /// </summary>
    public static partial class DemoSceneBuilder
    {
        // ---------- 层常量（与运行时代码的 LayerMask.NameToLayer 一致） ----------
        public const int LayerPlayer = 6;
        public const int LayerEnemy = 7;
        public const int LayerGround = 8;
        public const int LayerProjectile = 9;

        private static void SetupProjectSettings()
        {
            SetupLayersAndTags();
            SetupPhysicsMatrix();
            SetupUrpAssets();
            SetupGraphicsAndQuality();
            SetupPlayerSettings();
            SetupBuildScenes();
        }

        /// <summary>注册自定义层（写入 TagManager 资产）。横版闯关版：8=Ground 地形层。</summary>
        private static void SetupLayersAndTags()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");

            SetLayer(layers, LayerPlayer, "Player");
            SetLayer(layers, LayerEnemy, "Enemy");
            SetLayer(layers, LayerGround, "Ground");
            SetLayer(layers, LayerProjectile, "Projectile");
            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var prop = layers.GetArrayElementAtIndex(index);
            // 6-9 层归本项目所有：旧版遗留名（PlayerBullet/XPGem）直接覆写为横版名（Ground/Projectile），
            // 层号才是运行时代码（LayerMask.NameToLayer/数字掩码）真正依赖的东西
            if (prop.stringValue != name)
            {
                if (!string.IsNullOrEmpty(prop.stringValue))
                    Debug.LogWarning($"[Setup] 层 {index}: {prop.stringValue} → {name}（旧名覆写）");
                prop.stringValue = name;
            }
        }

        /// <summary>
        /// 碰撞矩阵（横版闯关版，最小碰撞面）：
        /// Player↔Ground（站立）、Enemy↔Ground（行走）、Player↔Enemy（接触伤害）开启；
        /// Projectile↔Enemy（命中判定）开启；弹丸穿地形（命中判定走层掩码不走矩阵）；
        /// Enemy↔Enemy 关闭（怪物互不推挤）。
        /// </summary>
        private static void SetupPhysicsMatrix()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets == null || assets.Length == 0) return;
            var dynamics = new SerializedObject(assets[0]);
            var matrix = dynamics.FindProperty("m_LayerCollisionMatrix");

            int n = 32;
            bool[,] on = new bool[n, n];
            // 对角线默认关（含 Default 层互撞关闭，场景里没有 Default 层的碰撞体）
            void Enable(int a, int b) { on[a, b] = true; on[b, a] = true; }
            Enable(LayerPlayer, LayerGround);
            Enable(LayerEnemy, LayerGround);
            Enable(LayerPlayer, LayerEnemy);
            Enable(LayerProjectile, LayerEnemy);

            // 位图编码：matrix[i] 的第 j 位表示 i 层与 j 层是否碰撞
            for (int i = 0; i < n; i++)
            {
                uint bits = 0;
                for (int j = 0; j < n; j++) if (on[i, j]) bits |= 1u << j;
                matrix.GetArrayElementAtIndex(i).intValue = unchecked((int)bits);
            }
            dynamics.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>创建 URP 全局设置：Renderer2DData + UniversalRenderPipelineAsset（2D 光照+HDR+Bloom 前置）。</summary>
        private static void SetupUrpAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath) != null) return;

            // 2D 渲染器：支持 Light2D 动态光照（深度/颜色贴图需求项在 URP14 里由管线自动管理）
            var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            rendererData.name = "Renderer2D_Neon";
            AssetDatabase.CreateAsset(rendererData, UrpRendererPath);

            var urp = UniversalRenderPipelineAsset.Create(rendererData);
            urp.name = "URP_Neon";
            // 霓虹观感三件套：HDR（发光超1喂Bloom）、软阴影无所谓关掉、MSAA 4x
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 0f; // 2D 项目不需要阴影贴图
            AssetDatabase.CreateAsset(urp, UrpAssetPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>图形/质量设置：指到 URP 资产、颜色空间 Linear（URP 必需）、抗锯齿。</summary>
        private static void SetupGraphicsAndQuality()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp != null) GraphicsSettings.defaultRenderPipeline = urp;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // 质量等级：MSAA 4x + 垂直同步（属性名随版本可能变化，找不到就跳过，不做硬性依赖）
            var qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityAssets == null || qualityAssets.Length == 0) return;
            var qualityAsset = new SerializedObject(qualityAssets[0]);
            var levels = qualityAsset.FindProperty("m_QualitySettings");
            if (levels == null) return;
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                TrySetInt(level, "antiAliasing", 4);
                TrySetInt(level, "vsyncCount", 1);
            }
            qualityAsset.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TrySetInt(SerializedProperty parent, string name, int value)
        {
            var prop = parent.FindPropertyRelative(name);
            if (prop != null) prop.intValue = value;
        }

        /// <summary>玩家设置：产品名、默认分辨率、窗口模式（demo 演示友好）。</summary>
        private static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "StarVortex";
            PlayerSettings.productName = "Hero of StarVortex";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // 输入后端切换为"两者兼容"：新版 Input System 生效（Keyboard.current 等），
            // 同时保留旧输入避免第三方包炸掉。这个设置在 ProjectSettings.asset 序列化字段里。
            var settings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var handler = settings.FindProperty("activeInputHandler");
            if (handler != null) handler.intValue = 2; // 0=旧 1=新 2=两者
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>构建场景列表：MainMenu 在前（启动场景）。</summary>
        private static void SetupBuildScenes()
        {
            // 横版闯关：主菜单 + 三关（LevelBuilder 建完场景后再跑一次会以文件存在为准）
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(SceneMainMenu, true),
                new EditorBuildSettingsScene("Assets/Scenes/Level1.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Level2.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Level3.unity", true),
            };
        }

        // ---------- 常量路径（partial 各文件共享） ----------
        public const string UrpAssetPath = "Assets/Settings/URP_Neon.asset";
        public const string UrpRendererPath = "Assets/Settings/Renderer2D_Neon.asset";
        public const string SceneMainMenu = "Assets/Scenes/MainMenu.unity";
        public const string ArtDir = "Assets/Art/Generated";
        public const string HifiArtDir = "Assets/Art/Hifi";
        public const string AudioDir = "Assets/Audio/Generated";
        public const string DataDir = "Assets/Data/Generated";
        public const string PrefabDir = "Assets/Prefabs/Generated";

        /// <summary>载入精灵：AI 成品（Hifi 目录，Python 管线产出）优先，程序化生成兜底。</summary>
        internal static Sprite Art(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{HifiArtDir}/{name}.png")
            ?? AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

        internal static AudioClip Sfx(string name) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{name}.wav");
    }
}
