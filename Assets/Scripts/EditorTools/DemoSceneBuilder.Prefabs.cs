using System.IO;
using UnityEditor;
using UnityEngine;
using StarVortex.Data;
using StarVortex.Gameplay;
using TMPro;

namespace StarVortex.EditorTools
{
    /// <summary>一键搭建器（资产部分）：SO 数据资产 + 运行时预制体。</summary>
    public static partial class DemoSceneBuilder
    {
        private static WeaponData _weapon;
        private static EnemyData _drone, _darter, _brute;
        private static DifficultyCurve _difficulty;
        private static UpgradeData[] _upgrades;

        /// <summary>创建全部 ScriptableObject 资产（数据驱动：改平衡不进代码）。</summary>
        private static void CreateDataAssets()
        {
            EnsureDir(DataDir);

            // ---------- 武器 ----------
            _weapon = NewAsset<WeaponData>("Weapon_PulseBlaster");
            _weapon.displayName = "脉冲步枪";
            _weapon.description = "自动锁定最近的敌人射击";
            _weapon.fireInterval = 0.42f;
            _weapon.projectileCount = 1;
            _weapon.spreadAngle = 10f;
            _weapon.projectileSpeed = 15f;
            _weapon.projectileDamage = 12f;
            _weapon.pierce = 1;
            _weapon.projectileLifetime = 1.4f;
            _weapon.targetRadius = 10f;
            _weapon.projectileSprite = Art("projectile");
            _weapon.projectileColor = new Color(1f, 0.95f, 0.64f, 1f);
            _weapon.projectileEmission = new Color(1.9f, 1.7f, 0.7f, 1f); // HDR 超白，Bloom 发光

            // ---------- 三种敌人 ----------
            _drone = NewAsset<EnemyData>("Enemy_Drone");
            _drone.displayName = "浮游机";
            _drone.maxHp = 24f; _drone.contactDamage = 8f; _drone.contactCooldown = 0.8f;
            _drone.xpValue = 3; _drone.moveSpeed = 2.4f; _drone.separationRadius = 1.0f;
            _drone.radius = 0.42f; _drone.baseWeight = 1f;
            _drone.sprite = Art("enemy_drone_0");
            _drone.tintColor = new Color(1f, 0.4f, 0.42f, 1f);

            _darter = NewAsset<EnemyData>("Enemy_Darter");
            _darter.displayName = "冲锋者";
            _darter.maxHp = 16f; _darter.contactDamage = 12f; _darter.contactCooldown = 0.7f;
            _darter.xpValue = 4; _darter.moveSpeed = 4.2f; _darter.separationRadius = 0.8f;
            _darter.radius = 0.34f; _darter.baseWeight = 0.6f;
            _darter.sprite = Art("enemy_darter_0");
            _darter.tintColor = new Color(1f, 0.62f, 0.26f, 1f);

            _brute = NewAsset<EnemyData>("Enemy_Brute");
            _brute.displayName = "重装单位";
            _brute.maxHp = 90f; _brute.contactDamage = 18f; _brute.contactCooldown = 1.0f;
            _brute.xpValue = 10; _brute.moveSpeed = 1.5f; _brute.separationRadius = 1.2f;
            _brute.radius = 0.62f; _brute.baseWeight = 0.3f;
            _brute.sprite = Art("enemy_brute_0");
            _brute.tintColor = new Color(0.68f, 0.5f, 1f, 1f);

            // ---------- 难度曲线 ----------
            _difficulty = NewAsset<DifficultyCurve>("DifficultyCurve_Main");

            // ---------- 8 个升级项 ----------
            _upgrades = new[]
            {
                MakeUpgrade("Upgrade_Damage", "火力强化", "伤害提升 25%", UpgradeKind.DamageMul, 0.25f,
                    UpgradeRarity.Common, "icon_damage", new Color(1f, 0.5f, 0.5f, 1f)),
                MakeUpgrade("Upgrade_FireRate", "超频扳机", "攻击速度提升 20%", UpgradeKind.FireRateMul, 0.2f,
                    UpgradeRarity.Common, "icon_firerate", new Color(1f, 0.95f, 0.5f, 1f)),
                MakeUpgrade("Upgrade_Speed", "推进器", "移动速度提升 15%", UpgradeKind.MoveSpeedMul, 0.15f,
                    UpgradeRarity.Common, "icon_speed", new Color(0.5f, 1f, 0.85f, 1f)),
                MakeUpgrade("Upgrade_MaxHp", "装甲扩容", "最大生命值 +30 并立即回复", UpgradeKind.MaxHpAdd, 30f,
                    UpgradeRarity.Common, "icon_maxhp", new Color(1f, 0.45f, 0.6f, 1f)),
                MakeUpgrade("Upgrade_Pickup", "引力线圈", "拾取范围提升 40%", UpgradeKind.PickupRadiusMul, 0.4f,
                    UpgradeRarity.Common, "icon_pickup", new Color(0.65f, 0.8f, 1f, 1f)),
                MakeUpgrade("Upgrade_Multishot", "多弹幕", "弹幕数量 +1", UpgradeKind.ProjectileAdd, 1f,
                    UpgradeRarity.Epic, "icon_multishot", new Color(1f, 1f, 0.6f, 1f)),
                MakeUpgrade("Upgrade_Crit", "弱点扫描", "暴击率提升 10%（暴击造成双倍伤害）", UpgradeKind.CritChanceAdd, 0.1f,
                    UpgradeRarity.Rare, "icon_crit", new Color(1.8f, 1.3f, 0.5f, 1f)),
                MakeUpgrade("Upgrade_Regen", "纳米修复", "每秒回复 2 点生命", UpgradeKind.HpRegenAdd, 2f,
                    UpgradeRarity.Rare, "icon_regen", new Color(0.55f, 1f, 0.65f, 1f)),
            };

            AssetDatabase.SaveAssets();
        }

