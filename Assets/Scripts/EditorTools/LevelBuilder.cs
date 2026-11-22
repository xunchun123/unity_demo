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
        private const string FallbackSpritePath = "Assets/Art/Generated/level_fallback_white.png";

        // 层数字契约：与 DemoSceneBuilder.Settings.cs（Player=6/Enemy=7/Ground=8）及
        // HeroController.groundMask（1<<8）一致。用数字不用 LayerMask.NameToLayer——
        // TagManager 里层名可能被旧项目设置占用（当前 8 层叫 PlayerBullet），数字才是运行时唯一事实
        private const int LayerPlayer = 6;
        private const int LayerGround = 8;

        private static Sprite _fallbackWhite; // 编辑器会话内缓存（资产本体已落盘，引用可持久化）

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
                Ground(10, "Slime"), Ground(25, "Slime"), Ground(37, "Slime"),
                Ground(50, "Slime"), Ground(60, "Slime"), Ground(74, "Slime"),
                Fly(18, 4, "Bat"), Fly(32, 4, "Bat"), Fly(48, 6, "Bat"), Fly(66, 4, "Bat"),
                Ground(76, "EliteGuard"), // 关底精英
            });
            spec.traps.AddRange(new[]
            {
                Trap(8, "Spike"), Trap(26, "Spike"), Trap(38, "Spike"), Trap(58, "Spike"),
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

            BuildCamera(spec);
            BuildLighting(spec);
            BuildBackdrop(spec);
            BuildScenery(spec);            // 远景剪影 + 雾带氛围层（AI 背景之上、地形之下）
            BuildManagers();
            BuildHero(spec);
            BuildTerrain(spec);
            BuildProps(spec);              // 平台装饰散植（草丛/花/蘑菇/石/树桩/藤）
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

        private static void BuildCamera(LevelSpec spec)
        {
            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3.5f; // 比例网格：视口高 7u ≈ 勇者身高 1.25u 的 5.6 倍（人物占画面约 1/5 高）
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = spec.backdropColor;
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

        private static void BuildLighting(LevelSpec spec)
        {
            // URP 2D 渲染器下没有全局光精灵会全黑，每关必带一盏
            var lightGo = new GameObject("GlobalLight");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = spec.lightColor;
        }

        private static void BuildBackdrop(LevelSpec spec)
        {
            // 静态大背景（旧占位星空/菜单底图都可用）：排序层压到最底，主题色由光照与背景色承担。
            // 不再用 BackgroundParallax（旧太空玩法组件正在删除，且视差对静态底图是过度设计）。
            var backdrop = TrySprite($"bg_level{spec.levelIndex}") ?? TrySprite("menu_bg");
            if (backdrop == null)
            {
                Debug.LogWarning("[LevelBuilder] 背景精灵缺失（menu_bg/star_bg_0），用纯色方块兜底");
                backdrop = FallbackWhite();
            }
            var go = new GameObject("Backdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = backdrop;
            sr.color = new Color(0.4f, 0.42f, 0.45f, 1f); // 压暗，避免抢主体
            sr.sortingOrder = -50;
            var bounds = backdrop.bounds;
            go.transform.position = new Vector3(spec.widthUnits * 0.5f, spec.baseGroundY + 4f, 0f);
            go.transform.localScale = new Vector3(
                (spec.widthUnits + 10f) / Mathf.Max(0.01f, bounds.size.x),
                16f / Mathf.Max(0.01f, bounds.size.y), 1f);
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
            hero.transform.position = new Vector3(0f, spec.baseGroundY + 1f, 0f); // 关卡 1 出生点 (0,1)
            // prefab 本体未设层（Default 层与碰撞矩阵互不相撞会掉出世界）——只改场景实例补挂 Player 层，
            // prefab 资产的层归属仍由勇者资产构建器负责（本类不改他人资产）
            hero.layer = LayerPlayer;
            hero.AddComponent<HeroRespawn>(); // 坠落回收（y<-5 扣 20 血回落脚点）
        }

        private static void BuildTerrain(LevelSpec spec)
        {
            var root = new GameObject("Terrain");

            // 三关地形瓦片：platform_l1 灰烬森林 / platform_l2 坍塌矿洞 / platform_l3 深渊王座
            //（TileArtGenerator 生成；Tiled 平铺模式要求纹理 Repeat 包裹；缺资产回退旧占位砖）
            string tileName = $"platform_l{spec.levelIndex}";
            var tile = TrySprite(tileName) ?? TrySprite("platform_tile");
            if (tile != null) EnsureRepeat(tile.name);
            var tileSprite = tile != null ? tile : FallbackWhite();
            float tileH = tile != null ? tile.bounds.size.y : 2f; // 瓦片世界高度（256px@PPU100 → 2.56u）

            // 地面段：BoxCollider2D 顶面精确落在 groundY（碰撞契约不动）；
            // 视觉厚度取整张瓦片高——平铺恰好一整行，瓦片顶缘的草皮/描边必然露出地表
            foreach (var s in spec.groundSegments)
            {
                float width = s.endX - s.startX;
                if (width <= 0f) continue;
                var go = new GameObject($"Ground_{s.startX:F0}_{s.endX:F0}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3((s.startX + s.endX) * 0.5f, s.groundY - tileH * 0.5f, 0f); // 顶面=groundY
                go.layer = LayerGround;

                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(width, 2f); // 碰撞仍厚 2（比视觉薄的部分埋在地下，运行时无感）

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(width, tileH);
                sr.color = spec.groundTint;
                sr.sortingOrder = 0;
            }

            // 悬浮平台：薄板（顶面精确落在 y）；Tiled 从上往下铺贴，顶缘草皮露出
            foreach (var p in spec.platforms)
            {
                var go = new GameObject($"Platform_{p.x:F0}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(p.x + p.width * 0.5f, p.y - 0.3f, 0f);
                go.layer = LayerGround;

                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(p.width, 0.6f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(p.width, 0.6f);
                sr.color = Color.Lerp(spec.groundTint, Color.white, 0.25f); // 平台略亮，与主地形区分
                sr.sortingOrder = 0;
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

        // ---------- 远景氛围层（剪影 + 雾带） ----------

        /// <summary>
        /// 远景剪影 + 雾带：AI 背景图之上、地形之下的氛围夹层（sortingOrder -46 ~ -40）。
        /// 视差用 ParallaxScroller（父对象按 (1−factor)·camX 横移、纵向钉在地平线）——
        /// 旧 BackgroundParallax 是"贴相机 XY + UV 偏移"的星空件，横版剪影必须钉世界高度，
        /// 且逐株摆放比 UV 平铺更有机，故按任务书写简单跟随。
        /// 两层剪影 + 两层雾（远/近不同系数、透明度、疏密）：错周期叠出非重复天际线。
        /// </summary>
        private static void BuildScenery(LevelSpec spec)
        {
            var tree = TrySprite("tree_far");
            var fog = TrySprite("fog_strip");
            if (tree == null) Debug.LogWarning("[LevelBuilder] 缺 tree_far.png，远景剪影层跳过（先跑一键搭建生成环境美术）");
            if (fog == null) Debug.LogWarning("[LevelBuilder] 缺 fog_strip.png，雾带层跳过");
            if (tree == null && fog == null) return;

            float gy = spec.baseGroundY;
            // 关卡氛围参数：L1 森林（暗绿剪影密两层 + 轻晨雾）/ L2 矿洞（冷灰矮影 + 浓冷雾）/ L3 深渊（紫黑枯影 + 紫雾）
            Color treeTint, fogTint;
            float treeAlphaFar, treeAlphaNear, treeScaleMin, treeScaleMax, treeGapMin, treeGapMax;
            float fogAlphaMin, fogAlphaMax, fogGapMin, fogGapMax, fogScaleMin, fogScaleMax;
            switch (spec.levelIndex)
            {
                case 2:
                    treeTint = new Color(0.72f, 0.82f, 0.9f); treeAlphaFar = 0.5f; treeAlphaNear = 0.75f;
                    treeScaleMin = 0.62f; treeScaleMax = 0.9f; treeGapMin = 3.6f; treeGapMax = 5.6f;
                    fogTint = new Color(0.72f, 0.84f, 0.95f); fogAlphaMin = 0.55f; fogAlphaMax = 0.85f;
                    fogGapMin = 2.6f; fogGapMax = 4.2f; fogScaleMin = 1.4f; fogScaleMax = 2.3f;
                    break;
                case 3:
                    treeTint = new Color(0.5f, 0.42f, 0.62f); treeAlphaFar = 0.55f; treeAlphaNear = 0.85f;
                    treeScaleMin = 0.8f; treeScaleMax = 1.15f; treeGapMin = 3.2f; treeGapMax = 5.2f;
                    fogTint = new Color(0.78f, 0.66f, 0.95f); fogAlphaMin = 0.5f; fogAlphaMax = 0.8f;
                    fogGapMin = 3.0f; fogGapMax = 4.8f; fogScaleMin = 1.3f; fogScaleMax = 2.2f;
                    break;
                default:
                    treeTint = new Color(0.62f, 0.72f, 0.64f); treeAlphaFar = 0.5f; treeAlphaNear = 0.8f;
                    treeScaleMin = 0.85f; treeScaleMax = 1.25f; treeGapMin = 2.6f; treeGapMax = 4.4f;
                    fogTint = new Color(0.85f, 0.93f, 0.9f); fogAlphaMin = 0.45f; fogAlphaMax = 0.7f;
                    fogGapMin = 3.4f; fogGapMax = 5.4f; fogScaleMin = 1.3f; fogScaleMax = 2.1f;
                    break;
            }

            var sceneryRoot = new GameObject("Scenery");

            if (tree != null)
            {
                var farTint = treeTint; farTint.a = treeAlphaFar;
                BuildTreeBand(sceneryRoot.transform, spec, tree, "TreesFar", 0.26f, gy,
                    treeScaleMin * 0.85f, treeScaleMax * 0.85f, treeGapMin * 1.2f, treeGapMax * 1.2f, farTint, -46, 4711);
                var nearTint = treeTint; nearTint.a = treeAlphaNear;
                BuildTreeBand(sceneryRoot.transform, spec, tree, "TreesNear", 0.36f, gy,
                    treeScaleMin, treeScaleMax, treeGapMin, treeGapMax, nearTint, -42, 4712);
            }

            if (fog != null)
            {
                var fogLow = fogTint; fogLow.a = fogAlphaMin;
                BuildFogBand(sceneryRoot.transform, spec, fog, "FogLow", 0.52f, gy + 0.85f,
                    fogScaleMin, fogScaleMax, fogGapMin, fogGapMax, fogLow, -44, 8711);
                var fogMid = fogTint; fogMid.a = fogAlphaMax;
                BuildFogBand(sceneryRoot.transform, spec, fog, "FogMid", 0.62f, gy + 1.9f,
                    fogScaleMin * 1.15f, fogScaleMax * 1.15f, fogGapMin * 1.15f, fogGapMax * 1.15f, fogMid, -40, 8712);
            }
        }

        /// <summary>
        /// 一条剪影带：按固定种子逐株撒 tree_far（随机缩放/镜像），覆盖域按视差推导——
        /// 子物体世界 x = 设计 x0 + camX·(1−f)，可见 ⇔ x0 − camX·f ∈ [−半视宽, +半视宽]，
        /// 故 x0 需覆盖 [−余量, 关卡宽·f + 余量]（余量 12u 吸收宽屏视口与震屏偏移）。
        /// </summary>
        private static void BuildTreeBand(Transform parent, LevelSpec spec, Sprite sprite, string name, float factor,
            float baseY, float scaleMin, float scaleMax, float gapMin, float gapMax, Color tint, int sortingOrder, int seed)
        {
            var band = new GameObject(name);
            band.transform.SetParent(parent, false);
            band.transform.position = new Vector3(0f, baseY, 0f);
            band.AddComponent<ParallaxScroller>().Configure(factor);

            var rng = new System.Random(seed);
            float halfH = sprite.bounds.extents.y;
            float x = -12f;
            float endX = spec.widthUnits * factor + 12f;
            while (x < endX)
            {
                float s = scaleMin + (float)rng.NextDouble() * (scaleMax - scaleMin);
                var go = new GameObject($"Tree_{x:F0}");
                go.transform.SetParent(band.transform, false);
                go.transform.localPosition = new Vector3(x, -halfH * s - 0.35f, 0f); // 纹理底缘沉入地表 0.35u
                go.transform.localScale = new Vector3(s * (rng.Next(0, 2) == 0 ? 1f : -1f), s, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = tint;
                sr.sortingOrder = sortingOrder;
                x += gapMin + (float)rng.NextDouble() * (gapMax - gapMin);
            }
        }

        /// <summary>一条雾带：fog_strip 逐团散布（随机横向拉伸/高度抖动/透明度），贴地低雾与悬空中雾各一条。</summary>
        private static void BuildFogBand(Transform parent, LevelSpec spec, Sprite sprite, string name, float factor,
            float centerY, float scaleMin, float scaleMax, float gapMin, float gapMax, Color tint, int sortingOrder, int seed)
        {
            var band = new GameObject(name);
            band.transform.SetParent(parent, false);
            band.AddComponent<ParallaxScroller>().Configure(factor);

            var rng = new System.Random(seed);
            float x = -12f;
            float endX = spec.widthUnits * factor + 12f;
            while (x < endX)
            {
                float sx = scaleMin + (float)rng.NextDouble() * (scaleMax - scaleMin);
                float sy = 0.75f + (float)rng.NextDouble() * 0.5f;
                var go = new GameObject($"Fog_{x:F0}");
                go.transform.SetParent(band.transform, false);
                go.transform.localPosition = new Vector3(x, centerY + ((float)rng.NextDouble() - 0.5f) * 0.8f, 0f);
                go.transform.localScale = new Vector3(sx, sy, 1f);
                var c = tint;
                c.a = tint.a * (0.7f + 0.5f * (float)rng.NextDouble());
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = c;
                sr.sortingOrder = sortingOrder;
                x += gapMin + (float)rng.NextDouble() * (gapMax - gapMin);
            }
        }

        // ---------- 平台装饰散植 ----------

        /// <summary>
        /// 各关 prop 色板：同一批贴图按关卡主题染色（L1 自然色 / L2 矿洞冷色+荧光蘑菇 / L3 深渊紫黑）。
        /// 数组内重复项即权重——草丛多、蘑菇树桩少，密度克制不遮挡战斗（纯视觉，sortingOrder=1）。
        /// </summary>
        private static readonly (string sprite, Color tint)[] PropPaletteL1 =
        {
            ("grass_tuft_0", Color.white), ("grass_tuft_1", Color.white), ("grass_tuft_2", Color.white),
            ("grass_tuft_0", new Color(0.92f, 1f, 0.9f)), ("grass_tuft_1", new Color(0.95f, 1f, 0.88f)),
            ("flower", Color.white), ("flower", new Color(1f, 0.95f, 0.85f)),
            ("mushroom", new Color(0.95f, 0.85f, 0.8f)), ("stump", Color.white), ("rock", Color.white),
        };

        private static readonly (string sprite, Color tint)[] PropPaletteL2 =
        {
            ("rock", new Color(0.82f, 0.9f, 1f)), ("rock", new Color(0.75f, 0.85f, 0.95f)),
            ("mushroom", new Color(0.45f, 1.05f, 1.15f)),  // 荧光蓝蘑菇（HDR 喂 Bloom）
            ("mushroom", new Color(0.55f, 0.95f, 1.1f)),
            ("grass_tuft_0", new Color(0.62f, 0.82f, 0.72f)), // 苔丛
            ("grass_tuft_2", new Color(0.55f, 0.75f, 0.68f)),
        };

        private static readonly (string sprite, Color tint)[] PropPaletteL3 =
        {
            ("rock", new Color(0.62f, 0.55f, 0.8f)), ("rock", new Color(0.55f, 0.48f, 0.72f)),
            ("mushroom", new Color(1.05f, 0.5f, 1.3f)),    // 深渊紫光蘑菇
            ("grass_tuft_1", new Color(0.5f, 0.42f, 0.62f)), // 枯草
            ("grass_tuft_2", new Color(0.45f, 0.4f, 0.58f)),
        };

        /// <summary>地表每段撒 1-3 株 + 悬浮平台顶面 0-1 株小装饰、底面垂 1-2 条藤蔓（固定种子可复现）。</summary>
        private static void BuildProps(LevelSpec spec)
        {
            var palette = spec.levelIndex == 1 ? PropPaletteL1 : spec.levelIndex == 2 ? PropPaletteL2 : PropPaletteL3;
            var propRoot = new GameObject("Props");
            var rng = new System.Random(20261122 + spec.levelIndex * 7919);

            foreach (var s in spec.groundSegments)
            {
                float width = s.endX - s.startX;
                if (width < 2f) continue;
                int count = 1 + rng.Next(0, 3);
                for (int i = 0; i < count; i++)
                {
                    float x = s.startX + 0.7f + (float)rng.NextDouble() * (width - 1.4f);
                    SpawnProp(propRoot.transform, palette[rng.Next(palette.Length)],
                        new Vector2(x, s.groundY - 0.05f), rng);
                }
            }

            Color vineTint = spec.levelIndex == 1 ? new Color(0.9f, 1f, 0.85f)
                : spec.levelIndex == 2 ? new Color(0.6f, 0.9f, 0.85f)
                : new Color(0.7f, 0.5f, 0.95f);
            var vine = TrySprite("vine");
            foreach (var p in spec.platforms)
            {
                // 平台顶面只放小型 prop（stump/rock 太重，重抽几次规避）
                if (rng.Next(0, 2) == 1)
                {
                    var pick = palette[rng.Next(palette.Length)];
                    for (int t = 0; t < 4 && (pick.sprite == "stump" || pick.sprite == "rock"); t++)
                        pick = palette[rng.Next(palette.Length)];
                    SpawnProp(propRoot.transform, pick,
                        new Vector2(p.x + 0.8f + (float)rng.NextDouble() * (p.width - 1.6f), p.y - 0.05f), rng);
                }
                // 平台底缘垂藤：顶部贴平台底、向下垂落（竖长图按 bounds 对齐）
                if (vine == null) continue;
                int vines = 1 + rng.Next(0, 2);
                for (int i = 0; i < vines; i++)
                {
                    float x = p.x + 0.6f + (float)rng.NextDouble() * (p.width - 1.2f);
                    float scaleY = 1.4f + (float)rng.NextDouble() * 0.8f;
                    var sr = SpawnProp(propRoot.transform, ("vine", vineTint), new Vector2(x, p.y - 0.6f), rng);
                    if (sr == null) continue;
                    float half = vine.bounds.extents.y * scaleY;
                    sr.transform.localScale = new Vector3(sr.transform.localScale.x, scaleY, 1f);
                    sr.transform.localPosition = new Vector3(x, p.y - 0.62f - half, 0f);
                }
            }
        }

        /// <summary>单株 prop：缺资产静默跳过（降级策略与全文件一致）；随机镜像 + 轻微缩放抖动出群感。</summary>
        private static SpriteRenderer SpawnProp(Transform parent, (string sprite, Color tint) prop, Vector2 pos, System.Random rng)
        {
            var sprite = TrySprite(prop.sprite);
            if (sprite == null) return null;
            var go = new GameObject($"Prop_{prop.sprite}_{pos.x:F1}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = prop.tint;
            sr.sortingOrder = 1; // 压地形之上、全部玩法对象（尖刺 5/怪物视觉 15/勇者 20）之下
            float flip = rng.Next(0, 2) == 0 ? 1f : -1f;
            float s = 0.85f + (float)rng.NextDouble() * 0.35f;
            go.transform.localScale = new Vector3(s * flip, s, 1f);
            return sr;
        }

        // ---------- 新手教学（只在 Level1） ----------

        /// <summary>教学牌布点：Level1 起点沿线间隔 6-9u，避开断桥 [30,34) 与台阶拐点（x=35 紧接断桥落点）。</summary>
        private static readonly float[] TutorialSignXs = { 4f, 11f, 18f, 26f, 35f };

        private static readonly string[] TutorialMessages =
        {
            "A / D 或 ← → 移动",
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
                messages[0] = "A / D 或 方向键 移动";
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

            director.InjectForBuild(spec, monsterBindings, projectileBindings);
        }

        // ============================================================ 资产工具 ============================================================

        /// <summary>按名找精灵：Hifi（AI 成品）优先 → Generated（程序化占位）→ null（由调用方兜底）。</summary>
        private static Sprite TrySprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Hifi/{name}.png")
                ?? AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Generated/{name}.png");
        }

        /// <summary>纯色方块兜底：生成 8×8 白 png 并导入为 Sprite（必须落盘成资产——内存 Sprite.Create 引用保存场景会悬空）。</summary>
        private static Sprite FallbackWhite()
        {
            if (_fallbackWhite != null) return _fallbackWhite;
            _fallbackWhite = AssetDatabase.LoadAssetAtPath<Sprite>(FallbackSpritePath);
            if (_fallbackWhite != null) return _fallbackWhite;

            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var white = new Color32(255, 255, 255, 255);
            var pixels = new Color32[8 * 8];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
            tex.SetPixels32(pixels);
            tex.Apply();

            // 写盘用绝对路径（相对路径依赖编辑器工作目录，batch 环境下不可靠）
            string absPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                FallbackSpritePath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(FallbackSpritePath);
            var importer = AssetImporter.GetAtPath(FallbackSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f; // 与项目其他精灵同一 PPU 契约
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            _fallbackWhite = AssetDatabase.LoadAssetAtPath<Sprite>(FallbackSpritePath);
            Debug.LogWarning($"[LevelBuilder] 已生成纯色兜底精灵：{FallbackSpritePath}");
            return _fallbackWhite;
        }

        /// <summary>Tiled 平铺前提：纹理包裹模式 Repeat（与 DemoSceneBuilder.EnsureTextureRepeat 同款做法）。</summary>
        private static void EnsureRepeat(string spriteName)
        {
            foreach (string dir in new[] { "Assets/Art/Generated", "Assets/Art/Hifi" })
            {
                string path = $"{dir}/{spriteName}.png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.wrapMode != TextureWrapMode.Repeat)
                {
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.SaveAndReimport();
                }
                return;
            }
        }

        private static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir)!.Replace('\\', '/'), Path.GetFileName(dir));
        }
    }
}
