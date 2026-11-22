using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 星涡幸存者 StarVortex —— 程序化霓虹矢量美术生成器（Editor 工具）。
    ///
    /// 菜单：StarVortex/生成美术资源
    /// 输出：Assets/Art/Generated/*.png（覆盖式写入，可重复执行，固定随机种子结果稳定）
    /// 风格：霓虹矢量 —— 深底高亮、边缘发光、几何干净。全部图像由内置软件光栅器
    ///       以「符号距离场（SDF）+ smoothstep 羽化」逐像素绘制，不下载任何外部素材。
    /// 说明：PNG 保持 LDR；需要 HDR 提亮的部分请在运行时用 ColorUsage(HDR) 材质色实现。
    /// </summary>
    public static class NeonArtGenerator
    {
        // ------------------------------------------------------------------
        // 调色板（const 十六进制常量，统一在此维护）
        // ------------------------------------------------------------------
        public const string HexBackground = "#0B0E1A"; // 背景深蓝黑
        public const string HexPlayer     = "#40E0FF"; // 玩家 霓虹青（描边）
        public const string HexPlayerCore = "#CFF6FF"; // 玩家 发光核心
        public const string HexDrone      = "#FF5A5A"; // 敌A 浮游drone 珊瑚红
        public const string HexDroneCore  = "#FFD1D1";
        public const string HexDarter     = "#FF9F43"; // 敌B 冲锋darter 橙
        public const string HexDarterCore = "#FFE2C2";
        public const string HexBrute      = "#9B6BFF"; // 敌C 坦克brute 紫
        public const string HexBruteCore  = "#E2D6FF";
        public const string HexGem        = "#FF4FD8"; // 经验宝石 品红
        public const string HexGemCore    = "#FFD9F6";
        public const string HexBullet     = "#FFF3A3"; // 弹丸 暖黄白
        public const string HexBulletCore = "#FFFFFF"; // 弹丸核心纯白
        public const string HexIconWhite  = "#F4F8FF"; // UI 图标白线（略偏冷）

        // 输出目录与统一 PPU
        const string OutputRelDir = "Assets/Art/Generated";
        const float PixelsPerUnit = 100f;

        // 运行时颜色（由 const 十六进制解析一次，避免每像素重复解析）
        static class Pal
        {
            public static readonly Color Background = FromHex(HexBackground);
            public static readonly Color Player = FromHex(HexPlayer);
            public static readonly Color PlayerCore = FromHex(HexPlayerCore);
            public static readonly Color Drone = FromHex(HexDrone);
            public static readonly Color DroneCore = FromHex(HexDroneCore);
            public static readonly Color Darter = FromHex(HexDarter);
            public static readonly Color DarterCore = FromHex(HexDarterCore);
            public static readonly Color Brute = FromHex(HexBrute);
            public static readonly Color BruteCore = FromHex(HexBruteCore);
            public static readonly Color Gem = FromHex(HexGem);
            public static readonly Color GemCore = FromHex(HexGemCore);
            public static readonly Color Bullet = FromHex(HexBullet);
            public static readonly Color BulletCore = FromHex(HexBulletCore);
            public static readonly Color IconWhite = FromHex(HexIconWhite);

            static Color FromHex(string hex)
            {
                if (ColorUtility.TryParseHtmlString(hex, out Color c))
                    return c;
                return Color.magenta; // 解析失败用显眼色，便于发现配置错误
            }
        }

        /// <summary>菜单入口：一次性生成全部美术资源。</summary>
        [MenuItem("StarVortex/生成美术资源")]
        public static void GenerateAll()
        {
            int count = 0;
            string absDir = Path.Combine(Application.dataPath, "Art", "Generated");
            if (!Directory.Exists(absDir))
                Directory.CreateDirectory(absDir);

            // ---- 角色 ----
            for (int f = 0; f < 4; f++) Emit(BuildPlayer(f), absDir, $"player_idle_{f}", ref count);
            for (int f = 0; f < 2; f++) Emit(BuildDrone(f), absDir, $"enemy_drone_{f}", ref count);
            for (int f = 0; f < 2; f++) Emit(BuildDarter(f), absDir, $"enemy_darter_{f}", ref count);
            for (int f = 0; f < 2; f++) Emit(BuildBrute(f), absDir, $"enemy_brute_{f}", ref count);

            // ---- 战斗 / 拾取 ----
            Emit(BuildProjectile(), absDir, "projectile", ref count);
            Emit(BuildXpGem(), absDir, "xp_gem", ref count);

            // ---- 粒子 / 特效 ----
            Emit(BuildParticleSoft(), absDir, "particle_soft", ref count);
            Emit(BuildParticleSpark(), absDir, "particle_spark", ref count);
            Emit(BuildRingShockwave(), absDir, "ring_shockwave", ref count);
            Emit(BuildGlowDisc(), absDir, "glow_disc", ref count);

            // ---- UI 图标 ----
            Emit(BuildIconDamage(), absDir, "icon_damage", ref count);
            Emit(BuildIconFirerate(), absDir, "icon_firerate", ref count);
            Emit(BuildIconSpeed(), absDir, "icon_speed", ref count);
            Emit(BuildIconMaxHp(), absDir, "icon_maxhp", ref count);
            Emit(BuildIconPickup(), absDir, "icon_pickup", ref count);
            Emit(BuildIconMultishot(), absDir, "icon_multishot", ref count);
            Emit(BuildIconCrit(), absDir, "icon_crit", ref count);
            Emit(BuildIconRegen(), absDir, "icon_regen", ref count);

            // ---- 场景 / 后期 ----
            Emit(BuildVignette(), absDir, "vignette", ref count);
            // 三层视差星空：远层小星稀疏低亮 → 近层大星带十字光
            Emit(BuildStarLayer(11, 60, 0.7f, 1.4f, 0.30f, 0.55f, false), absDir, "star_bg_0", ref count);
            Emit(BuildStarLayer(23, 40, 1.1f, 2.2f, 0.55f, 0.85f, false), absDir, "star_bg_1", ref count);
            Emit(BuildStarLayer(37, 25, 2.0f, 3.4f, 0.85f, 1.00f, true), absDir, "star_bg_2", ref count);
            Emit(BuildNebula(101, Pal.Player), absDir, "nebula_blob_0", ref count);
            Emit(BuildNebula(202, Pal.Brute), absDir, "nebula_blob_1", ref count);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NeonArt] 生成 {count} 张精灵 → Assets/Art/Generated");
        }

        /// <summary>写盘 + 首次导入 + 统一导入参数（直接覆盖旧文件，幂等）。</summary>
        static void Emit(Raster canvas, string absDir, string name, ref int count)
        {
            string relPath = $"{OutputRelDir}/{name}.png";
            canvas.SavePng(Path.Combine(absDir, name + ".png"));
            AssetDatabase.ImportAsset(relPath); // 先导入占位
            ApplySpriteImport(relPath);         // 再统一设置导入参数并重导入
            count++;
        }

        /// <summary>统一导入参数（Unity 2022.3 TextureImporter API）。</summary>
        static void ApplySpriteImport(string relPath)
        {
            var importer = AssetImporter.GetAtPath(relPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[NeonArt] 无法获取 TextureImporter: " + relPath);
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.isReadable = false;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteMeshType = SpriteMeshType.FullRect; // 光晕类精灵避免紧缩网格裁切
            importer.SaveAndReimport();
        }

        // ==================================================================
        // 精灵构建
        // ==================================================================

        // 玩家：圆润三角飞船（机头朝上，描边2px 青）。
        // 4 帧 = 核心光点大小 0.9/1.0/1.1/1.0 呼吸 + 尾部引擎火苗长短变化。
        static Raster BuildPlayer(int frame)
        {
            var c = new Raster(96, 96);
            Color edge = Pal.Player;
            Color core = Pal.PlayerCore;
            float[] coreScale = { 0.9f, 1.0f, 1.1f, 1.0f };
            float[] flameLen = { 10f, 14f, 12f, 17f };
            float s = coreScale[frame];

            // 机身轮廓：圆角三角（二次贝塞尔圆角）
            Vector2[] tri = { new Vector2(48f, 88f), new Vector2(16f, 22f), new Vector2(80f, 22f) };
            Vector2[] hull = RoundedPath(tri, new float[] { 14f, 10f, 10f }, 6);
            Vector2 centroid = new Vector2(48f, 44f);

            // 环境微光
            Color amb = edge; amb.a = 0.10f;
            c.DrawRadialFade(48f, 48f, 46f, amb, 2f);

            // 引擎火苗（机尾下方，先画、压在机身后面；长短随帧变化）
            float fl = flameLen[frame];
            float baseY = 24f;
            Vector2[] flame =
            {
                new Vector2(48f, baseY + 2.5f),
                new Vector2(53f, baseY + 0.5f),
                new Vector2(48f, baseY - fl),
                new Vector2(43f, baseY + 0.5f)
            };
            Color fGlow = edge; fGlow.a = 0.30f;
            c.DrawRadialFade(48f, baseY - fl * 0.4f, fl * 1.2f, fGlow, 2f);
            Color fFill = core; fFill.a = 0.90f;
            c.FillPolygon(flame, fFill, 0.8f);
            Vector2[] flameInner =
            {
                new Vector2(48f, baseY + 1f),
                new Vector2(50.5f, baseY + 0.2f),
                new Vector2(48f, baseY - fl * 0.65f),
                new Vector2(45.5f, baseY + 0.2f)
            };
            Color fInner = Color.white; fInner.a = 0.80f;
            c.FillPolygon(flameInner, fInner, 0.8f);

            // 机身：微填充 + 霓虹描边（宽软晕打底，再叠 2px 主线）
            Color hullFill = edge; hullFill.a = 0.10f;
            c.FillPolygon(hull, hullFill);
            Color rim = edge; rim.a = 0.30f;
            c.StrokePolygon(hull, 5f, rim);
            c.StrokePolygon(hull, 2f, edge);

            // 内部轮廓细节线
            var inner = new Vector2[hull.Length];
            for (int i = 0; i < hull.Length; i++)
                inner[i] = centroid + (hull[i] - centroid) * 0.55f;
            Color innerC = edge; innerC.a = 0.35f;
            c.StrokePolygon(inner, 1.2f, innerC);

            // 发光核心（呼吸）
            float coreR = 6.5f * s;
            c.DrawGlow(48f, 46f, coreR * 2.6f, edge); // 同色低 alpha 多层同心
            Color coreGlow = core; coreGlow.a = 0.55f;
            c.DrawRadialFade(48f, 46f, coreR * 2.2f, coreGlow, 2f);
            c.DrawDisc(48f, 46f, coreR, core);
            Color dot = Color.white; dot.a = 0.95f;
            c.DrawDisc(48f, 46f, coreR * 0.45f, dot);
            return c;
        }

        // 敌A 浮游drone：圆形浮游体（描边红）+ 中心单眼亮点。
        // 2 帧 = 瞳孔位置偏移。
        static Raster BuildDrone(int frame)
        {
            var c = new Raster(64, 64);
            Color edge = Pal.Drone;
            Color core = Pal.DroneCore;
            float cx = 32f, cy = 33f, r = 23f;

            Color amb = edge; amb.a = 0.10f;
            c.DrawRadialFade(cx, cy, 31f, amb, 2f);

            // 主体：暗色微填充 + 软晕描边 + 2px 主线 + 内圈细节
            Color body = edge; body.a = 0.12f;
            c.DrawDisc(cx, cy, r, body);
            Color rim = edge; rim.a = 0.30f;
            c.DrawRing(cx, cy, r, 5f, rim);
            c.DrawRing(cx, cy, r, 2f, edge);
            Color detail = edge; detail.a = 0.30f;
            c.DrawRing(cx, cy, r - 6f, 1f, detail);

            // 单眼：暗色眼窝 + 亮色虹膜 + 红瞳孔 + 白色高光点
            Color socket = Pal.Background; socket.a = 0.90f;
            c.DrawDisc(cx, cy, 9.5f, socket);
            c.DrawGlow(cx, cy, 9f, core);
            c.DrawDisc(cx, cy, 6f, core);
            float ox = frame == 0 ? -1.8f : 1.8f;
            float oy = frame == 0 ? 1.2f : -1.2f;
            c.DrawDisc(cx + ox, cy + oy, 2.6f, edge);
            Color glint = Color.white; glint.a = 0.90f;
            c.DrawDisc(cx + ox + 0.9f, cy + oy + 0.9f, 0.9f, glint);
            return c;
        }

        // 敌B 冲锋darter：细长箭头形（朝右，描边橙）。
        // 2 帧 = 尾鳍张合角度变化。
        static Raster BuildDarter(int frame)
        {
            var c = new Raster(64, 64);
            Color edge = Pal.Darter;
            Color core = Pal.DarterCore;
            float finDeg = frame == 0 ? 20f : 45f;

            // 尾鳍（先画，压在身体后面）
            DrawDartFin(c, 28f, 36.5f, finDeg, 15f, edge);
            DrawDartFin(c, 28f, 27.5f, -finDeg, 15f, edge);

            // 身体：细长箭头（凹尾双叉）
            Vector2[] body =
            {
                new Vector2(57f, 32f),
                new Vector2(14f, 40f),
                new Vector2(25f, 32f),
                new Vector2(14f, 24f)
            };
            Vector2[] hull = RoundedPath(body, new float[] { 5f, 4f, 3f, 4f }, 5);
            Color bodyFill = edge; bodyFill.a = 0.12f;
            c.FillPolygon(hull, bodyFill);
            Color rim = edge; rim.a = 0.30f;
            c.StrokePolygon(hull, 5f, rim);
            c.StrokePolygon(hull, 2f, edge);

            // 头部亮芯
            c.DrawGlow(46f, 32f, 7f, core);
            c.DrawDisc(46f, 32f, 3.2f, core);
            return c;
        }

        // darter 尾鳍：薄三角，deg 为后掠角（正 = 向上展开，负 = 向下）
        static void DrawDartFin(Raster c, float pivotX, float pivotY, float deg, float len, Color col)
        {
            float rad = deg * Mathf.Deg2Rad;
            Vector2 tip = new Vector2(pivotX - Mathf.Cos(rad) * len, pivotY + Mathf.Sin(rad) * len);
            Vector2[] fin =
            {
                new Vector2(pivotX + 7f, pivotY),
                new Vector2(pivotX - 3f, pivotY),
                tip
            };
            Color fill = col; fill.a = 0.35f;
            c.FillPolygon(fin, fill, 0.7f);
            c.StrokePolygon(fin, 1.8f, col);
        }

        // 敌C 坦克brute：六边形装甲块（描边紫）+ 内部几何纹理线。
        // 2 帧 = 内六边形旋转 + 桁架弦线相位偏移。
        static Raster BuildBrute(int frame)
        {
            var c = new Raster(96, 96);
            Color edge = Pal.Brute;
            Color core = Pal.BruteCore;
            float cx = 48f, cy = 48f, R = 36f;
            Vector2 center = new Vector2(cx, cy);

            Vector2[] hex = RegularPolygon(cx, cy, R, 6, 90f);

            Color amb = edge; amb.a = 0.10f;
            c.DrawRadialFade(cx, cy, 46f, amb, 2f);

            Color fill = edge; fill.a = 0.14f;
            c.FillPolygon(hex, fill);
            Color rim = edge; rim.a = 0.30f;
            c.StrokePolygon(hex, 6f, rim);
            c.StrokePolygon(hex, 2.5f, edge);

            // 内六边形（帧间旋转偏移）
            Vector2[] inner = RegularPolygon(cx, cy, R - 12f, 6, 90f + frame * 12f);
            Color detail = edge; detail.a = 0.40f;
            c.StrokePolygon(inner, 1.4f, detail);

            // 桁架弦线（帧间换相位：顶点 0/2/4 ↔ 1/3/5）
            Color chord = edge; chord.a = 0.30f;
            int shift = frame;
            for (int k = 0; k < 3; k++)
            {
                Vector2 a = hex[(k * 2 + shift) % 6];
                Vector2 b = hex[(k * 2 + shift + 2) % 6];
                c.DrawLineSegment(a.x, a.y, b.x, b.y, 1.4f, chord);
            }

            // 顶点内侧铆钉亮点
            for (int k = 0; k < 6; k++)
            {
                Vector2 dir = (hex[k] - center).normalized;
                Vector2 p = center + dir * (R - 7f);
                Color rivet = core; rivet.a = 0.95f;
                c.DrawDisc(p.x, p.y, 2.2f, rivet);
            }

            // 中央核心
            c.DrawGlow(cx, cy, 12f, core);
            Color cg = core; cg.a = 0.50f;
            c.DrawRadialFade(cx, cy, 14f, cg, 2f);
            c.DrawDisc(cx, cy, 5f, core);
            return c;
        }

        // 弹丸：暖黄发光圆头弹（实心白芯 + 多层柔光晕）
        static Raster BuildProjectile()
        {
            var c = new Raster(24, 24);
            c.DrawGlow(12f, 12f, 9.5f, Pal.Bullet);
            Color mid = Pal.Bullet; mid.a = 0.85f;
            c.DrawDisc(12f, 12f, 5.2f, mid);
            c.DrawDisc(12f, 12f, 3.6f, Pal.BulletCore);
            return c;
        }

        // 经验宝石：品红菱形描边 + 亮核心 + 内部斜切面高光
        static Raster BuildXpGem()
        {
            var c = new Raster(32, 32);
            Color edge = Pal.Gem;
            Color core = Pal.GemCore;
            Vector2[] dia =
            {
                new Vector2(16f, 29f),
                new Vector2(27f, 16f),
                new Vector2(16f, 3f),
                new Vector2(5f, 16f)
            };

            Color amb = edge; amb.a = 0.12f;
            c.DrawRadialFade(16f, 16f, 15.5f, amb, 2f);

            Color fill = edge; fill.a = 0.30f;
            c.FillPolygon(dia, fill);

            // 内部：亮核 + 上半斜切面 + 腰线 + 内棱面线
            c.DrawGlow(16f, 16f, 7f, core);
            Vector2[] facet = { dia[0], dia[1], dia[3] };
            Color facetC = core; facetC.a = 0.20f;
            c.FillPolygon(facet, facetC, 0.8f);
            Color girdle = core; girdle.a = 0.45f;
            c.DrawLineSegment(5f, 16f, 27f, 16f, 1f, girdle);
            Vector2[] inner =
            {
                new Vector2(16f, 23f),
                new Vector2(21f, 16f),
                new Vector2(16f, 9f),
                new Vector2(11f, 16f)
            };
            Color inLine = Color.white; inLine.a = 0.50f;
            c.StrokePolygon(inner, 1f, inLine);

            // 描边：软晕 + 2px 主线
            Color rim = edge; rim.a = 0.30f;
            c.StrokePolygon(dia, 5f, rim);
            c.StrokePolygon(dia, 2f, edge);

            // 高光点
            Color sp = Color.white; sp.a = 0.95f;
            c.DrawDisc(13f, 21f, 1.6f, sp);
            return c;
        }

        // 粒子：径向柔光圆（中心白 → 边缘透明，平滑二次衰减）
        static Raster BuildParticleSoft()
        {
            var c = new Raster(32, 32);
            c.DrawRadialFade(16f, 16f, 16f, Color.white, 2.4f);
            return c;
        }

        // 粒子：实心小圆点（微弱光晕防硬边）
        static Raster BuildParticleSpark()
        {
            var c = new Raster(16, 16);
            Color halo = Color.white; halo.a = 0.30f;
            c.DrawRadialFade(8f, 8f, 7.5f, halo, 2f);
            c.DrawDisc(8f, 8f, 4.5f, Color.white);
            return c;
        }

        // 冲击波圆环：细环线，边缘羽化约 2px（过渡窗口 ±1px）
        static Raster BuildRingShockwave()
        {
            var c = new Raster(128, 128);
            c.DrawRing(64f, 64f, 56f, 3f, Color.white, 1f);
            return c;
        }

        // 大面积柔光晕：供 Light2D（Sprite Light）使用
        static Raster BuildGlowDisc()
        {
            var c = new Raster(256, 256);
            c.DrawRadialFade(128f, 128f, 127f, Color.white, 2.2f);
            return c;
        }

        // ------------------------------------------------------------------
        // UI 图标（48x48，线性几何风：白线 + 主题色点缀 + 透明底）
        // ------------------------------------------------------------------

        // 伤害：上箭头 + 弹头（红）
        static Raster BuildIconDamage()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            Color acc = Pal.Drone;
            c.DrawLineSegment(24f, 8f, 24f, 27f, 3f, w);
            c.DrawLineSegment(15f, 18f, 24f, 27f, 3f, w);
            c.DrawLineSegment(33f, 18f, 24f, 27f, 3f, w);
            Vector2[] bullet =
            {
                new Vector2(24f, 45f),
                new Vector2(29f, 38.5f),
                new Vector2(24f, 29.5f),
                new Vector2(19f, 38.5f)
            };
            c.FillPolygon(bullet, acc, 0.7f);
            c.StrokePolygon(bullet, 1.5f, w);
            Color dot = Color.white; dot.a = 0.95f;
            c.DrawDisc(24f, 38.5f, 1.4f, dot);
            return c;
        }

        // 射速：闪电（白描边 + 暖黄填充）
        static Raster BuildIconFirerate()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            Vector2[] bolt =
            {
                new Vector2(20f, 46f),
                new Vector2(34f, 46f),
                new Vector2(23f, 27f),
                new Vector2(31f, 27f),
                new Vector2(11f, 2f),
                new Vector2(18f, 24f),
                new Vector2(9f, 24f)
            };
            c.FillPolygon(bolt, Pal.Bullet, 0.7f);
            c.StrokePolygon(bolt, 2f, w);
            return c;
        }

        // 移速：双箭头 + 加速三角（青）
        static Raster BuildIconSpeed()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            c.DrawLineSegment(7f, 38f, 19f, 24f, 3.5f, w);
            c.DrawLineSegment(19f, 24f, 7f, 10f, 3.5f, w);
            c.DrawLineSegment(21f, 38f, 33f, 24f, 3.5f, w);
            c.DrawLineSegment(33f, 24f, 21f, 10f, 3.5f, w);
            Vector2[] boost =
            {
                new Vector2(37f, 31f),
                new Vector2(45f, 24f),
                new Vector2(37f, 17f)
            };
            c.FillPolygon(boost, Pal.Player, 0.7f);
            return c;
        }

        // 最大生命：心形（红填充 + 白描边）
        static Raster BuildIconMaxHp()
        {
            var c = new Raster(48, 48);
            Vector2[] heart = HeartPts(24f, 27f, 1.05f, 40);
            Color fill = Pal.Drone; fill.a = 0.35f;
            c.FillPolygon(heart, fill, 0.8f);
            c.StrokePolygon(heart, 3f, Pal.IconWhite);
            return c;
        }

        // 拾取范围：U 形磁铁（白线）+ 品红磁极
        static Raster BuildIconPickup()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            c.DrawLineSegment(15f, 42f, 15f, 22f, 4f, w);
            c.DrawLineSegment(33f, 42f, 33f, 22f, 4f, w);
            // 底部半圆弧（180°→360°）
            var arc = new List<Vector2>(21);
            for (int i = 0; i <= 20; i++)
            {
                float ang = Mathf.PI + Mathf.PI * i / 20f;
                arc.Add(new Vector2(24f + Mathf.Cos(ang) * 9f, 22f + Mathf.Sin(ang) * 9f));
            }
            for (int i = 0; i < 20; i++)
                c.DrawLineSegment(arc[i].x, arc[i].y, arc[i + 1].x, arc[i + 1].y, 4f, w);
            // 磁极
            Vector2[] poleL =
            {
                new Vector2(11f, 36f), new Vector2(19f, 36f),
                new Vector2(19f, 43f), new Vector2(11f, 43f)
            };
            Vector2[] poleR =
            {
                new Vector2(29f, 36f), new Vector2(37f, 36f),
                new Vector2(37f, 43f), new Vector2(29f, 43f)
            };
            c.FillPolygon(poleL, Pal.Gem, 0.7f);
            c.FillPolygon(poleR, Pal.Gem, 0.7f);
            return c;
        }

        // 多重射击：三箭散射（橙箭头 + 白箭杆）
        static Raster BuildIconMultishot()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            Vector2 origin = new Vector2(24f, 7f);
            Vector2[] dirs =
            {
                new Vector2(-0.5f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0.5f, 1f)
            };
            for (int i = 0; i < 3; i++)
            {
                Vector2 dir = dirs[i].normalized;
                Vector2 tip = origin + dir * 32f;
                c.DrawLineSegment(origin.x, origin.y, tip.x - dir.x * 6f, tip.y - dir.y * 6f, 2.5f, w);
                FilledArrowHead(c, tip, dir, 9f, 4.5f, Pal.Darter, 0.7f);
            }
            return c;
        }

        // 暴击：十字准星（白）+ 红色准星芯
        static Raster BuildIconCrit()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            Color acc = Pal.Drone;
            c.DrawRing(24f, 24f, 13f, 2.5f, w);
            c.DrawLineSegment(24f, 37f, 24f, 45f, 3f, w);
            c.DrawLineSegment(24f, 11f, 24f, 3f, 3f, w);
            c.DrawLineSegment(11f, 24f, 3f, 24f, 3f, w);
            c.DrawLineSegment(37f, 24f, 45f, 24f, 3f, w);
            c.DrawGlow(24f, 24f, 7f, acc);
            c.DrawDisc(24f, 24f, 3f, acc);
            return c;
        }

        // 再生：心形（白描边）+ 医疗十字（青）
        static Raster BuildIconRegen()
        {
            var c = new Raster(48, 48);
            Color w = Pal.IconWhite;
            Vector2[] heart = HeartPts(24f, 26f, 0.8f, 36);
            Color fill = w; fill.a = 0.15f;
            c.FillPolygon(heart, fill, 0.8f);
            c.StrokePolygon(heart, 2.5f, w);
            Color acc = Pal.Player; acc.a = 0.95f;
            c.FillPolygon(new[]
            {
                new Vector2(13f, 21.8f), new Vector2(35f, 21.8f),
                new Vector2(35f, 26.2f), new Vector2(13f, 26.2f)
            }, acc, 0.6f);
            c.FillPolygon(new[]
            {
                new Vector2(21.8f, 13f), new Vector2(26.2f, 13f),
                new Vector2(26.2f, 35f), new Vector2(21.8f, 35f)
            }, acc, 0.6f);
            return c;
        }

        // ------------------------------------------------------------------
        // 场景 / 后期
        // ------------------------------------------------------------------

        // 暗角遮罩：中心透明 → 四角黑（平滑）
        static Raster BuildVignette()
        {
            const int size = 512;
            var c = new Raster(size, size);
            float half = size * 0.5f;
            float maxD = half * Mathf.Sqrt(2f);
            for (int y = 0; y < size; y++)
            {
                float dy = y + 0.5f - half;
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float t = Mathf.Sqrt(dx * dx + dy * dy) / maxD;
                    float a = Mathf.Clamp01((t - 0.50f) / 0.50f);
                    a = a * a * (3f - 2f * a);
                    if (a <= 0f) continue;
                    c.Set(x, y, new Color(0f, 0f, 0f, a));
                }
            }
            return c;
        }

        // 视差星空层：深蓝黑底铺满 + 指定数量的星星（白 + 蓝/青/品红微调）。
        // 近层额外带十字光芒与柔晕。固定种子 → 每次生成结果一致。
        static Raster BuildStarLayer(int seed, int starCount, float rMin, float rMax, float aMin, float aMax, bool flare)
        {
            var c = new Raster(512, 512);
            c.Fill(Pal.Background);
            var rng = new Rng((uint)seed);
            Color[] tints =
            {
                FromHexTint("#8FB8FF"), // 蓝
                FromHexTint("#7FE9FF"), // 青
                FromHexTint("#FF9BE8")  // 品红
            };
            for (int i = 0; i < starCount; i++)
            {
                float x = rng.Range(6f, 506f);
                float y = rng.Range(6f, 506f);
                float r = rng.Range(rMin, rMax);
                float a = rng.Range(aMin, aMax);
                Color col = Color.Lerp(Color.white, tints[(int)(rng.Next() % 3u)], rng.Range(0f, 0.45f));
                col.a = a;
                if (flare)
                {
                    Color halo = col; halo.a = a * 0.25f;
                    c.DrawRadialFade(x, y, r * 4.5f, halo, 2f);
                }
                c.DrawDisc(x, y, r, col, 0.6f);
                Color bright = Color.white; bright.a = Mathf.Min(1f, a + 0.15f);
                c.DrawDisc(x, y, r * 0.55f, bright, 0.5f);
                if (flare)
                {
                    float fl = r * rng.Range(4.5f, 8.5f);
                    Color fc = col; fc.a = a * 0.40f;
                    c.DrawLineSegment(x - fl, y, x + fl, y, 1f, fc, 0.6f);
                    c.DrawLineSegment(x, y - fl, x, y + fl, 1f, fc, 0.6f);
                }
            }
            return c;
        }

        static Color FromHexTint(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return c;
            return Color.white;
        }

        // 柔和星云团：多个低 alpha 柔斑叠成不规则 blob + 中央亮核 + 微星点缀
        static Raster BuildNebula(int seed, Color baseCol)
        {
            var c = new Raster(256, 256);
            var rng = new Rng((uint)seed);
            float cx = 128f, cy = 128f;
            for (int i = 0; i < 12; i++)
            {
                float ang = rng.Range(0f, Mathf.PI * 2f);
                float dist = rng.Range(0f, 62f);
                float size = rng.Range(34f, 82f);
                Vector2 p = new Vector2(cx + Mathf.Cos(ang) * dist, cy + Mathf.Sin(ang) * dist * 0.7f);
                Color col = Color.Lerp(baseCol, Color.white, rng.Range(0f, 0.25f));
                col.a = rng.Range(0.05f, 0.13f);
                c.DrawRadialFade(p.x, p.y, size, col, 2.1f);
            }
            Color heart = baseCol; heart.a = 0.28f;
            c.DrawRadialFade(cx, cy, 60f, heart, 2.1f);
            Color heart2 = Color.Lerp(baseCol, Color.white, 0.5f); heart2.a = 0.20f;
            c.DrawRadialFade(cx, cy, 34f, heart2, 2f);
            for (int i = 0; i < 4; i++)
            {
                float x = rng.Range(70f, 186f);
                float y = rng.Range(70f, 186f);
                Color sc = Color.white; sc.a = rng.Range(0.4f, 0.8f);
                c.DrawDisc(x, y, rng.Range(0.8f, 1.6f), sc, 0.6f);
            }
            return c;
        }

        // ==================================================================
        // 几何工具
        // ==================================================================

        // 圆角闭合路径：对每个角用二次贝塞尔做圆角近似（用于飞船/箭头轮廓）
        static Vector2[] RoundedPath(Vector2[] corners, float[] radii, int segsPerCorner)
        {
            var list = new List<Vector2>(corners.Length * (segsPerCorner + 1));
            int n = corners.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = corners[(i + n - 1) % n];
                Vector2 cur = corners[i];
                Vector2 next = corners[(i + 1) % n];
                float r = radii != null && i < radii.Length ? radii[i] : 4f;
                Vector2 a = cur + (prev - cur).normalized * r;
                Vector2 b = cur + (next - cur).normalized * r;
                for (int s2 = 0; s2 <= segsPerCorner; s2++)
                {
                    float t = s2 / (float)segsPerCorner;
                    float it = 1f - t;
                    list.Add(it * it * a + 2f * it * t * cur + t * t * b);
                }
            }
            return list.ToArray();
        }

        // 正多边形顶点（startDeg 为第一个顶点的角度，90 = 正上方）
        static Vector2[] RegularPolygon(float cx, float cy, float r, int sides, float startDeg)
        {
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float ang = (startDeg + i * 360f / sides) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
            }
            return pts;
        }

        // 心形参数曲线点集（用于 maxhp / regen 图标）
        static Vector2[] HeartPts(float cx, float cy, float scale, int segs)
        {
            var pts = new Vector2[segs];
            for (int i = 0; i < segs; i++)
            {
                float t = Mathf.PI * 2f * i / segs;
                float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t)
                        - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                pts[i] = new Vector2(cx + x * scale, cy + y * scale);
            }
            return pts;
        }

        // 实心箭头（三角头）：tip 为尖端，dir 为朝向
        static void FilledArrowHead(Raster c, Vector2 tip, Vector2 dir, float len, float halfW, Color col, float feather = 0.5f)
        {
            dir = dir.normalized;
            Vector2 back = tip - dir * len;
            Vector2 perp = new Vector2(-dir.y, dir.x) * halfW;
            Vector2[] tri = { tip, back + perp, back - perp };
            c.FillPolygon(tri, col, feather);
        }

        // ==================================================================
        // 软件光栅器（Texture2D 直写版）
        // - 所有图元基于符号距离场（SDF）+ smoothstep 羽化，天然抗锯齿无硬边
        // - 混合为 straight-alpha "over"，可安全多层叠加发光
        // ==================================================================
        class Raster
        {
            public readonly int W;
            public readonly int H;
            readonly Color[] px;

            public Raster(int w, int h)
            {
                W = w;
                H = h;
                px = new Color[w * h]; // 默认全透明
            }

            // 纯色铺满（星空底色用）
            public void Fill(Color c)
            {
                for (int i = 0; i < px.Length; i++) px[i] = c;
            }

            // 直写像素（越界忽略）
            public void Set(int x, int y, Color c)
            {
                if ((uint)x < (uint)W && (uint)y < (uint)H)
                    px[y * W + x] = c;
            }

            // straight-alpha "over" 混合（越界忽略）
            public void Blend(int x, int y, Color c)
            {
                if (c.a <= 0f) return;
                if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
                int i = y * W + x;
                Color d = px[i];
                float keep = 1f - c.a;
                float oa = c.a + d.a * keep;
                if (oa <= 0f)
                {
                    px[i] = new Color(0f, 0f, 0f, 0f);
                    return;
                }
                px[i] = new Color(
                    (c.r * c.a + d.r * d.a * keep) / oa,
                    (c.g * c.a + d.g * d.a * keep) / oa,
                    (c.b * c.a + d.b * d.a * keep) / oa,
                    oa);
            }

            // SDF → 覆盖率：dist ≤ -f 全显、≥ +f 全隐，中间 smoothstep（f = 羽化半径）。
            // 默认 f = 0.5，等价于规格公式 alpha = smoothstep(0.5, -0.5, dist)。
            public static float SdfAlpha(float dist, float feather)
            {
                if (feather < 0.05f) feather = 0.05f;
                float t = Mathf.Clamp01((feather - dist) / (2f * feather));
                return t * t * (3f - 2f * t);
            }

            // 实心圆（SDF，边缘 1px 羽化抗锯齿）
            public void DrawDisc(float cx, float cy, float r, Color color, float feather = 0.5f)
            {
                float ext = r + feather + 1f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - ext));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + ext));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ext));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ext));
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - cy;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                        float a = SdfAlpha(d, feather);
                        if (a <= 0f) continue;
                        Color t = color;
                        t.a *= a;
                        Blend(x, y, t);
                    }
                }
            }

            // 圆环（width 为线宽，圆头无方向）
            public void DrawRing(float cx, float cy, float r, float width, Color color, float feather = 0.5f)
            {
                float ext = r + width * 0.5f + feather + 1f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - ext));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + ext));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ext));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ext));
                float half = width * 0.5f;
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - cy;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx;
                        float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - half;
                        float a = SdfAlpha(d, feather);
                        if (a <= 0f) continue;
                        Color t = color;
                        t.a *= a;
                        Blend(x, y, t);
                    }
                }
            }

            // 线段（胶囊距离场：圆头端点、宽度均匀，优于 Bresenham 的抗锯齿效果）
            public void DrawLineSegment(float x0, float y0, float x1, float y1, float width, Color color, float feather = 0.5f)
            {
                float ext = width * 0.5f + feather + 1f;
                int bx0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - ext));
                int bx1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + ext));
                int by0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - ext));
                int by1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + ext));
                float vx = x1 - x0;
                float vy = y1 - y0;
                float len2 = vx * vx + vy * vy;
                float half = width * 0.5f;
                for (int y = by0; y <= by1; y++)
                {
                    float py = y + 0.5f;
                    for (int x = bx0; x <= bx1; x++)
                    {
                        float wx = x + 0.5f - x0;
                        float wy = py - y0;
                        float t = len2 > 1e-8f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
                        float dx = wx - t * vx;
                        float dy = wy - t * vy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) - half;
                        float a = SdfAlpha(d, feather);
                        if (a <= 0f) continue;
                        Color c2 = color;
                        c2.a *= a;
                        Blend(x, y, c2);
                    }
                }
            }

            // 多边形填充（偶奇规则判内 + 到边界的符号距离羽化，支持凹多边形）
            public void FillPolygon(Vector2[] pts, Color color, float feather = 0.5f)
            {
                int n = pts.Length;
                if (n < 3) return;
                PolygonBounds(pts, n, out float minX, out float maxX, out float minY, out float maxY);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX - feather - 1f));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX + feather + 1f));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY - feather - 1f));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY + feather + 1f));
                for (int y = y0; y <= y1; y++)
                {
                    float sy = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        bool inside = PolygonSdf(pts, n, x + 0.5f, sy, out float dmin);
                        float sdf = inside ? -dmin : dmin;
                        float a = SdfAlpha(sdf, feather);
                        if (a <= 0f) continue;
                        Color t = color;
                        t.a *= a;
                        Blend(x, y, t);
                    }
                }
            }

            // 多边形描边（整体距离场成线：宽度均匀、拐角平滑无接缝珠）
            public void StrokePolygon(Vector2[] pts, float width, Color color, float feather = 0.5f)
            {
                int n = pts.Length;
                if (n < 3) return;
                PolygonBounds(pts, n, out float minX, out float maxX, out float minY, out float maxY);
                float ext = width * 0.5f + feather + 1f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX - ext));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX + ext));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY - ext));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY + ext));
                float half = width * 0.5f;
                for (int y = y0; y <= y1; y++)
                {
                    float sy = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        PolygonSdf(pts, n, x + 0.5f, sy, out float dmin);
                        float d = dmin - half; // 描边带只看绝对距离
                        float a = SdfAlpha(d, feather);
                        if (a <= 0f) continue;
                        Color t = color;
                        t.a *= a;
                        Blend(x, y, t);
                    }
                }
            }

            // 发光叠加：4 层同心圆（中心 → 外 alpha 约 0.5 → 0.05），层缘大羽化避免色带
            public void DrawGlow(float cx, float cy, float r, Color color)
            {
                float[] mul = { 1.0f, 0.74f, 0.52f, 0.32f };
                float[] alp = { 0.05f, 0.10f, 0.22f, 0.50f };
                for (int i = 0; i < mul.Length; i++)
                {
                    float rr = r * mul[i];
                    Color t = color;
                    t.a *= alp[i];
                    DrawDisc(cx, cy, rr, t, Mathf.Max(1.2f, rr * 0.5f));
                }
            }

            // 平滑径向衰减圆：中心 color.a → 边缘 0（连续衰减，等效多层同心叠加且无色带）
            public void DrawRadialFade(float cx, float cy, float r, Color color, float power = 2f)
            {
                if (r <= 0f) return;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1f));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r + 1f));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1f));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r + 1f));
                float invR = 1f / r;
                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - cy;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx;
                        float t = Mathf.Sqrt(dx * dx + dy * dy) * invR;
                        if (t >= 1f) continue;
                        float a = Mathf.Pow(1f - t, power) * color.a;
                        if (a <= 0.002f) continue;
                        Color c2 = color;
                        c2.a = a;
                        Blend(x, y, c2);
                    }
                }
            }

            // 编码为 PNG 并写入磁盘（RGBA32，sRGB 直写；覆盖旧文件）
            public void SavePng(string absPath)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.SetPixels(px);
                tex.Apply(false, false);
                try
                {
                    File.WriteAllBytes(absPath, tex.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }

            // ---- 内部工具 ----

            static void PolygonBounds(Vector2[] pts, int n, out float minX, out float maxX, out float minY, out float maxY)
            {
                minX = maxX = pts[0].x;
                minY = maxY = pts[0].y;
                for (int i = 1; i < n; i++)
                {
                    if (pts[i].x < minX) minX = pts[i].x;
                    if (pts[i].x > maxX) maxX = pts[i].x;
                    if (pts[i].y < minY) minY = pts[i].y;
                    if (pts[i].y > maxY) maxY = pts[i].y;
                }
            }

            // 偶奇规则判内 + 到边界最近距离（一条循环同时完成）
            static bool PolygonSdf(Vector2[] pts, int n, float sx, float sy, out float dmin)
            {
                bool inside = false;
                dmin = float.MaxValue;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 a = pts[j];
                    Vector2 b = pts[i];
                    if ((a.y > sy) != (b.y > sy))
                    {
                        float xInt = (b.x - a.x) * (sy - a.y) / (b.y - a.y) + a.x;
                        if (sx < xInt) inside = !inside;
                    }
                    float d = DistPointSeg(sx, sy, a.x, a.y, b.x, b.y);
                    if (d < dmin) dmin = d;
                }
                return inside;
            }

            // 点到线段距离
            static float DistPointSeg(float px, float py, float ax, float ay, float bx, float by)
            {
                float vx = bx - ax;
                float vy = by - ay;
                float wx = px - ax;
                float wy = py - ay;
                float len2 = vx * vx + vy * vy;
                float t = len2 > 1e-9f ? Mathf.Clamp01((wx * vx + wy * vy) / len2) : 0f;
                float dx = wx - t * vx;
                float dy = wy - t * vy;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }
        }

        // 简易 xorshift32 随机数（固定种子 → 生成结果可复现；不依赖 System.Random）
        struct Rng
        {
            uint state;

            public Rng(uint seed)
            {
                state = seed == 0 ? 0x9E3779B9u : seed;
            }

            public uint Next()
            {
                uint x = state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                state = x;
                return x;
            }

            public float Next01()
            {
                return (Next() & 0xFFFFFF) / 16777215f;
            }

            public float Range(float min, float max)
            {
                return Mathf.Lerp(min, max, Next01());
            }
        }
    }
}