        private static UpgradeData MakeUpgrade(string fileName, string name, string desc,
            UpgradeKind kind, float value, UpgradeRarity rarity, string icon, Color iconColor)
        {
            var u = NewAsset<UpgradeData>(fileName);
            u.displayName = name;
            u.description = desc;
            u.kind = kind;
            u.value = value;
            u.rarity = rarity;
            u.icon = Art(icon);
            u.iconColor = iconColor;
            return u;
        }

        /// <summary>通用 SO 创建：已存在就载入旧的（幂等，保留用户改动）。</summary>
        private static T NewAsset<T>(string fileName) where T : ScriptableObject
        {
            string path = fileName.EndsWith(".asset")
                ? $"{DataDir}/{fileName}"
                : $"{DataDir}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------- 运行时预制体 ----------

        private static GameObject _projectilePrefab;
        private static GameObject _enemyPrefab;
        private static GameObject _gemPrefab;
        private static GameObject _popPrefab;

        private static void CreatePrefabs()
        {
            EnsureDir(PrefabDir);
            _projectilePrefab = BuildProjectilePrefab();
            _enemyPrefab = BuildEnemyPrefab();
            _gemPrefab = BuildGemPrefab();
            _popPrefab = BuildPopPrefab();
        }

        /// <summary>弹丸预制体：小刚体 + 触发器 + 拖尾（拖尾是弹幕"贵"感的廉价来源）。</summary>
        private static GameObject BuildProjectilePrefab()
        {
            var go = new GameObject("Projectile");
            go.layer = LayerPlayerBullet;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.11f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art("projectile");
            sr.color = _weapon.projectileColor;

            // 拖尾：细长渐隐，宽度与弹丸匹配
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = 0.16f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.08f;
            trail.material = NeonMaterial(_weapon.projectileEmission, additive: true);
            var tcm = new GradientColorKey[]
            {
                new(_weapon.projectileColor, 0f),
                new(new Color(_weapon.projectileColor.r, _weapon.projectileColor.g, _weapon.projectileColor.b, 0f), 1f),
            };
            trail.colorGradient = new Gradient { colorKeys = tcm, alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) } };
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;

            var proj = go.AddComponent<Projectile>();
            SetField(proj, "_rb", rb);
            SetField(proj, "_sr", sr);
            SetField(proj, "_trail", trail); // AddComponent 不触发 Reset() 自动抓引用，必须显式接线

            return SavePrefab(go, "Projectile");
        }

        /// <summary>敌人预制体：刚体 + 触发器 + 精灵 + 动画（动画控制器由 Anim 部分注入）。</summary>
        private static GameObject BuildEnemyPrefab()
        {
            var go = new GameObject("Enemy");
            go.layer = LayerEnemy;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.mass = 1f;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true; // 触发器形态：不参与物理互推，间距由分离力算法管
            col.radius = 0.5f;

            var child = new GameObject("Sprite");
            child.transform.SetParent(go.transform, false);
            child.layer = LayerEnemy;
            var sr = child.AddComponent<SpriteRenderer>();
            sr.sprite = Art("enemy_drone_0");
            sr.sortingOrder = 1;
            child.AddComponent<Animator>(); // 控制器在 Anim 阶段按类型分配

            go.AddComponent<Enemy>();
            return SavePrefab(go, "Enemy");
        }

