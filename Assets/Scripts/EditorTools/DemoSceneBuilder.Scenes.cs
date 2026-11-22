using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StarVortex.Core;
using StarVortex.Data;
using StarVortex.Gameplay;
using StarVortex.UI;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>一键搭建器（场景部分）：GameScene + MainMenu 的完整场景与 UGUI 层级。</summary>
    public static partial class DemoSceneBuilder
    {
        // 游戏内全部中文字符集：用于烘焙 TMP SDF 字体图集（静态化后打包可用，不依赖系统字体）
        private const string ChineseChars =
            "星涡幸存者开始游戏退出暂无纪录来创造第一个最佳存活击杀等级已暂停继续回主菜单" +
            "死亡结算时间数最终新再一局返回脉冲步枪自动锁定最近的敌人射击浮游机冲重装单位" +
            "火力强化伤害提升超频扳机攻击速度推进器移动装甲扩容最大生命值并立即回复引力" +
            "线圈拾取范围多弹幕弱扫描暴率成双倍纳修复每秒点按需升级选择一项快冷却无目标" +
            "·！（）：、。？！+%";

        private static TMP_FontAsset _cjkFont;

        // ============================ GameScene ============================

        private static void BuildGameScene()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // ---------- 相机 ----------
            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Bg;
            cam.cullingMask = ~0;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cameraGo.AddComponent<CameraRig>();
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);

            // ---------- 全局光 + 后处理 Volume ----------
            var lightGo = new GameObject("GlobalLight");
            var global = lightGo.AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 0.9f;
            global.color = new Color(0.86f, 0.9f, 1f);
            CreateBloomVolume();

            // ---------- 背景（三层视差星空） ----------
            BuildBackground(cameraGo.transform);

            // ---------- 管理器 ----------
            var managers = new GameObject("Managers");
            managers.AddComponent<GameManager>();
            managers.AddComponent<PoolService>();
            var juice = managers.AddComponent<Juice>();
            var audio = managers.AddComponent<AudioService>();

            // ---------- 玩家 ----------
            var player = BuildPlayer();
            player.transform.position = Vector3.zero;

            // ---------- 玩家光环（Sprite Light2D） ----------
            var aura = new GameObject("Aura");
            aura.transform.SetParent(player.transform, false);
            var auraLight = aura.AddComponent<Light2D>();
            auraLight.lightType = Light2D.LightType.Sprite;
            auraLight.lightCookieSprite = Art("glow_disc"); // URP14 中 Sprite 光源的贴图字段叫 lightCookieSprite
            auraLight.color = new Color(0.25f, 0.88f, 1f);
            auraLight.intensity = 1.5f;
            auraLight.overlapOperation = Light2D.OverlapOperation.Additive;
            aura.transform.localScale = Vector3.one * 6f;
            aura.transform.localPosition = Vector3.zero;

            // ---------- 系统对象 ----------
            var directorGo = new GameObject("EnemyDirector");
            var director = directorGo.AddComponent<EnemyDirector>();
            SetField(director, "_difficulty", _difficulty);
            SetField(director, "_droneData", _drone);
            SetField(director, "_darterData", _darter);
            SetField(director, "_bruteData", _brute);
            SetField(director, "_enemyPrefab", _enemyPrefab);

            // 宝石池 / 飘字池
            var gemPoolGo = new GameObject("GemPool");
            var gemPool = gemPoolGo.AddComponent<GemPool>();
            SetField(gemPool, "_gemPrefab", _gemPrefab);

            var popPoolGo = new GameObject("PopPool");
            var popPool = popPoolGo.AddComponent<PopPool>();
            SetField(popPool, "_popPrefab", _popPrefab);

            // ---------- 粒子系统（Juice 的表现层） ----------
            var particlesRoot = new GameObject("JuiceParticles");
            var death = BuildParticle(particlesRoot.transform, "DeathBurst", 1f, simulationSpace: ParticleSystemSimulationSpace.World);
            var hit = BuildParticle(particlesRoot.transform, "HitSpark", 0.8f);
            var pickup = BuildParticle(particlesRoot.transform, "PickupSpark", 0.9f);
            var muzzle = BuildParticle(particlesRoot.transform, "MuzzleFlash", 1f);
            var ring = BuildRingParticle(particlesRoot.transform, "LevelUpRing");
            juice.BindParticles(death, hit, pickup, muzzle, ring);

            // ---------- UI ----------
            var canvas = CreateCanvas("Canvas_HUD", 0);
            BuildHud(canvas.transform);
            var levelUpPanel = BuildLevelUpPanel(canvas.transform);
            var gameOverPanel = BuildGameOverPanel(canvas.transform);
            var pausePanel = BuildPausePanel(canvas.transform);
            CreateEventSystem();

            // ---------- 引导器 ----------
            var bootstrapGo = new GameObject("GameSceneBootstrap");
            var bootstrap = bootstrapGo.AddComponent<GameSceneBootstrap>();
            SetField(bootstrap, "_cameraRig", cameraGo.GetComponent<CameraRig>());
            SetField(bootstrap, "_player", player.GetComponent<PlayerController>());
            SetField(bootstrap, "_pausePanel", pausePanel.GetComponent<PausePanel>());
            SetField(bootstrap, "_levelUpPanel", levelUpPanel.GetComponent<LevelUpPanel>());
            SetField(bootstrap, "_gameOverPanel", gameOverPanel.GetComponent<GameOverPanel>());
            SetField(bootstrap, "_hudCanvas", (RectTransform)canvas.transform);

            // 音频绑定（从 GameScene 单独进 Play 时也生效）
            BindAudio(audio);

            // 音量：压到合理范围
            EnsureDir("Assets/Scenes");
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, SceneGame);
            Debug.Log($"[Scene] GameScene 构建完成 → {SceneGame}");
        }

        /// <summary>玩家组装：刚体/碰撞/精灵/动画/控制器/武器/拾取圈。</summary>
        private static GameObject BuildPlayer()
        {
            var go = new GameObject("Player");
            go.tag = "Player";
            go.layer = LayerPlayer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var body = go.AddComponent<CircleCollider2D>(); // 实体碰撞：与敌人接触伤害
            body.radius = 0.42f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art("player_idle_0");
            sr.color = new Color(0.6f, 1f, 1f); // 青色调精灵
            sr.sortingOrder = 2;                // 玩家在敌人之上

            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = _playerController;

            var controller = go.AddComponent<PlayerController>();

            // 武器挂点（位置=玩家中心，武器不转体——弹丸自带方向）
            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(go.transform, false);
            var weapon = weaponGo.AddComponent<WeaponController>();

            // 拾取圈（触发器，半径随升级变化；tag 供宝石判定归属）
            var pickupGo = new GameObject("PickupArea");
            pickupGo.transform.SetParent(go.transform, false);
            pickupGo.tag = "Player";
            var pickup = pickupGo.AddComponent<CircleCollider2D>();
            pickup.isTrigger = true;
            pickup.radius = 2.2f;

            // 注入引用
            SetField(controller, "_rb", rb);
            SetField(controller, "_spriteRenderer", sr);
            SetField(controller, "_weapon", weapon);
            SetField(controller, "_pickupArea", pickup);
            SetField(controller, "_animator", animator);
            SetField(controller, "_startingWeapon", _weapon);
            SetField(weapon, "_player", controller);
            SetField(weapon, "_projectilePrefab", _projectilePrefab.GetComponent<Projectile>());
            return go;
        }

        /// <summary>三层视差星空（远中近）。</summary>
        private static void BuildBackground(Transform camParent)
        {
            var bg = new GameObject("Background");
            var parallax = bg.AddComponent<BackgroundParallax>();

            string[] sprites = { "star_bg_0", "star_bg_1", "star_bg_2" };
            float[] factors = { 0.08f, 0.2f, 0.42f };
            Vector2[] scales = { new Vector2(10f, 10f), new Vector2(7f, 7f), new Vector2(5f, 5f) };
            int[] orders = { -30, -20, -10 };

            var renderers = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var layerGo = new GameObject($"StarLayer_{i}");
                layerGo.transform.SetParent(bg.transform, false);
                var sr = layerGo.AddComponent<SpriteRenderer>();
                sr.sprite = Art(sprites[i]);
                sr.sortingOrder = orders[i];
                EnsureTextureRepeat(sprites[i]);
                renderers[i] = sr;
            }

            // 结构体数组用逐子字段方式写入（视差系数/缩放不可序列化成 Object 引用）
            SetStructArrayField(parallax, "_layers", (elem, i) =>
            {
                elem.FindPropertyRelative("renderer").objectReferenceValue = renderers[i];
                elem.FindPropertyRelative("followFactor").floatValue = factors[i];
                elem.FindPropertyRelative("scale").vector2Value = scales[i];
            }, 3);
        }

        /// <summary>星空贴图设为 Repeat 包裹（UV 偏移平铺的前提）。</summary>
        private static void EnsureTextureRepeat(string spriteName)
        {
            string path = $"{ArtDir}/{spriteName}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Bloom 后处理 Volume（HDR 精灵 + 强度超 1 的光，全靠它发光）。</summary>
        private static void CreateBloomVolume()
        {
            var volumeGo = new GameObject("PostVolume");
            volumeGo.transform.position = Vector3.zero;
            var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/PostVolume.asset");

            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.6f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.85f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.7f;

            // 轻微暗角：霓虹风格聚焦画面中心
            var vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.32f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;

            // 轻微色偏：电影感（不夸张）
            var colorAdj = profile.Add<ColorAdjustments>();
            colorAdj.active = true;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.1f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 8f;

            volume.sharedProfile = profile;
            AssetDatabase.SaveAssets();
        }

        /// <summary>音效绑定：程序化 WAV 按枚举顺序接线 + 环境音乐。</summary>
        private static void BindAudio(AudioService audio)
        {
            var clips = new List<AudioClip>
            {
                Sfx("sfx_shoot"), Sfx("sfx_hit"), Sfx("sfx_crit"), Sfx("sfx_kill"),
                Sfx("sfx_elitekill"), Sfx("sfx_pickup"), Sfx("sfx_levelup"), Sfx("sfx_hurt"),
                Sfx("sfx_gameover"), Sfx("sfx_click"),
            };
            var music = Sfx("music_ambient");
            audio.BindClips(clips, music);
        }
    }
}
