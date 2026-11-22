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
        public const int LayerPlayerBullet = 8;
        public const int LayerXpGem = 9;

        private static void SetupProjectSettings()
        {
            SetupLayersAndTags();
            SetupPhysicsMatrix();
            SetupUrpAssets();
            SetupGraphicsAndQuality();
            SetupPlayerSettings();
            SetupBuildScenes();
        }

        /// <summary>注册自定义层（写入 TagManager 资产）。</summary>
        private static void SetupLayersAndTags()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");

            SetLayer(layers, LayerPlayer, "Player");
            SetLayer(layers, LayerEnemy, "Enemy");
            SetLayer(layers, LayerPlayerBullet, "PlayerBullet");
            SetLayer(layers, LayerXpGem, "XPGem");
            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var prop = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(prop.stringValue) || prop.stringValue == name) prop.stringValue = name;
            else if (prop.stringValue != name)
                Debug.LogWarning($"[Setup] 层 {index} 已被占用：{prop.stringValue}，跳过 {name}");
        }

        /// <summary>
        /// 碰撞矩阵（最小碰撞面，帧率友好）：
        /// Player↔Enemy（接触伤害）、Player↔XPGem（拾取磁吸）、Enemy↔PlayerBullet（命中判定）开启；
        /// Enemy↔Enemy 关闭（敌人间距交给分离力算法，物理求解器零成本）。
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
            Enable(LayerPlayer, LayerEnemy);
            Enable(LayerPlayer, LayerXpGem);
            Enable(LayerEnemy, LayerPlayerBullet);

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

            // 2D 渲染器：支持 Light2D 动态光照
            var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            rendererData.name = "Renderer2D_Neon";
            rendererData.requiresDepthOption = Renderer2DData.DepthTextureOption.Force;
            rendererData.requiresColorOption = Renderer2DData.ColorTextureOption.Force;
            AssetDatabase.CreateAsset(rendererData, UrpRendererPath);

            var urp = UniversalRenderPipelineAsset.Create(new[] { rendererData });
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

            // 质量等级：关闭不需要的实时特性，保证低配机也稳 60
            var qualityAsset = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qualityAsset.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("antiAliasing").intValue = 4;
                level.FindPropertyRelative("vsyncCount").intValue = 1;
            }
            qualityAsset.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>玩家设置：产品名、默认分辨率、窗口模式（demo 演示友好）。</summary>
        private static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "StarVortex";
            PlayerSettings.productName = "StarVortex Survivors";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Landscape;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        /// <summary>构建场景列表：MainMenu 在前（启动场景）。</summary>
        private static void SetupBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(SceneMainMenu, true),
                new EditorBuildSettingsScene(SceneGame, true),
            };
        }

        // ---------- 常量路径（partial 各文件共享） ----------
        public const string UrpAssetPath = "Assets/Settings/URP_Neon.asset";
        public const string UrpRendererPath = "Assets/Settings/Renderer2D_Neon.asset";
        public const string SceneGame = "Assets/Scenes/GameScene.unity";
        public const string SceneMainMenu = "Assets/Scenes/MainMenu.unity";
        public const string ArtDir = "Assets/Art/Generated";
        public const string AudioDir = "Assets/Audio/Generated";
        public const string DataDir = "Assets/Data/Generated";
        public const string PrefabDir = "Assets/Prefabs/Generated";

        /// <summary>载入生成的精灵的快捷方法。</summary>
        internal static Sprite Art(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{name}.png");

        internal static AudioClip Sfx(string name) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{name}.wav");
    }
}