        /// <summary>经验宝石预制体：触发器 + 精灵 + 微光晕子物体。</summary>
        private static GameObject BuildGemPrefab()
        {
            var go = new GameObject("XPGem");
            go.layer = LayerXpGem;
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.22f;

            var child = new GameObject("Sprite");
            child.transform.SetParent(go.transform, false);
            child.layer = LayerXpGem;
            var sr = child.AddComponent<SpriteRenderer>();
            sr.sprite = Art("xp_gem");
            sr.color = new Color(1f, 0.31f, 0.85f, 1f);

            // 光晕（Additive 贴图叠加，Bloom 后更"贵"）
            var halo = new GameObject("Halo");
            halo.transform.SetParent(child.transform, false);
            halo.layer = LayerXpGem;
            halo.transform.localScale = Vector3.one * 2.2f;
            var haloSr = halo.AddComponent<SpriteRenderer>();
            haloSr.sprite = Art("glow_disc");
            haloSr.material = NeonMaterial(new Color(1.2f, 0.3f, 1f, 0.35f), additive: true);
            haloSr.sortingOrder = -1;

            var gem = go.AddComponent<XPGem>();
            SetField(gem, "_sr", sr); // 精灵在子物体上，必须接线（漏了会 UnassignedReference）
            return SavePrefab(go, "XPGem");
        }

        /// <summary>伤害飘字预制体：世界空间 TMP 文本（显式中文字体，数字+暴击提示都能显示）。</summary>
        private static GameObject BuildPopPrefab()
        {
            var go = new GameObject("DamagePop");
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = "1";
            tmp.fontSize = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            if (_cjkFont != null) tmp.font = _cjkFont;

            var pop = go.AddComponent<DamagePop>();
            SetField(pop, "_label", tmp); // 不接线的话首次命中就 NRE，敌人永远死不了（冒烟测试抓出来的）
            return SavePrefab(go, "DamagePop");
        }

        // ---------- 材质助手 ----------

        /// <summary>
        /// 自写的 URP 加色混合 Unlit 着色器（发光体专用：拖尾/光晕/粒子）。
        /// 内置管线的 Particles/Standard Unlit 在 URP 下不可用（会渲染成洋红），
        /// 与其依赖 Graphics API 的兼容层，不如 30 行 HLSL 写明白——面试可讲。
        /// </summary>
        private const string AdditiveShaderSource = @"
Shader ""StarVortex/UnlitAdditive""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
        _Color (""Tint"", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { ""RenderType""=""Transparent"" ""Queue""=""Transparent"" ""RenderPipeline""=""UniversalPipeline"" ""IgnoreProjector""=""True"" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings  { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color * IN.color;
            }
            ENDHLSL
        }
    }
}";

        /// <summary>确保自写着色器已导入项目（幂等），返回其路径。</summary>
        private const string AdditiveShaderPath = "Assets/Settings/UnlitAdditive.shader";

        private static void EnsureAdditiveShader()
        {
            if (AssetDatabase.LoadAssetAtPath<Shader>(AdditiveShaderPath) != null) return;
            EnsureDir("Assets/Settings");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                AdditiveShaderPath.Replace('/', Path.DirectorySeparatorChar)), AdditiveShaderSource);
            AssetDatabase.ImportAsset(AdditiveShaderPath);
        }

        /// <summary>发光材质：Sprite 默认着色器（普通）或自写加色着色器（发光体）。</summary>
        internal static Material NeonMaterial(Color color, bool additive)
        {
            EnsureDir("Assets/Materials/Generated");
            string path = $"Assets/Materials/Generated/Mat_Glow_{ColorToHex(color)}_{(additive ? "Add" : "Norm")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            if (additive)
            {
                EnsureAdditiveShader();
                mat = new Material(Shader.Find("StarVortex/UnlitAdditive"));
                mat.SetColor("_Color", color);
            }
            else
            {
                mat = new Material(Shader.Find("Sprites/Default"));
                mat.SetColor("_Color", color);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static string ColorToHex(Color c) =>
            ColorUtility.ToHtmlStringRGBA(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a)));

        /// <summary>逐级创建目录（AssetDatabase.CreateFolder 不支持多级递归）。</summary>
        private static void EnsureDir(string path)
        {
            var parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
