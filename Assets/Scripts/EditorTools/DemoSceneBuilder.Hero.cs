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

        /// <summary>比例网格基准：勇者目标身高（世界单位）。怪物按 0.7~2.6u 分档，相机视口 7u。</summary>
        internal const float HeroTargetHeight = 1.25f;

        /// <summary>
        /// 把视觉子物体缩放到目标身高并把"脚底"钉在根原点：精灵 pivot 是中心（脚在 sprite 下缘），
        /// 不做偏移的话脚会插进地面半个身位。缩放系数按 sprite.bounds 实测——不同画布尺寸的帧
        /// 自动归一，逐帧换装也不会跳尺寸。
        /// </summary>
        private static void ApplyVisualFeet(Transform visual, Sprite sprite, float targetHeight)
        {
            if (sprite == null) return; // Art() 缺图时已有告警，保持默认缩放即可
            var b = sprite.bounds;
            float h = 2f * b.extents.y;
            if (h <= 0f) return;
            float s = targetHeight / h;
            visual.localScale = new Vector3(s, s, 1f);
            visual.localPosition = new Vector3(0f, b.extents.y * s, 0f); // feet → y=0
        }

        private static void CreateHeroPrefab()
        {
            string path = $"{HeroPrefabDir}/Hero.prefab";
            var go = new GameObject("Hero");
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.4f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // 比例网格：勇者目标身高 1.25u；胶囊略矮于视觉（头顶超出判定盒）——横版惯例的小受击体
            var col = go.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.55f, 1.1f);
            col.offset = new Vector2(0f, 0.58f);
            // 视觉子物体：ProceduralAnim 的 squash/stretch/前倾只捏这个 transform 的 scale/rotation/局部位置，
            // 根上的 Rigidbody2D/碰撞体不受影响（直接缩放根会连物理体一起捏变形）
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = Art("hero_idle_0"); // idle=收招站姿帧（与全套动画同批次，光影统一）
            sr.sortingOrder = 20;
            ApplyVisualFeet(visual.transform, sr.sprite, HeroTargetHeight);
            // 不挂 Animator：自动试玩实证——程序化 PPtr 换帧曲线在 Player 端确实生效，但攻击状态
            // 播完会卡在末帧持续覆盖 SpriteRenderer（"过一会儿变贴纸"的元凶）。勇者动画由
            // HeroController.DriveFrameAnimation 代码驱动，单一数据源不留第二条写 sprite 的路
            var hero = go.AddComponent<HeroController>();
            go.AddComponent<ProceduralAnim>(); // 程序化动画：跑动弹跳/呼吸/跳跃拉伸落地压扁/攻击 punch
            go.AddComponent<DustPuff>();       // 灰尘：落地 5 粒/起跳 3 粒/跑动 0.15s 一粒/冲刺沿途
            go.layer = LayerPlayer; // 近战掩码 1<<7 找的是 Enemy 层；勇者必须挂 Player 层（矩阵 Player↔Enemy 开）

            var origin = new GameObject("AttackOrigin");
            origin.transform.SetParent(go.transform, false);
            origin.transform.localPosition = new Vector3(0.55f, 0.6f, 0f);

            SetField(hero, "_rb", rb);
            SetField(hero, "_sr", sr); // 指向 Visual 子物体：无敌闪烁/残影都作用在它上面
            SetField(hero, "_attackOrigin", origin.transform);
            SetField(hero, "_fireballPrefab", LoadPrefabComp<Fireball>("proj_fireball"));

            // 真帧动画帧组（用户 AI 生图，代码驱动换帧——Animator PPtr 在 Player 端不生效的兜底，见 DriveFrameAnimation）
            SetObjectArrayField(hero, "_idleFrames", new Object[] { Art("hero_idle_0") });
            SetObjectArrayField(hero, "_runFrames", new Object[]
            {
                Art("hero_run_0"), Art("hero_run_1"), Art("hero_run_2"), Art("hero_run_1"),
            });
            SetObjectArrayField(hero, "_jumpFrames", new Object[] { Art("hero_jump_0") });
            SetObjectArrayField(hero, "_fallFrames", new Object[] { Art("hero_jump_1") });
            SetObjectArrayField(hero, "_attackFrames", new Object[]
            {
                Art("hero_attack_0"), Art("hero_attack_1"), Art("hero_attack_2"),
            });

            SavePrefab(go, path);
        }

        /// <summary>
        /// 勇者动画状态机（程序化创建，Animator 系统）——真帧动画版：
        /// 用户 AI 生图批次二提供 9 帧（跑步三帧+攻击三帧+跳跃两帧+站姿），按 0.12s/帧换帧：
        /// Idle=站姿单帧（呼吸由 ProceduralAnim 叠加）→ Run=迈步A→收拢→迈步B→收拢 四拍循环；
        /// Jump（上升收腿）/Fall（下落展腿）由 Airborne+Falling 双 bool 驱动；Attack=蓄力→突刺→收招。
        /// 换帧曲线绑定 "Visual" 子物体的 SpriteRenderer.m_Sprite——根上的物理体不受影响；
        /// squash/stretch/前倾继续由 ProceduralAnim 在 LateUpdate 叠加（两套驱动互补）。
        /// </summary>
        private static RuntimeAnimatorController BuildHeroAnimator()
        {
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(
                $"{HeroPrefabDir}/Hero.controller");
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Falling", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            var root = controller.layers[0].stateMachine;

            const float frameTime = 0.12f;
            var idleClip = SpriteSwapClip("HeroIdle", new[] { "hero_idle_0" }, new[] { 0f, 1f }, loop: true);
            var runClip = SpriteSwapClip("HeroRun",
                new[] { "hero_run_0", "hero_run_1", "hero_run_2", "hero_run_1" },
                new[] { 0f, frameTime, frameTime * 2, frameTime * 3, frameTime * 4 }, loop: true);
            var jumpClip = SpriteSwapClip("HeroJump", new[] { "hero_jump_0" }, new[] { 0f, 0.5f }, loop: true);
            var fallClip = SpriteSwapClip("HeroFall", new[] { "hero_jump_1" }, new[] { 0f, 0.5f }, loop: true);
            // 攻击三帧：蓄力 0.12s → 突刺 0.12s → 收招停留到 0.4s（与 0.35s 攻击间隔基本对齐）
            var atkClip = SpriteSwapClip("HeroAttack",
                new[] { "hero_attack_0", "hero_attack_1", "hero_attack_2", "hero_attack_2" },
                new[] { 0f, frameTime, frameTime * 2, 0.4f }, loop: false);

            AssetDatabase.AddObjectToAsset(idleClip, controller);
            AssetDatabase.AddObjectToAsset(runClip, controller);
            AssetDatabase.AddObjectToAsset(jumpClip, controller);
            AssetDatabase.AddObjectToAsset(fallClip, controller);
            AssetDatabase.AddObjectToAsset(atkClip, controller);

            var idle = root.AddState("Idle");
            idle.motion = idleClip;
            var run = root.AddState("Run");
            run.motion = runClip;
            var jump = root.AddState("Jump");
            jump.motion = jumpClip;
            var fall = root.AddState("Fall");
            fall.motion = fallClip;
            var atk = root.AddState("Attack");
            atk.motion = atkClip;
            root.defaultState = idle;

            // Attack 触发过渡最先添加（同帧多条件满足时 Unity 按添加顺序取第一个命中的）
            foreach (var state in new[] { idle, run, jump, fall })
            {
                var toAtk = state.AddTransition(atk);
                toAtk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "Attack");
                toAtk.duration = 0f;
            }
            atk.AddExitTransition().duration = 0.08f;

            // 地面两态互换
            LinkStates(idle, run, "Moving", UnityEditor.Animations.AnimatorConditionMode.If, 0.1f);
            LinkStates(run, idle, "Moving", UnityEditor.Animations.AnimatorConditionMode.IfNot, 0.1f);

            // 地面 → 空中（起跳收腿 / 直接下落展腿）
            foreach (var state in new[] { idle, run })
            {
                LinkStates(state, jump, "Airborne", UnityEditor.Animations.AnimatorConditionMode.If, 0.05f,
                    ("Falling", UnityEditor.Animations.AnimatorConditionMode.IfNot));
                LinkStates(state, fall, "Airborne", UnityEditor.Animations.AnimatorConditionMode.If, 0.05f,
                    ("Falling", UnityEditor.Animations.AnimatorConditionMode.If));
            }

            // 空中升降互换
            LinkStates(jump, fall, "Falling", UnityEditor.Animations.AnimatorConditionMode.If, 0.05f);
            LinkStates(fall, jump, "Falling", UnityEditor.Animations.AnimatorConditionMode.IfNot, 0.05f);

            // 落地回地面态（Airborne=false 后按 Moving 分流）
            foreach (var air in new[] { jump, fall })
            {
                LinkStates(air, idle, "Airborne", UnityEditor.Animations.AnimatorConditionMode.IfNot, 0.1f,
                    ("Moving", UnityEditor.Animations.AnimatorConditionMode.IfNot));
                LinkStates(air, run, "Airborne", UnityEditor.Animations.AnimatorConditionMode.IfNot, 0.1f,
                    ("Moving", UnityEditor.Animations.AnimatorConditionMode.If));
            }

            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>换帧剪辑：ObjectReference 曲线驱动 SpriteRenderer.m_Sprite（times 末位=剪辑总长，非循环靠它停留末帧）。</summary>
        private static AnimationClip SpriteSwapClip(string name, string[] sprites, float[] times, bool loop)
        {
            var clip = new AnimationClip { name = name };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var frames = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                frames[i] = new ObjectReferenceKeyframe { time = times[i], value = Art(sprites[i]) };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Visual", typeof(SpriteRenderer), "m_Sprite"), frames);
            return clip;
        }

        /// <summary>状态过渡接线（支持两个条件：主条件 + 可选附加条件）。</summary>
        private static void LinkStates(UnityEditor.Animations.AnimatorState from,
            UnityEditor.Animations.AnimatorState to, string param, UnityEditor.Animations.AnimatorConditionMode mode,
            float duration, (string name, UnityEditor.Animations.AnimatorConditionMode mode)? extra = null)
        {
            var t = from.AddTransition(to);
            t.AddCondition(mode, 0, param);
            if (extra.HasValue) t.AddCondition(extra.Value.mode, 0, extra.Value.name);
            t.duration = duration;
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

        /// <summary>注入 Object 引用序列化字段（如 Bat 扑扇循环的第二帧精灵）。</summary>
        internal static void SetObjectField(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 怪物五兄弟 ----------

        private static void CreateMonsterPrefabs()
        {
            // 比例网格（勇者 1.25u）：史莱姆 0.8 / 蝙蝠 0.7 / 法师 1.1 / 精英 1.7 / BOSS 2.6
            // 史莱姆：地面蹦跳，重力由 prefab 设置
            var slime = BuildMonsterBase("Slime", "slime_0", new Vector2(0.6f, 0.5f), new Vector2(0f, 0.25f), 3.4f,
                targetHeight: 0.8f);
            slime.AddComponent<StarVortex.Enemies.Slime>();
            SavePrefab(slime, $"{HeroPrefabDir}/Slime.prefab");

            // 蝙蝠：飞行怪，无重力（hover 模式：悬浮 bob + 两帧扑扇）
            var bat = BuildMonsterBase("Bat", "bat_0", new Vector2(0.45f, 0.35f), Vector2.zero, 0f, hover: true,
                targetHeight: 0.7f);
            var batAI = bat.AddComponent<StarVortex.Enemies.Bat>();
            SetObjectField(batAI, "_frameB", Art("bat_1")); // 扑扇第二帧（缺图时 Bat 自己跳过切换）
            SavePrefab(bat, $"{HeroPrefabDir}/Bat.prefab");

            // 骷髅法师：远程施法
            var mage = BuildMonsterBase("Mage", "mage_0", new Vector2(0.55f, 0.9f), new Vector2(0f, 0.45f), 3.4f,
                targetHeight: 1.1f);
            mage.AddComponent<StarVortex.Enemies.MageAI>();
            SavePrefab(mage, $"{HeroPrefabDir}/Mage.prefab");

            // 精英·石像卫士
            var elite = BuildMonsterBase("EliteGuard", "elite_0", new Vector2(0.8f, 1.5f), new Vector2(0f, 0.75f), 3.4f,
                targetHeight: 1.7f);
            var eliteAI = elite.AddComponent<StarVortex.Enemies.EliteGuard>();
            SetField(eliteAI, "maxHp", 150f);
            SetField(eliteAI, "xpValue", 30);
            SetField(eliteAI, "contactDamage", 18f);
            SavePrefab(elite, $"{HeroPrefabDir}/EliteGuard.prefab");

            // BOSS·深渊魔王
            var boss = BuildMonsterBase("BossKing", "boss_0", new Vector2(1.1f, 2.3f), new Vector2(0f, 1.15f), 3.4f,
                targetHeight: 2.6f);
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
            Vector2 colOffset, float gravity, bool hover = false, float targetHeight = 1f)
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
            // 视觉子物体：ProceduralAnim 只捏这个 transform；各怪 AI 在根 transform 上写的
            // 压扁/拉伸 telegraph（蓄力/瞄准/眩晕）与之相乘互不覆盖，怪也告别"贴纸平移"
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = Art(spriteName);
            sr.sortingOrder = 15;
            ApplyVisualFeet(visual.transform, sr.sprite, targetHeight); // 比例网格：按目标身高缩放+脚底钉根原点
            // 程序化动画：地面怪跑动弹跳+呼吸+空中拉伸，飞行怪（hover）悬浮 bob
            var pa = go.AddComponent<StarVortex.Gameplay.ProceduralAnim>();
            pa.hoverMode = hover;
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
