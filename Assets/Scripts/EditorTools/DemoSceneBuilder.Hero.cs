using StarVortex.Core;
using StarVortex.Gameplay;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 《勇者远征》资产构建：勇者/怪物/弹丸预制体 + 三把武器 Kit（长剑/法杖/弓箭）。
    /// 面试点：怪物 prefab 与数值全在编辑器期程序化生成——平衡调整改这里重建即可，不进场景手摆。
    /// </summary>
    public static partial class DemoSceneBuilder
    {
        private const string HeroPrefabDir = "Assets/Prefabs/Hero";
        private const string HeroDataDir = "Assets/Data/Hero";

        [MenuItem("StarVortex/Hero/构建勇者资产", priority = 10)]
        public static void BuildHeroAssets()
        {
            NeonArtGenerator.GenerateFantasy();
            CreateHeroAssets();
        }

        /// <summary>GenerateAll 流程调用：先跑 GenerateFantasy 美术，再建预制体与武器资产。</summary>
        public static void CreateHeroAssets()
        {
            EnsureDir(HeroPrefabDir);
            EnsureDir(HeroDataDir);
            CreateHeroPrefab();
            CreateMonsterPrefabs();
            CreateProjectilePrefabs();
            CreateWeaponKits();
            AssetDatabase.SaveAssets();
            Debug.Log("[HeroAssets] 勇者资产构建完成");
        }

        // ---------- 勇者 ----------

        private static void CreateHeroPrefab()
        {
            string path = $"{HeroPrefabDir}/Hero.prefab";
            var go = new GameObject("Hero");
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.4f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.5f, 0.85f);
            col.offset = new Vector2(0f, 0.45f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art("hero_0");
            sr.sortingOrder = 20;
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildHeroAnimator();
            var hero = go.AddComponent<HeroController>();
            go.layer = LayerPlayer; // 近战掩码 1<<7 找的是 Enemy 层；勇者必须挂 Player 层（矩阵 Player↔Enemy 开）

            var origin = new GameObject("AttackOrigin");
            origin.transform.SetParent(go.transform, false);
            origin.transform.localPosition = new Vector3(0.45f, 0.5f, 0f);
            origin.transform.SetParent(go.transform, false);
            origin.transform.localPosition = new Vector3(0.45f, 0.5f, 0f);

            SetField(hero, "_rb", rb);
            SetField(hero, "_sr", sr);
            SetField(hero, "_attackOrigin", origin.transform);
            SetField(hero, "_animator", animator);
            SetField(hero, "_fireballPrefab", LoadPrefabComp<Fireball>("proj_fireball"));

            SavePrefab(go, path);
        }

        /// <summary>
        /// 勇者动画状态机（程序化创建，Animator 系统）：
        /// Idle/Run 两态由 "Moving" bool 驱动，剪辑动画化 localScale 呼吸/前倾，
        /// 占位美术是单帧，用变换动画让画面"活"——AI 帧图到位后可直接换剪辑。
        /// </summary>
        private static RuntimeAnimatorController BuildHeroAnimator()
        {
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(
                $"{HeroPrefabDir}/Hero.controller");
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            var root = controller.layers[0].stateMachine;

            var idleClip = new AnimationClip { name = "HeroIdle" };
            AnimationUtility.SetEditorCurve(idleClip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.y"),
                new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.6f, 1.03f), new Keyframe(1.2f, 1f)));

            var runClip = new AnimationClip { name = "HeroRun" };
            AnimationUtility.SetEditorCurve(runClip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.y"),
                new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.18f, 0.94f), new Keyframe(0.36f, 1f)));

            // 空中姿态：轻微前倾（localScaleZ 无效于 2D sprite，改用 y 压缩暗示"收腿"）
            var airClip = new AnimationClip { name = "HeroAir" };
            AnimationUtility.SetEditorCurve(airClip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.y"),
                new AnimationCurve(new Keyframe(0f, 0.92f), new Keyframe(0.3f, 0.96f)));

            // 攻击：快速前倾压出再弹回（0.18s 打击感一拍）
            var atkClip = new AnimationClip { name = "HeroAttack" };
            AnimationUtility.SetEditorCurve(atkClip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.y"),
                new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.06f, 0.88f), new Keyframe(0.18f, 1f)));
            AnimationUtility.SetEditorCurve(atkClip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x"),
                new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.06f, 1.12f), new Keyframe(0.18f, 1f)));

            AssetDatabase.AddObjectToAsset(idleClip, controller);
            AssetDatabase.AddObjectToAsset(runClip, controller);
            AssetDatabase.AddObjectToAsset(airClip, controller);
            AssetDatabase.AddObjectToAsset(atkClip, controller);

            var idle = root.AddState("Idle");
            idle.motion = idleClip;
            var run = root.AddState("Run");
            run.motion = runClip;
            var air = root.AddState("Airborne");
            air.motion = airClip;
            var atk = root.AddState("Attack");
            atk.motion = atkClip;
            root.defaultState = idle;

            var toRun = idle.AddTransition(run);
            toRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Moving");
            toRun.duration = 0.1f;
            var toIdle = run.AddTransition(idle);
            toIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, "Moving");
            toIdle.duration = 0.1f;

            // 空中优先级最高：任何态见 Airborne=true 立即切空中，落地回 Run/Idle
            foreach (var state in new[] { idle, run })
            {
                var toAir = state.AddTransition(air);
                toAir.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Airborne");
                toAir.duration = 0.05f;
                var toAtk = state.AddTransition(atk);
                toAtk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Attack");
                toAtk.duration = 0f;
                atk.AddExitTransition().duration = 0.08f;
            }

            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>注入 float 序列化字段（怪物数值 maxHp/contactDamage 用）。</summary>
        internal static void SetField(Object target, string fieldName, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 怪物五兄弟 ----------

        private static void CreateMonsterPrefabs()
        {
            // 史莱姆：地面蹦跳，重力由 prefab 设置
            var slime = BuildMonsterBase("Slime", "slime_0", new Vector2(0.5f, 0.45f), new Vector2(0f, 0.22f), 3.4f);
            slime.AddComponent<StarVortex.Enemies.Slime>();
            SavePrefab(slime, $"{HeroPrefabDir}/Slime.prefab");

            // 蝙蝠：飞行怪，无重力
            var bat = BuildMonsterBase("Bat", "bat_0", new Vector2(0.45f, 0.4f), Vector2.zero, 0f);
            bat.AddComponent<StarVortex.Enemies.Bat>();
            SavePrefab(bat, $"{HeroPrefabDir}/Bat.prefab");

            // 骷髅法师：远程施法
            var mage = BuildMonsterBase("Mage", "mage_0", new Vector2(0.5f, 0.8f), new Vector2(0f, 0.4f), 3.4f);
            mage.AddComponent<StarVortex.Enemies.MageAI>();
            SavePrefab(mage, $"{HeroPrefabDir}/Mage.prefab");

            // 精英·石像卫士
            var elite = BuildMonsterBase("EliteGuard", "elite_0", new Vector2(0.7f, 1.1f), new Vector2(0f, 0.55f), 3.4f);
            var eliteAI = elite.AddComponent<StarVortex.Enemies.EliteGuard>();
            SetField(eliteAI, "maxHp", 150f);
            SetField(eliteAI, "xpValue", 30);
            SetField(eliteAI, "contactDamage", 18f);
            SavePrefab(elite, $"{HeroPrefabDir}/EliteGuard.prefab");

            // BOSS·深渊魔王
            var boss = BuildMonsterBase("BossKing", "boss_0", new Vector2(0.9f, 1.5f), new Vector2(0f, 0.75f), 3.4f);
            var bossAI = boss.AddComponent<StarVortex.Enemies.BossKing>();
            SetField(bossAI, "maxHp", 600f);
            SetField(bossAI, "xpValue", 100);
            SetField(bossAI, "contactDamage", 20f);
            var slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/Slime.prefab");
            if (slimePrefab != null)
                SetField(bossAI, "slimePrefab", slimePrefab.GetComponent<StarVortex.Enemies.Slime>());
            SavePrefab(boss, $"{HeroPrefabDir}/BossKing.prefab");
        }

        private static GameObject BuildMonsterBase(string name, string spriteName, Vector2 colSize,
            Vector2 colOffset, float gravity)
        {
            var go = new GameObject(name);
            go.layer = LayerEnemy; // 怪物挂 Enemy 层：勇者近战掩码(1<<7)与弹丸矩阵(Projectile↔Enemy)都依赖它
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = gravity;
            rb.freezeRotation = true;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = colSize;
            col.offset = colOffset;
            // 接触伤害传感器（Trigger）：实体 collider 用于地面站立，Trigger 用于基类 OnTriggerStay2D
            //（两个实体 collider 之间不产生触发事件——怪物 AI 代理报告的集成风险 #1）
            var touch = go.AddComponent<BoxCollider2D>();
            touch.size = colSize * 1.1f;
            touch.offset = colOffset;
            touch.isTrigger = true;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art(spriteName);
            sr.sortingOrder = 15;
            return go;
        }

        // ---------- 弹丸三件套 ----------

        private static void CreateProjectilePrefabs()
        {
            // 火球（Fireball 组件：分裂/燃烧爽点分支）
            var fireball = new GameObject("proj_fireball");
            fireball.layer = LayerProjectile; // 与箭/剑气同为弹丸层，命中怪物的矩阵才开
            var fRb = fireball.AddComponent<Rigidbody2D>();
            fRb.gravityScale = 0f;
            fRb.freezeRotation = true;
            var fCol = fireball.AddComponent<CircleCollider2D>();
            fCol.radius = 0.16f;
            fCol.isTrigger = true;
            var fSr = fireball.AddComponent<SpriteRenderer>();
            fSr.sprite = Art("proj_fireball");
            fSr.sortingOrder = 12;
            var fb = fireball.AddComponent<Fireball>();
            SetField(fb, "_rb", fRb);
            SavePrefab(fireball, $"{HeroPrefabDir}/proj_fireball.prefab");

            // 箭（通用 Projectile 组件）
            var arrow = BuildGenericProjectile("proj_arrow", "proj_arrow", new Vector2(0.5f, 0.1f));
            SavePrefab(arrow, $"{HeroPrefabDir}/proj_arrow.prefab");

            // 剑气波
            var wave = BuildGenericProjectile("proj_swordwave", "proj_swordwave", new Vector2(0.5f, 0.5f));
            SavePrefab(wave, $"{HeroPrefabDir}/proj_swordwave.prefab");
        }

        private static GameObject BuildGenericProjectile(string name, string spriteName, Vector2 colSize)
        {
            var go = new GameObject(name);
            go.layer = LayerProjectile; // 弹丸层：矩阵 Projectile↔Enemy 开启，命中触发事件才有
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = colSize;
            col.isTrigger = true;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art(spriteName);
            sr.sortingOrder = 12;
            var proj = go.AddComponent<StarVortex.Gameplay.Projectile>();
            SetField(proj, "_rb", rb);
            SetField(proj, "_sr", sr);
            return go;
        }

        private static T LoadPrefabComp<T>(string key) where T : Component
        {
            var prefab = ProjectilePool.Get(key);
            if (prefab != null) return prefab.GetComponent<T>();
            var pathPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/{key}.prefab");
            return pathPrefab != null ? pathPrefab.GetComponent<T>() : null;
        }

        // ---------- 武器 Kit（选人界面三选一） ----------

        private static void CreateWeaponKits()
        {
            NewHeroAsset<WeaponKit>("Weapon_Sword", kit =>
            {
                kit.kind = WeaponKind.Sword;
                kit.displayName = "长剑";
                kit.description = "三段连击，末段击退。强化树：剑气冲击波 / 刀变长 / 旋风斩 / 震地斩 / 连击落雷 / 连击灼烧 / 嗜血";
                kit.attackInterval = 0.35f;
                kit.attackDamageMul = 1f;
                kit.boonPool = new[]
                {
                    BoonIds.SwordShockwave, BoonIds.SwordLong, BoonIds.SwordWhirlwind,
                    BoonIds.SwordQuakeSlam, BoonIds.SwordComboLightning, BoonIds.SwordLifesteal,
                    BoonIds.SwordComboBurn,
                };
            });

            NewHeroAsset<WeaponKit>("Weapon_Staff", kit =>
            {
                kit.kind = WeaponKind.Staff;
                kit.displayName = "法杖";
                kit.description = "奥术弹免费连射，K 秘法新星耗蓝。强化树：火球分裂 / 燃烧地面 / 双生火球 / 爆裂范围 / 冰霜新星 / 奥术涌流";
                kit.attackInterval = 0.45f;
                kit.attackDamageMul = 1.1f;
                kit.boonPool = new[]
                {
                    BoonIds.StaffFireballSplit, BoonIds.StaffBurningGround, BoonIds.StaffTwinBolt,
                    BoonIds.StaffBigBurst, BoonIds.StaffFrostNova, BoonIds.StaffArcaneFlow,
                };
            });

            NewHeroAsset<WeaponKit>("Weapon_Bow", kit =>
            {
                kit.kind = WeaponKind.Bow;
                kit.displayName = "弓箭";
                kit.description = "射箭可蓄力（爽点解锁狙击）。强化树：穿透+1 / 多重箭 / 蓄力狙击 / 箭雨 / 后撤步 / 爆裂箭头";
                kit.attackInterval = 0.5f;
                kit.attackDamageMul = 0.9f;
                kit.boonPool = new[]
                {
                    BoonIds.BowPiercePlus, BoonIds.BowMultishot, BoonIds.BowPowerShot,
                    BoonIds.BowArrowRain, BoonIds.BowBackstep, BoonIds.BowExplosive,
                };
            });
        }

        private static void NewHeroAsset<T>(string name, System.Action<T> fill) where T : ScriptableObject
        {
            string path = $"{HeroDataDir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            fill(asset);
            EditorUtility.SetDirty(asset); // 载入+改字段必须标脏才落盘（SetDirty 教训）

            // 创建自检：m_Script {fileID:0}（类绑定丢失）会让 LoadAssetAtPath 永远 null，
            // UI 拿到空 Kit 数组、卡片永远占位文案（实测坑：删除资产重建即恢复）
            var verify = AssetDatabase.LoadAssetAtPath<T>(path);
            if (verify == null)
                Debug.LogError($"[HeroAssets] {path} 创建后无法加载——m_Script 绑定丢失，删除资产后重跑");
        }

        private static void SavePrefab(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }
    }
}
