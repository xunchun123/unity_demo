using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;
using StarVortex.Enemies;
using StarVortex.Gameplay;
using StarVortex.Gameplay.World;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 《勇者远征》关卡构建器（独立静态类，与 DemoSceneBuilder 系列零耦合，可单独分阶段跑）。
    /// 面试点：关卡 = 数据（LevelSpec）+ 几何（编辑器期按 spec 铺 BoxCollider2D/SpriteRenderer）
    ///       + 状态机（LevelDirector 运行期按同一份 spec 布置怪物/陷阱）——
    ///   一份 spec 既是地形蓝图也是玩法清单，地形与怪物/陷阱坐标永远对齐；
    /// 缺资产降级策略：怪物 prefab / 勇者 prefab / 美术精灵任一缺失 → LogWarning + 跳过继续，
    ///   保证美术与 prefab 流程没跑完时也能先出可玩的关卡骨架（分阶段联调不互相阻塞）。
    /// 用法：菜单 StarVortex/Levels/构建全部关卡（可重复执行，每次全量重建三关场景）。
    /// </summary>
    public static class LevelBuilder
    {
        private const string SceneDir = "Assets/Scenes";
        private const string HeroPrefabPath = "Assets/Prefabs/Hero/Hero.prefab";
        private const string MonsterPrefabDir = "Assets/Prefabs/Hero";
        private const string AudioDir = "Assets/Audio/Generated";

        // 层数字契约：与 DemoSceneBuilder.Settings.cs（Player=6/Enemy=7/Ground=8）及
        // HeroController.groundMask（1<<8）一致。用数字不用 LayerMask.NameToLayer——
        // TagManager 里层名可能被旧项目设置占用（当前 8 层叫 PlayerBullet），数字才是运行时唯一事实
        private const int LayerPlayer = 6;
        private const int LayerGround = 8;


        // ============================================================ 入口 ============================================================

        [MenuItem("StarVortex/Levels/构建全部关卡")]
        public static void BuildAllLevels()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[LevelBuilder] Play 模式下不能构建场景，请先退出 Play");
                return;
            }

            AssetDatabase.Refresh(); // 美术代理可能刚生成占位图，先入库再加载
            VerifyLayerNames();

            BuildLevel(Level1Spec());
            BuildLevel(Level2Spec());
            BuildLevel(Level3Spec());
            SetupBuildSettings();
            DemoSceneBuilder.BuildHeroMenuScene(); // 主菜单场景（标题/开始远征/退出 + 最佳纪录）

            AssetDatabase.SaveAssets();
            Debug.Log("[LevelBuilder] ========== 三关 + 主菜单构建完成：Level1 灰烬森林 / Level2 坍塌矿洞 / Level3 深渊王座 ==========");
        }

        /// <summary>层名只做提示不阻塞：数字掩码不受层名影响，层名注册归 DemoSceneBuilder 项目设置负责。</summary>
        private static void VerifyLayerNames()
        {
            string groundName = LayerMask.LayerToName(LayerGround);
            if (groundName != "Ground")
                Debug.LogWarning($"[LevelBuilder] 层 {LayerGround} 当前名为「{groundName}」（期望 Ground）。" +
                                 "地形已按数字层 8 摆放不受影响；层名注册需在 TagManager 清理旧占用后重跑项目设置");
        }

        private static void SetupBuildSettings()
        {
            // MainMenu 保持第 0 位（启动场景；其内容由成长系统代理重做，这里只保证排序）
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Level1.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Level2.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Level3.unity", true),
            };
            Debug.Log("[BuildSettings] 场景列表：MainMenu(0) → Level1 → Level2 → Level3");
        }

        // ============================================================ 关卡规格 ============================================================
        // 三份 spec 是关卡设计的唯一事实来源：地形（地面高度曲线段+平台）、怪物、陷阱、目标全部在数据里。

        /// <summary>关卡 1 灰烬森林：宽 80 格，3 组缓坡台阶 + 1 处断桥；Spike×4 / Slime×6 / Bat×4，关底 EliteGuard。</summary>
        private static LevelSpec Level1Spec()
        {
            var spec = new LevelSpec
            {
                levelIndex = 1,
                displayName = "灰烬森林",
                widthUnits = 80f,
                baseGroundY = 0f,
                backdropColor = new Color(0.06f, 0.085f, 0.07f),
                lightColor = new Color(0.9f, 0.95f, 0.85f),
                groundTint = new Color(0.95f, 1f, 0.92f),
                goalKind = LevelGoalKind.Elite,
                portalX = 78f,
                nextSceneName = "Level2",
            };
            // 地面高度曲线段：缺口 x∈[30,34) 即断桥（无地面，坠落由 HeroRespawn 回收）
            spec.groundSegments.AddRange(new[]
            {
                Seg(0, 12, 0),                                              // 出生平地
                Seg(12, 14, 1), Seg(14, 16, 2), Seg(16, 20, 2), Seg(20, 22, 1), // 台阶组 A（缓坡上 2 级再下）
                Seg(22, 30, 0),
                // [30,34) 断桥
                Seg(34, 44, 0),
                Seg(44, 46, 1), Seg(46, 48, 2), Seg(48, 52, 2), Seg(52, 54, 1), // 台阶组 B
                Seg(54, 62, 0),
                Seg(62, 64, 1), Seg(64, 66, 2), Seg(66, 70, 2), Seg(70, 72, 1), // 台阶组 C
                Seg(72, 80, 0),                                             // 关底精英平台
            });
            spec.platforms.AddRange(new[]
            {
                Plat(17, 4.5f, 6),  // 台阶组 A 上空的跳跃路线
                Plat(36, 3.5f, 5),  // 断桥后过渡
                Plat(64, 4.5f, 6),  // 台阶组 C 上空
            });
            // 怪物：y 由地面高度自动对齐（地面怪贴地，飞行怪给悬停高度）
            spec.monsters.AddRange(new[]
            {
                // 首只史莱姆随出生点平移（10→13.5，落台阶 A 首级）：索敌半径 6u，留在 10 会开局直追出生点
                Ground(13.5f, "Slime"), Ground(25, "Slime"), Ground(37, "Slime"),
                Ground(50, "Slime"), Ground(60, "Slime"), Ground(74, "Slime"),
                Fly(18, 4, "Bat"), Fly(32, 4, "Bat"), Fly(48, 6, "Bat"), Fly(66, 4, "Bat"),
                Ground(76, "EliteGuard"), // 关底精英
            });
            spec.traps.AddRange(new[]
            {
                // 首根尖刺随出生点平移（8→11）：尖刺触发盒宽 2u，留在 8 时勇者（出生 6.5）
                // 必须先踩刺才能走到第一块教学牌（8.5）前——新手流程改为先看教学再遇首个陷阱
                Trap(11, "Spike"), Trap(26, "Spike"), Trap(38, "Spike"), Trap(58, "Spike"),
            });
            return spec;
        }

        /// <summary>关卡 2 坍塌矿洞：宽 100 格，2-4 格落差的碎台阶；FallingRock×3 / FlameVent×2 / Mage×5 / Bat×6，关底精英（EliteGuard 换色）。</summary>
        private static LevelSpec Level2Spec()
        {
            var spec = new LevelSpec
            {
                levelIndex = 2,
                displayName = "坍塌矿洞",
                widthUnits = 100f,
                baseGroundY = 0f,
                backdropColor = new Color(0.09f, 0.07f, 0.05f),
                lightColor = new Color(0.98f, 0.85f, 0.68f),
                groundTint = new Color(0.85f, 0.93f, 1f), // 蓝灰石砖自带主题色，染色只做轻辅色
                goalKind = LevelGoalKind.Elite,
                portalX = 98f,
                nextSceneName = "Level3",
            };
            // 落差更大的台阶：单级抬升 2-3 格，最大下落 4 格（46→52 高台 → 58 后直落 2）
            spec.groundSegments.AddRange(new[]
            {
                Seg(0, 10, 0),
                Seg(10, 14, 2), Seg(14, 20, 4), Seg(20, 26, 4), Seg(26, 30, 2), Seg(30, 40, 0),
                Seg(40, 46, 3), Seg(46, 52, 6), Seg(52, 58, 6), Seg(58, 64, 2), Seg(64, 74, 0),
                Seg(74, 80, 2), Seg(80, 86, 4), Seg(86, 92, 4), Seg(92, 100, 0),
            });
            spec.platforms.AddRange(new[]
            {
                Plat(31, 3, 5),
                Plat(66, 4, 6),
                Plat(83, 7, 5),
            });
            spec.monsters.AddRange(new[]
            {
                Ground(22, "Mage"), Ground(37, "Mage"), Ground(50, "Mage"), Ground(70, "Mage"), Ground(88, "Mage"),
                Fly(12, 6, "Bat"), Fly(28, 5, "Bat"), Fly(42, 8, "Bat"), Fly(56, 10, "Bat"), Fly(68, 6, "Bat"), Fly(84, 8, "Bat"),
                Elite(96, "EliteGuard", new Color(0.55f, 0.75f, 1f)), // 复用 EliteGuard 换冷色区分矿洞精英
            });
            spec.traps.AddRange(new[]
            {
                Trap(18, "FallingRock"), Trap(44, "FallingRock"), Trap(77, "FallingRock"),
                Trap(32, "FlameVent"), Trap(66, "FlameVent"),
            });
            return spec;
        }

        /// <summary>关卡 3 深渊王座：宽 60 格，前置走廊 + 平坦 BOSS 房；混合怪×8，BossKing 坐镇。</summary>
        private static LevelSpec Level3Spec()
        {
            var spec = new LevelSpec
            {
                levelIndex = 3,
                displayName = "深渊王座",
                widthUnits = 60f,
                baseGroundY = 0f,
                backdropColor = new Color(0.06f, 0.04f, 0.09f),
                lightColor = new Color(0.82f, 0.78f, 1f),
                groundTint = new Color(1.05f, 0.95f, 1.1f), // 黑曜石偏暗，轻微 HDR 染色提亮紫调
                goalKind = LevelGoalKind.Boss,
                portalX = 56f,       // BOSS 关不走传送门（BossHpChanged(0,0) 直接结算），仅占位
                nextSceneName = "",  // 最终关：通关直接结算回主菜单
            };
            spec.groundSegments.Add(Seg(0, 60, 0)); // 全平：走廊(0-22) + BOSS 房(22-60)
            spec.platforms.AddRange(new[]
            {
                Plat(26, 4, 6), // BOSS 房两侧走位平台（躲冲撞/远程拉扯）
                Plat(44, 4, 6),
            });
            spec.monsters.AddRange(new[]
            {
                // 前置走廊混合怪 ×8：史莱姆/蝙蝠/法师三种按节奏混排
                Ground(4, "Mage"), Ground(6, "Slime"),
                Fly(10, 4, "Bat"), Ground(12, "Mage"), Ground(14, "Slime"),
                Fly(16, 4, "Bat"), Ground(18, "Slime"), Ground(20, "Mage"),
                Ground(48, "BossKing"), // 深渊魔王
            });
            return spec;
        }

        // spec 构造小工具（保持上面三段可读）
        private static GroundSegment Seg(float sx, float ex, float y) =>
            new GroundSegment { startX = sx, endX = ex, groundY = y };
        private static PlatformSpec Plat(float x, float y, float w) =>
            new PlatformSpec { x = x, y = y, width = w };
        private static MonsterSpawn Ground(float x, string type) =>
            new MonsterSpawn { position = new Vector2(x, 0f), type = type }; // y 在 Build 前由 AlignMonstersToGround 统一贴地
        private static MonsterSpawn Fly(float x, float y, string type) =>
            new MonsterSpawn { position = new Vector2(x, y), type = type };
        private static MonsterSpawn Elite(float x, string type, Color tint) =>
            new MonsterSpawn { position = new Vector2(x, 0f), type = type, tint = tint };
        private static TrapSpawn Trap(float x, string type) =>
            new TrapSpawn { position = new Vector2(x, 0f), type = type };

        // ============================================================ 单关构建 ============================================================

        private static void BuildLevel(LevelSpec spec)
        {
            AlignMonstersToGround(spec); // 地面怪贴地（飞行怪保留悬停高度）

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var look = PixelEnvironment.For(spec.levelIndex);
            BuildCamera(spec, look);
            BuildLighting(look);
            PixelEnvironment.BuildParallax(look, spec.widthUnits, spec.baseGroundY); // 多层像素视差背景
            BuildManagers();
            BuildHero(spec);
            BuildTerrain(spec, look);
            if (spec.levelIndex == 1) BuildTutorial(spec); // 新手教学牌只在第一关
            BuildDirector(spec);

            // 成长系统 UI（HUD + 选人/升级/结算面板 + HeroGameBootstrap）——由成长代理的构建器统一接线
            var canvas = DemoSceneBuilder.CreateCanvas($"Canvas_HUD_L{spec.levelIndex}", 0);
            DemoSceneBuilder.CreateEventSystem();
            DemoSceneBuilder.BuildHeroUiSet(canvas.transform);

            EnsureDir(SceneDir);
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Level{spec.levelIndex}.unity");
            Debug.Log($"[LevelBuilder] Level{spec.levelIndex}「{spec.displayName}」构建完成：宽 {spec.widthUnits} 格，" +
                      $"地面段 {spec.groundSegments.Count}，怪物 {spec.monsters.Count}，陷阱 {spec.traps.Count}，" +
                      $"目标 {spec.goalKind}");
        }

        /// <summary>地面怪 y 统一对齐到所在地面段的表面高度（spec 数据只写 x，写 y 会在调地形时错位）。</summary>
        private static void AlignMonstersToGround(LevelSpec spec)
        {
            foreach (var m in spec.monsters)
            {
                if (m.type == "Bat") continue; // 飞行怪保持悬停高度
                m.position = new Vector2(m.position.x, GroundYAt(spec, m.position.x));
            }
            foreach (var t in spec.traps)
                t.position = new Vector2(t.position.x, GroundYAt(spec, t.position.x));
        }

        private static float GroundYAt(LevelSpec spec, float x)
        {
            foreach (var s in spec.groundSegments)
                if (x >= s.startX && x < s.endX) return s.groundY;
            return spec.baseGroundY;
        }

        // ---------- 场景组装 ----------

        private static void BuildCamera(LevelSpec spec, PixelEnvironment.Look look)
        {
            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3.5f; // 比例网格：视口高 7u ≈ 勇者身高 1.25u 的 5.6 倍（人物占画面约 1/5 高）
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = look.Sky; // 与最远背景层顶部同色：震屏露边时看不出缝
            cam.cullingMask = ~0;
            cameraGo.transform.position = new Vector3(0f, spec.baseGroundY + 2.8f, -10f);
            cameraGo.AddComponent<SideCamera>(); // 横向跟随（Y 锁地面 + X 边界钳制 + CameraRig 兼容震屏）
            BuildVignette(cameraGo.transform, cam);
        }

        /// <summary>
        /// 全屏暗角 overlay：程序化 vignette 贴图挂在相机上盖满视口（中心全透明不挡战斗）。
        /// 关卡场景没有后处理 Volume（Bloom/Vignette 是主菜单的）， SpriteRenderer 暗角是三关唯一的画面收边。
        /// </summary>
        private static void BuildVignette(Transform cameraRoot, Camera cam)
        {
            var sprite = TrySprite("vignette");
            if (sprite == null) return; // 环境美术没生成时静默跳过（降级策略与全文件一致）
            var go = new GameObject("Vignette");
            go.transform.SetParent(cameraRoot, false);
            go.transform.localPosition = new Vector3(0f, 0f, 5f); // 相机前 5u：世界层之上、UI 画布之下
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 30; // 高于玩法对象（勇者 20/怪物视觉 15），暗角中心透明不遮信息
            // 按相机视口尺寸拉伸（留 5% 余量），任何宽高比都恰好盖满
            float viewW = cam.orthographicSize * cam.aspect * 2f * 1.05f;
            float viewH = cam.orthographicSize * 2f * 1.05f;
            var bounds = sprite.bounds.size;
            go.transform.localScale = new Vector3(viewW / Mathf.Max(0.01f, bounds.x), viewH / Mathf.Max(0.01f, bounds.y), 1f);
        }

        private static void BuildLighting(PixelEnvironment.Look look)
        {
            // URP 2D 渲染器下没有全局光精灵会全黑，每关必带一盏
            var lightGo = new GameObject("GlobalLight");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = look.Light;
        }

        private static void BuildManagers()
        {
            // 全局单例群：GameManager（状态机/timeScale 收口）、PoolService（弹丸池）、
            // Juice（停帧/闪白）、AudioService（音效）——都是 DDOL 单例，重复进入场景自动去重
            var managers = new GameObject("Managers");
            managers.AddComponent<GameManager>();
            managers.AddComponent<PoolService>();
            managers.AddComponent<Juice>();
            managers.AddComponent<AudioService>();
            BindAudio(managers.GetComponent<AudioService>());
        }

        /// <summary>绑定合成音效（顺序对齐 SfxId 枚举；资产缺失则跳过——AudioService 本身 null-safe 可选）。</summary>
        private static void BindAudio(AudioService audio)
        {
            if (audio == null || !AssetDatabase.IsValidFolder(AudioDir)) return;
            var names = new[] { "sfx_shoot", "sfx_hit", "sfx_crit", "sfx_kill", "sfx_elitekill",
                                "sfx_pickup", "sfx_levelup", "sfx_hurt", "sfx_gameover", "sfx_click" };
            var clips = new List<AudioClip>();
            foreach (var n in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{n}.wav");
                if (clip == null) { Debug.LogWarning($"[LevelBuilder] 音效缺失：{n}.wav，跳过音频绑定"); return; }
                clips.Add(clip);
            }
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/music_ambient.wav");
            if (music != null) audio.BindClips(clips, music);
        }

        private static void BuildHero(LevelSpec spec)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[LevelBuilder] 勇者 prefab 缺失：{HeroPrefabPath}（先跑 StarVortex/Hero/构建勇者资产）——本关暂无勇者，场景骨架照常输出");
                return;
            }
            var hero = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            // 出生点 x=6.5：正交 3.5 相机在关卡左端会被钳在 minX+半宽≈6.2（SideCamera 以 minX=0 钳制），
            // 旧出生 x=0 时勇者贴屏幕左缘半个身子出画——挪到钳制线右侧一点，人物完整可见且前方留白。
            // L3 例外：前置走廊 4-20 是混合怪长廊（Slime 索敌半径 6u），6.5 会直接生在史莱姆（x=6）窝里，
            // 改在走廊口 x=2——虽然相机仍钳在 6.2，人物距左缘 2u 完整可见，也不会开局即接战。
            float spawnX = spec.levelIndex == 3 ? 2f : 6.5f;
            hero.transform.position = new Vector3(spawnX, spec.baseGroundY + 1f, 0f);
            // prefab 本体未设层（Default 层与碰撞矩阵互不相撞会掉出世界）——只改场景实例补挂 Player 层，
            // prefab 资产的层归属仍由勇者资产构建器负责（本类不改他人资产）
            hero.layer = LayerPlayer;
            hero.AddComponent<HeroRespawn>(); // 坠落回收（y<-5 扣 20 血回落脚点）
        }

        /// <summary>
        /// 地形：碰撞体与视觉分开——父物体上的 BoxCollider2D 顶面精确落在 groundY（物理契约不变），
        /// 九宫格像素地块挂在子物体 Visual 上，草叶/石块凸起可以略高出碰撞面而不影响站立。
        /// 同高相邻段先合并，平地上不会出现一段一道的侧边竖缝。
        /// </summary>
        private static void BuildTerrain(LevelSpec spec, PixelEnvironment.Look look)
        {
            var root = new GameObject("Terrain");

            foreach (var run in PixelEnvironment.MergeRuns(spec.groundSegments))
            {
                float width = run.endX - run.startX;
                float height = run.groundY - PixelEnvironment.TerrainBottomY;
                var go = new GameObject($"Ground_{run.startX:F0}_{run.endX:F0}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3((run.startX + run.endX) * 0.5f, PixelEnvironment.TerrainBottomY + height * 0.5f, 0f);
                go.layer = LayerGround;
                go.AddComponent<BoxCollider2D>().size = new Vector2(width, height); // 顶面 = groundY
                PixelEnvironment.AddGroundVisual(go.transform, look, run.startX, run.endX, run.groundY);
            }

            // 悬浮平台：0.6u 厚的碰撞板，顶面精确落在 y
            foreach (var p in spec.platforms)
            {
                var go = new GameObject($"Platform_{p.x:F0}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(p.x + p.width * 0.5f, p.y - 0.3f, 0f);
                go.layer = LayerGround;
                go.AddComponent<BoxCollider2D>().size = new Vector2(p.width, 0.6f);
                PixelEnvironment.AddPlatformVisual(go.transform, look, p.x, p.width, p.y);
            }

            // 两端隐形墙：防止勇者走出关卡（左墙避开出生点 x=0，表面退到 -0.5）
            BuildWall(root.transform, -1.25f, spec.baseGroundY + 2f, 1.5f, 12f);
            BuildWall(root.transform, spec.widthUnits + 1.25f, spec.baseGroundY + 2f, 1.5f, 12f);
        }

        private static void BuildWall(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject($"Wall_{x:F0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.layer = LayerGround;
            go.AddComponent<BoxCollider2D>().size = new Vector2(w, h);
        }

        // ---------- 新手教学（只在 Level1） ----------

        /// <summary>
        /// 教学牌布点（随出生点 6.5 平移）：8.5 起步，间隔 7.5-8u（断桥 [30,34) 前后强制拉开 12）。
        /// 触发柱宽 2.6u（±1.3），逐点核对过尖刺触发盒（宽 2u，±1）与断桥：
        /// 8.5 避尖刺@11 / 23.5 避尖刺@26 / 35.5 避断桥落点与尖刺@38 / 42.5 避台阶 B——
        /// 16 压在台阶组 A 高台 y=2 上，头顶正对悬浮平台（17,4.5），学二段跳即有落脚目标。
        /// </summary>
        private static readonly float[] TutorialSignXs = { 8.5f, 16f, 23.5f, 35.5f, 42.5f };

        private static readonly string[] TutorialMessages =
        {
            "A / D 或 ← → 移动\n跑起来，勇者！", // 首牌两行式：第一行操作、第二行打气短句
            "空格 跳跃 · 空中再按一次 = 二段跳",
            "J 攻击 · 连按触发三段连击",
            "L 冲刺 · 冲刺瞬间无敌",
            "K 特殊技 · 消耗蓝量",
        };

        private const string CjkFontAssetRefPath = "Assets/Fonts/TMP_CJK.asset"; // 与 DemoSceneBuilder.CjkFontAssetPath 同源

        private static void BuildTutorial(LevelSpec spec)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetRefPath);
            var messages = (string[])TutorialMessages.Clone();
            if (font == null)
            {
                Debug.LogWarning("[LevelBuilder] 缺 TMP_CJK 字体资产——教学牌文字将不可读（先跑 StarVortex/① 一键搭建）");
            }
            else
            {
                messages = BakeTutorialChars(font, messages); // 补烘字母/箭头字形；缺字形时降级替换文案
            }

            for (int i = 0; i < messages.Length; i++)
            {
                float x = TutorialSignXs[i];
                var go = new GameObject($"TutorialSign_{i + 1}");
                go.transform.position = new Vector3(x, GroundYAt(spec, x), 0f);
                go.layer = LayerGround; // 触发走 Player↔Ground 碰撞矩阵（与 SpikeTrap 同契约）

                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(2.6f, 3.4f);
                col.offset = new Vector2(0f, 1.7f); // 触发柱：地面到跳跃顶点高度都能进入

                var sign = go.AddComponent<TutorialSign>();
                sign.Configure(messages[i], font);
            }
        }

        /// <summary>
        /// 教学文案里有一批字不在 ChineseChars 初始烘焙集（A D J L K / = ← → 二 跃）——
        /// 把整条文案喂给 TryAddCharacters 按 EnsureFontExtraChars 同款"动态→补烘→静态"舞步
        /// 补进字体图集并嵌入子资产（黑体字库必含这些常用字，实测缺字只会来自罕见字体源）；
        /// 箭头字形缺失时把 ←→ 替换成「方向键」保底（保底用字全部在初始集内）。
        /// </summary>
        private static string[] BakeTutorialChars(TMP_FontAsset font, string[] messages)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in messages) sb.Append(m);
            sb.Append(' '); // 空格字形兜底

            // 已全部烘焙过就不再动图集：TMP 3.0.6 在"没有新字可加"时也返回 false 并把整串当成缺失，会刷出误报警告
            if (font.HasCharacters(sb.ToString())) return messages;

            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.TryAddCharacters(sb.ToString(), out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[LevelBuilder] 教学文案缺字形：{missing}（相应字符将显示为方块）");

            // 动态烘焙产生的图集纹理/材质必须嵌回资产本体，否则重载后引用悬空（与 EnsureCjkFont 同坑）
            if (font.atlasTextures != null)
                foreach (var tex in font.atlasTextures)
                    if (tex != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                        AssetDatabase.AddObjectToAsset(tex, font);
            if (font.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(font.material)))
                AssetDatabase.AddObjectToAsset(font.material, font);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);

            bool arrowMissing = missing != null && (missing.Contains("←") || missing.Contains("→"));
            if (arrowMissing)
                messages[0] = "A / D 或 方向键 移动\n跑起来，勇者！"; // 保底用字全部在初始集内，保留两行式
            return messages;
        }

        // ---------- 关卡引导器 ----------

        private static void BuildDirector(LevelSpec spec)
        {
            var go = new GameObject("LevelDirector");
            var director = go.AddComponent<LevelDirector>();

            // 怪物 prefab 绑定（由另一流程生成；加载不到 → LogWarning 跳过，保证可分阶段跑通）
            var monsterBindings = new List<LevelDirector.PrefabBinding>();
            GameObject bossPrefab = null;
            foreach (var type in new[] { "Slime", "Bat", "Mage", "EliteGuard", "BossKing" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MonsterPrefabDir}/{type}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[LevelBuilder] 怪物 prefab 缺失：{MonsterPrefabDir}/{type}.prefab（运行期该类怪物将跳过）");
                    continue;
                }
                monsterBindings.Add(new LevelDirector.PrefabBinding { key = type, prefab = prefab });
                if (type == "BossKing") bossPrefab = prefab;
            }
            // 任务书把法师叫 MageAI（组件名），prefab 文件叫 Mage——两个键都注册，spec 用哪个名都能命中
            var mageBinding = monsterBindings.Find(b => b.key == "Mage");
            if (mageBinding != null)
                monsterBindings.Add(new LevelDirector.PrefabBinding { key = "MageAI", prefab = mageBinding.prefab });

            // 勇者弹丸池（剑气/火球/箭）：键名对齐 ProjectilePool 常量
            var projectileBindings = new List<LevelDirector.PrefabBinding>();
            foreach (var key in new[] { ProjectilePool.Fireball, ProjectilePool.Arrow, ProjectilePool.SwordWave })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MonsterPrefabDir}/{key}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[LevelBuilder] 弹丸 prefab 缺失：{key}.prefab（对应武器将静默降级）");
                    continue;
                }
                projectileBindings.Add(new LevelDirector.PrefabBinding { key = key, prefab = prefab });
            }

            // BOSS 关血条初值：从 BossKing prefab 的 EnemyBase.maxHp 读真实数值（protected 序列化字段可反射到）
            if (spec.goalKind == LevelGoalKind.Boss && bossPrefab != null)
            {
                var bossBase = bossPrefab.GetComponent<EnemyBase>();
                if (bossBase != null)
                {
                    var hp = new SerializedObject(bossBase).FindProperty("maxHp");
                    if (hp != null && hp.floatValue > 0f) spec.bossMaxHp = hp.floatValue;
                }
            }

            // 陷阱/传送门视觉精灵：按名加载（Hifi 优先 → Generated），缺失传 null 由运行期纯色兜底。
            // 尖刺换金属尖刺条、传送门换漩涡环（TileArtGenerator 环境套件）；落石/火口底座用本关瓦片保主题一致
            string levelTile = $"platform_l{spec.levelIndex}";
            director.InjectVisuals(
                TrySprite("spikes_metal"),   // 尖刺：银灰渐变金属条（缺资产回退运行期灰盒）
                TrySprite(levelTile) ?? TrySprite("platform_tile"), // 落石（tile 染深色充岩块）
                TrySprite("proj_fireball"),  // 火焰
                TrySprite("portal_vortex") ?? TrySprite("portal"),  // 传送门：多层紫蓝漩涡环
                TrySprite(levelTile) ?? TrySprite("platform_tile")); // 火口底座

            // 像素机关（Assets/Art/Pixel/Fx）：覆盖上面的旧占位精灵；缺帧时 LevelDirector 自动退回旧表现
            Sprite First(string action)
            {
                var frames = PixelArtPipeline.LoadFrames("Fx", action);
                return frames.Length > 0 ? frames[0] : null;
            }
            var vents = PixelArtPipeline.LoadFrames("Fx", "Vent");
            director.InjectPixelVisuals(
                First("Spike") ?? TrySprite("spikes_metal"),
                First("Rock"),
                vents.Length > 0 ? vents[0] : null,
                vents.Length > 1 ? vents[1] : null,
                PixelArtPipeline.LoadFrames("Fx", "Flame"),
                PixelArtPipeline.LoadFrames("Fx", "Portal"),
                PixelArtPipeline.LoadFrames("Fx", "Spark"));

            director.InjectForBuild(spec, monsterBindings, projectileBindings);
        }

        // ============================================================ 资产工具 ============================================================

        /// <summary>按名找精灵：Hifi（AI 成品）优先 → Generated（程序化占位）→ null（由调用方兜底）。</summary>
        private static Sprite TrySprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Hifi/{name}.png")
                ?? AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Generated/{name}.png");
        }

        private static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir)!.Replace('\\', '/'), Path.GetFileName(dir));
        }
    }
}
