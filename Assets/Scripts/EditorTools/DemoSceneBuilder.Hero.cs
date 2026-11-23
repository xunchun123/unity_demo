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
            HeroAnimationBuilder.BuildAll(); // 像素帧 → 剪辑 → 状态机，预制体和武器 Kit 都要引用它
            CreateHeroPrefab();
            CreateMonsterPrefabs();
            CreateProjectilePrefabs();
            CreateWeaponKits();
            AssetDatabase.SaveAssets();
            Debug.Log("[HeroAssets] 勇者资产构建完成");
        }

        // ---------- 勇者 ----------

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

        /// <summary>勇者零摩擦材质：必须落盘成资产（内存材质保存预制体会悬空引用，Player 端丢失）。</summary>
        private static PhysicsMaterial2D HeroSlipMaterial()
        {
            const string matPath = "Assets/Physics/Hero_Slip.physicsMaterial2D";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(matPath);
            if (mat != null) return mat;
            EnsureDir("Assets/Physics");
            // Box2D 摩擦混合 = √(fA×fB)：勇者摩擦 0 时与任何地面相乘结果都是 0（无需合并模式属性）
            mat = new PhysicsMaterial2D("Hero_Slip") { friction = 0f, bounciness = 0f };
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
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
            // 零摩擦材质：自动试玩实证——持续推墙时墙壁摩擦力会把勇者"粘"在半空（静摩擦平衡，
            // vy=0 悬停不掉落），脚底探测又摸不到侧壁 → 接地失效+跳不起。零摩擦让推墙即下滑落地
            col.sharedMaterial = HeroSlipMaterial();
            // 视觉子物体：Animator / SpriteRenderer / 程序化形变都只作用在这里，根上的物理体不受影响
            // 像素美术：PPU 统一 32、pivot 在脚底中心，视觉层保持 1 倍缩放（非整数缩放会让像素粗细不均）。
            // 骑士身体约 40px = 1.25u，正好对上胶囊碰撞体高度
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = HeroAnimationBuilder.PreviewSprite(WeaponKind.Sword);
            sr.sortingOrder = 20;
            var animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = HeroAnimationBuilder.ControllerFor(WeaponKind.Sword);

            var hero = go.AddComponent<HeroController>();
            var pa = go.AddComponent<ProceduralAnim>();
            pa.pixelArtMode = true; // 帧动画已有呼吸/跑动起伏，程序化层只留轻微落地形变
            go.AddComponent<DustPuff>();       // 灰尘：落地 5 粒/起跳 3 粒/跑动 0.15s 一粒/冲刺沿途
            go.layer = LayerPlayer; // 近战掩码 1<<7 找的是 Enemy 层；勇者必须挂 Player 层（矩阵 Player↔Enemy 开）

            var origin = new GameObject("AttackOrigin");
            origin.transform.SetParent(go.transform, false);
            origin.transform.localPosition = new Vector3(0.55f, 0.6f, 0f);

            SetField(hero, "_rb", rb);
            SetField(hero, "_sr", sr); // 指向 Visual 子物体：无敌闪烁/残影都作用在它上面
            SetField(hero, "_animator", animator);
            SetField(hero, "_flatMaterial", FlatUnlitMaterial());
            SetField(hero, "_attackOrigin", origin.transform);
            SetField(hero, "_fireballPrefab", LoadPrefabComp<Fireball>("proj_fireball"));

            SavePrefab(go, path);
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
            EnemyAnimationBuilder.BuildAll();
            var boltFrames = PixelArtPipeline.LoadFrames("Fx", "MageBolt");
            var boltSprite = boltFrames.Length > 0 ? boltFrames[0] : null;

            // 碰撞体按像素身体量（PPU 32：1u = 32px），脚底在根原点；视觉层 1 倍缩放，像素粗细与勇者一致
            // 血量按"长剑几下打死"设计（勇者基础攻击 20，三连击 20/28/36）：
            //   地狱犬 90 ≈ 一套三连 + 补一刀；幽灵 60 ≈ 3 下（会飞，难打中，血少一点）；
            //   骷髅巫师 110 ≈ 4~5 下（远程，逼玩家贴脸）；梦魇马 420 ≈ 15 下；恶魔 1500 ≈ 50 下（三阶段）。
            //   普通怪每深入一关 +30% 血量，跟上勇者升级与天赋的成长。
            // 史莱姆 → 地狱犬：地面蹦跳，身体约 38×22px
            var slime = BuildMonsterBase("Slime", "Hound", new Vector2(0.9f, 0.6f), new Vector2(0f, 0.3f), 3.4f);
            var slimeAI = slime.AddComponent<StarVortex.Enemies.Slime>();
            WireEnemyVisuals(slimeAI, maxHp: 90f, hpBarWidth: 0.8f);
            SetField(slimeAI, "xpValue", 6);
            SavePrefab(slime, $"{HeroPrefabDir}/Slime.prefab");

            // 蝙蝠 → 幽灵：飞行怪，无重力（悬浮起伏由 Bat AI 的弹簧巡航负责）
            var bat = BuildMonsterBase("Bat", "Ghost", new Vector2(0.55f, 0.9f), new Vector2(0f, 0.75f), 0f);
            var batAI = bat.AddComponent<StarVortex.Enemies.Bat>();
            WireEnemyVisuals(batAI, maxHp: 60f, hpBarWidth: 0.7f);
            SetField(batAI, "xpValue", 6);
            SavePrefab(bat, $"{HeroPrefabDir}/Bat.prefab");

            // 骷髅法师 → 骷髅巫师：远程施法
            var mage = BuildMonsterBase("Mage", "Wizard", new Vector2(0.55f, 1.1f), new Vector2(0f, 0.55f), 3.4f);
            var mageAI = mage.AddComponent<StarVortex.Enemies.MageAI>();
            WireEnemyVisuals(mageAI, maxHp: 110f, hpBarWidth: 0.8f);
            SetField(mageAI, "xpValue", 9);
            SetField(mageAI, "_boltSprite", boltSprite);
            SavePrefab(mage, $"{HeroPrefabDir}/Mage.prefab");

            // 精英 → 梦魇马：身体约 70×55px（不含鬃毛火焰）
            var elite = BuildMonsterBase("EliteGuard", "Nightmare", new Vector2(1.9f, 1.6f), new Vector2(0f, 0.8f), 3.4f);
            var eliteAI = elite.AddComponent<StarVortex.Enemies.EliteGuard>();
            // 霸体：不硬直、难推动，只闪光抖动——精英的威胁来自"打不断"
            WireEnemyVisuals(eliteAI, maxHp: 420f, hpBarWidth: 1.6f, staggerTime: 0f, knockbackResist: 0.8f, deathFxScale: 1.6f);
            SetField(eliteAI, "xpValue", 30);
            SetField(eliteAI, "contactDamage", 18f);
            SavePrefab(elite, $"{HeroPrefabDir}/EliteGuard.prefab");

            // BOSS → 恶魔：斗篷身体约 50×85px，翅膀与吐息不计入碰撞
            var boss = BuildMonsterBase("BossKing", "Demon", new Vector2(1.6f, 2.6f), new Vector2(0f, 1.3f), 3.4f);
            var bossAI = boss.AddComponent<StarVortex.Enemies.BossKing>();
            // BOSS 血条在屏幕顶部（HUD），头顶不再画小血条；血量单独设计，不随关卡缩放
            WireEnemyVisuals(bossAI, maxHp: 1500f, hpBarWidth: 0f, staggerTime: 0f, knockbackResist: 0.95f,
                deathFxScale: 2.4f, hpPerLevel: 0f);
            SetField(bossAI, "xpValue", 100);
            SetField(bossAI, "contactDamage", 20f);
            SetField(bossAI, "comboReach", 2.4f);                       // 吐息从嘴部向前喷约 3u
            SetVector2Field(bossAI, "comboBoxSize", new Vector2(3f, 1.8f));
            SetField(bossAI, "_boltSprite", boltSprite);
            var slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPrefabDir}/Slime.prefab");
            if (slimePrefab != null)
                SetField(bossAI, "slimePrefab", slimePrefab.GetComponent<StarVortex.Enemies.Slime>());
            SavePrefab(boss, $"{HeroPrefabDir}/BossKing.prefab");
        }

        /// <summary>
        /// 怪物通用骨架：根上放物理（刚体 + 实体碰撞体 + 接触伤害 Trigger），
        /// Visual 子物体放 SpriteRenderer + Animator（像素帧，pivot 在脚底，1 倍缩放）。
        /// </summary>
        private static GameObject BuildMonsterBase(string name, string character, Vector2 colSize,
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
            //（两个实体 collider 之间不产生触发事件）
            var touch = go.AddComponent<BoxCollider2D>();
            touch.size = colSize * 1.1f;
            touch.offset = colOffset;
            touch.isTrigger = true;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = EnemyAnimationBuilder.PreviewSprite(character);
            sr.sortingOrder = 15;
            var animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = EnemyAnimationBuilder.ControllerFor(character);

            // 程序化动画只保留落地/受击的轻微形变（像素帧已经有呼吸与步态）
            var pa = go.AddComponent<StarVortex.Gameplay.ProceduralAnim>();
            pa.pixelArtMode = true;
            pa.hoverMode = gravity == 0f;
            pa.bobAmplitude = 0f; // 飞行怪的起伏由 AI 的弹簧巡航负责，不叠第二层
            return go;
        }

        /// <summary>给怪物 AI 接上 EnemyBase 的表现与数值字段：Animator、死亡爆散帧、血量、受击手感、血条。</summary>
        private static void WireEnemyVisuals(StarVortex.Enemies.EnemyBase ai, float maxHp, float hpBarWidth,
            float staggerTime = 0.22f, float knockbackResist = 0f, float deathFxScale = 1f, float hpPerLevel = 0.3f)
        {
            SetField(ai, "_anim", ai.GetComponentInChildren<Animator>());
            SetObjectArrayField(ai, "_deathFrames", PixelArtPipeline.LoadFrames("Fx", "Death"));
            SetField(ai, "_deathFxScale", deathFxScale);
            SetField(ai, "_flatMaterial", FlatUnlitMaterial());
            SetField(ai, "maxHp", maxHp);
            SetField(ai, "hpBarWidth", hpBarWidth);
            SetField(ai, "staggerTime", staggerTime);
            SetField(ai, "knockbackResist", knockbackResist);
            SetField(ai, "hpPerLevel", hpPerLevel);
        }

        /// <summary>
        /// 纯色剪影材质（GUI/Text Shader：只用贴图 alpha，颜色取顶点色，不受 2D 灯光影响）。
        /// 落盘成资产再引用：运行时 Shader.Find 找的着色器不一定被打进包，被材质引用的一定会。
        /// </summary>
        private static Material FlatUnlitMaterial()
        {
            const string path = "Assets/Materials/FlatUnlit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            EnsureDir("Assets/Materials");
            mat = new Material(Shader.Find("GUI/Text Shader")) { name = "FlatUnlit" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void SetVector2Field(Object target, string fieldName, Vector2 value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"[Setup] {target.GetType().Name} 上找不到字段 {fieldName}"); return; }
            prop.vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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

            // 火铳子弹（通用 Projectile 组件）：池键沿用 proj_arrow，贴图换成猎人素材自带的弹丸帧
            var arrow = BuildGenericProjectile("proj_arrow", "proj_arrow", new Vector2(0.5f, 0.1f));
            var bulletFrames = PixelArtPipeline.LoadFrames("Fx", "Bullet");
            if (bulletFrames.Length > 0) arrow.GetComponent<SpriteRenderer>().sprite = bulletFrames[0];
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
                kit.heroAnimator = HeroAnimationBuilder.ControllerFor(WeaponKind.Sword);
                kit.displayName = "长剑";
                kit.attackOrigin = new Vector2(0.55f, 0.75f); // 剑刃挥砍高度
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
                kit.heroAnimator = HeroAnimationBuilder.ControllerFor(WeaponKind.Staff);
                kit.displayName = "气功";
                kit.attackOrigin = new Vector2(1.1f, 0.9f);   // 出拳拳峰（Attack 第 2-4 帧量得）
                kit.description = "气功波免费连发，K 气爆新星耗蓝。强化树：气劲分裂 / 焦土气劲 / 双发气功 / 爆气扩容 / 冰霜新星 / 调息回气";
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
                kit.heroAnimator = HeroAnimationBuilder.ControllerFor(WeaponKind.Bow);
                kit.displayName = "火铳";
                kit.attackOrigin = new Vector2(0.8f, 1.2f);   // 枪口（Attack 帧最右像素）
                kit.description = "火铳点射可蓄力（强化解锁狙击）。强化树：穿甲弹 / 双管齐射 / 蓄力狙击 / 弹雨压制 / 后坐跃步 / 爆裂弹头";
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
