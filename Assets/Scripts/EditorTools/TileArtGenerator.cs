using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 《勇者远征》环境美术套件（NeonArtGenerator 分部）：三关地形瓦片 / 装饰物 / 漩涡传送门 /
    /// 金属尖刺 / 雾带 / 全屏暗角。
    /// 面试点：针对"平台是纯色矩形+虚线、传送门白色 D 形、尖刺灰三角"的占位升级件——
    /// 瓦片带噪点纹理、明暗渐变（伪 AO）与四边描边；传送门做多层紫蓝光环 + 旋臂 + 外圈粒子尘；
    /// 尖刺做银灰三面渐变 + 中脊高光。全部由 SDF 光栅器 + 确定性噪声（格点哈希 + System.Random
    /// 固定种子）逐像素绘制，多次生成结果逐字节一致。
    /// 说明：PNG 保持 LDR，运行期靠 HDR 精灵色 + Bloom 补发光（与主生成器同一约定）。
    /// </summary>
    public static partial class NeonArtGenerator
    {
        /// <summary>入口：DemoSceneBuilder.GenerateAll 在 GenerateFantasy 之后调用。</summary>
        public static void GenerateTileArt()
        {
            string absDir = Path.Combine(Application.dataPath, "Art", "Generated");
            Directory.CreateDirectory(absDir);
            int count = 0;

            // ---- 三关地形瓦片（256²，LevelBuilder 以 Tiled 平铺铺贴） ----
            Emit(BuildTileAshForest(), absDir, "platform_l1", ref count);
            Emit(BuildTileCaveBrick(), absDir, "platform_l2", ref count);
            Emit(BuildTileObsidian(), absDir, "platform_l3", ref count);

            // ---- 装饰 props（各关通用贴图 + LevelBuilder 按关染色） ----
            Emit(BuildGrassTuft(0), absDir, "grass_tuft_0", ref count);
            Emit(BuildGrassTuft(1), absDir, "grass_tuft_1", ref count);
            Emit(BuildGrassTuft(2), absDir, "grass_tuft_2", ref count);
            Emit(BuildFlower(), absDir, "flower", ref count);
            Emit(BuildMushroom(), absDir, "mushroom", ref count);
            Emit(BuildRock(), absDir, "rock", ref count);
            Emit(BuildVine(), absDir, "vine", ref count);
            Emit(BuildStump(), absDir, "stump", ref count);
            Emit(BuildTreeFar(), absDir, "tree_far", ref count);

            // ---- 陷阱 / 传送门 / 氛围层 ----
            Emit(BuildPortalVortex(), absDir, "portal_vortex", ref count);
            Emit(BuildSpikesMetal(), absDir, "spikes_metal", ref count);
            Emit(BuildFogStrip(), absDir, "fog_strip", ref count);
            Emit(BuildVignette(), absDir, "vignette", ref count);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TileArt] 环境美术套件生成 {count} 张 → Assets/Art/Generated");
        }

        // ==================================================================
        // 确定性噪声与逐像素后处理（TileArt 专用；读像素走 Raster.Get）
        // ==================================================================

        /// <summary>格点哈希 → 0..1（整数溢出按补码回绕，输入相同结果恒定）。</summary>
        private static float Hash01(int ix, int iy, int seed)
        {
            int h = ix * 374761393 + iy * 668265263 + seed * 1013904223;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / 2147483647f;
        }

        /// <summary>值噪声：格点哈希双三次插值（smoothstep），连续可平铺尺度的基本单元。</summary>
        private static float ValueNoise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash01(ix, iy, seed);
            float b = Hash01(ix + 1, iy, seed);
            float c2 = Hash01(ix, iy + 1, seed);
            float d = Hash01(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c2, d, sx), sy);
        }

        /// <summary>三层 fbm：土质颗粒 / 砖面风化 / 雾团密度的通用噪声场。</summary>
        private static float Fbm(float x, float y, int seed)
        {
            return ValueNoise(x, y, seed) * 0.62f
                 + ValueNoise(x * 2.17f + 13.1f, y * 2.17f + 7.7f, seed + 37) * 0.26f
                 + ValueNoise(x * 4.3f + 31.7f, y * 4.3f + 17.3f, seed + 91) * 0.12f;
        }

        /// <summary>纹理噪点：亮度乘性微扰（只作用于已绘制像素，透明区不动）。</summary>
        private static void ApplyGrain(Raster c, float amplitude, int seed, float scaleX = 0.06f, float scaleY = 0.06f)
        {
            for (int y = 0; y < c.H; y++)
                for (int x = 0; x < c.W; x++)
                {
                    Color p = c.Get(x, y);
                    if (p.a <= 0f) continue;
                    float m = 1f + (Fbm(x * scaleX, y * scaleY, seed) - 0.5f) * 2f * amplitude;
                    p.r *= m;
                    p.g *= m;
                    p.b *= m;
                    c.Set(x, y, p);
                }
        }

        /// <summary>伪 AO：顶部微提亮、底部压暗（乘性渐变，只作用于已绘制像素）。</summary>
        private static void ApplyVerticalShade(Raster c, float topMul, float bottomMul)
        {
            for (int y = 0; y < c.H; y++)
                for (int x = 0; x < c.W; x++)
                {
                    Color p = c.Get(x, y);
                    if (p.a <= 0f) continue;
                    float t = y / (c.H - 1f);
                    float m = Mathf.Lerp(bottomMul, topMul, t * t * (3f - 2f * t));
                    p.r *= m;
                    p.g *= m;
                    p.b *= m;
                    c.Set(x, y, p);
                }
        }

        /// <summary>低频大斑块（暗色土斑 / 红矿脉）：fbm 阈值化后向 tint 插值；yMax 可限制只染土壤层。</summary>
        private static void ApplyBlotches(Raster c, Color tint, float strength, int seed, float scale, float yMax = float.MaxValue)
        {
            for (int y = 0; y < c.H && y < yMax; y++)
                for (int x = 0; x < c.W; x++)
                {
                    Color p = c.Get(x, y);
                    if (p.a <= 0f) continue;
                    float k = Mathf.SmoothStep(0.52f, 0.78f, Fbm(x * scale, y * scale, seed)) * strength;
                    p.r = Mathf.Lerp(p.r, tint.r, k);
                    p.g = Mathf.Lerp(p.g, tint.g, k);
                    p.b = Mathf.Lerp(p.b, tint.b, k);
                    c.Set(x, y, p);
                }
        }

        /// <summary>单像素向 tint 插值（强度按既有 alpha 加权，不产生外溢光晕）。</summary>
        private static void EdgeBlend(Raster c, int x, int y, Color tint, float strength)
        {
            Color p = c.Get(x, y);
            if (p.a <= 0f) return;
            float k = strength * p.a;
            p.r = Mathf.Lerp(p.r, tint.r, k);
            p.g = Mathf.Lerp(p.g, tint.g, k);
            p.b = Mathf.Lerp(p.b, tint.b, k);
            c.Set(x, y, p);
        }

        /// <summary>
        /// 瓦片四边描边：顶缘亮棱 + 棱下阴影线（地表读数），底/侧缘压暗（伪 AO）。
        /// 侧缘压得很淡——平铺后呈现克制的块面接缝而非"网格感"。
        /// </summary>
        private static void ApplyTileEdges(Raster c, Color topRim, Color topShadow, Color sideShade, Color bottomShade)
        {
            int w = c.W;
            int h = c.H;
            for (int x = 0; x < w; x++)
            {
                EdgeBlend(c, x, h - 1, topRim, 0.85f);
                EdgeBlend(c, x, h - 2, topRim, 0.55f);
                EdgeBlend(c, x, h - 3, topShadow, 0.50f);
                EdgeBlend(c, x, h - 4, topShadow, 0.28f);
                EdgeBlend(c, x, 0, bottomShade, 0.55f);
                EdgeBlend(c, x, 1, bottomShade, 0.30f);
            }
            for (int y = 2; y < h - 4; y++)
            {
                EdgeBlend(c, 0, y, sideShade, 0.38f);
                EdgeBlend(c, 1, y, sideShade, 0.20f);
                EdgeBlend(c, w - 1, y, sideShade, 0.38f);
                EdgeBlend(c, w - 2, y, sideShade, 0.20f);
            }
        }

        /// <summary>轴对齐矩形多边形（砖体 / 条带用）。</summary>
        private static Vector2[] RectPoly(float x0, float y0, float w, float h)
        {
            return new[]
            {
                new Vector2(x0, y0), new Vector2(x0 + w, y0),
                new Vector2(x0 + w, y0 + h), new Vector2(x0, y0 + h),
            };
        }

        /// <summary>随机游走折线：裂纹路径点（先取点后描线，暗缝与发光纹共用一条路径）。</summary>
        private static List<Vector2> CrackWalk(System.Random rng, float x0, float y0, int steps, float segLen, float spread)
        {
            var pts = new List<Vector2> { new Vector2(x0, y0) };
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float x = x0;
            float y = y0;
            for (int i = 0; i < steps; i++)
            {
                ang += ((float)rng.NextDouble() - 0.5f) * spread;
                x += Mathf.Cos(ang) * segLen;
                y += Mathf.Sin(ang) * segLen;
                pts.Add(new Vector2(x, y));
            }
            return pts;
        }

        private static void DrawPolyline(Raster c, List<Vector2> pts, float width, Color col)
        {
            for (int i = 0; i < pts.Count - 1; i++)
                c.DrawLineSegment(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, width, col);
        }

        /// <summary>椭圆填充（断层面 / 叶片等非正圆形状）。</summary>
        private static void EllipseFill(Raster c, float cx, float cy, float rx, float ry, Color col)
        {
            int n = 16;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }
            c.FillPolygon(pts, col);
        }

        // ==================================================================
        // 三关地形瓦片（256×256；y=255 为地表、y=0 为深层——Tiled 平铺时顶缘草皮/描边必然露出）
        // ==================================================================

        // ---- L1 灰烬森林：草皮 + 草根须过渡 + 噪点土体 + 石子/灰烬 ----
        private static Raster BuildTileAshForest()
        {
            var c = new Raster(256, 256);
            // 土体基色：下暗上亮的垂直渐变
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float t = y / 255f;
                    c.Set(x, y, Color.Lerp(new Color(0.24f, 0.185f, 0.14f), new Color(0.40f, 0.32f, 0.235f),
                        Mathf.SmoothStep(0f, 1f, t)));
                }

            // 草皮层：顶 50px，底界随噪声起伏（亮绿渐深绿，与土壤融接）
            for (int y = 204; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float wob = Fbm(x * 0.045f, 0.7f, 101) * 10f;
                    float depth = (y - (204f + wob)) / 50f;
                    if (depth <= 0f) continue;
                    Color grass = Color.Lerp(new Color(0.14f, 0.26f, 0.115f), new Color(0.43f, 0.60f, 0.27f),
                        Mathf.Clamp01(depth * 1.25f));
                    Color p = c.Get(x, y);
                    float a = Mathf.Clamp01(depth * 4f); // 草皮底缘 12px 内与土壤融接
                    p.r = Mathf.Lerp(p.r, grass.r, a);
                    p.g = Mathf.Lerp(p.g, grass.g, a);
                    p.b = Mathf.Lerp(p.b, grass.b, a);
                    c.Set(x, y, p);
                }

            var rng = new System.Random(20260101);

            // 草根须：从草皮底界向下垂的暗绿短根，衔接草皮与土体
            for (int i = 0; i < 26; i++)
            {
                float x = 4f + (float)rng.NextDouble() * 248f;
                float wobble = Fbm(x * 0.05f, 0.7f, 101) * 10f;
                float y0 = 202f + wobble - 2f;
                float len = 8f + (float)rng.NextDouble() * 13f;
                float drift = ((float)rng.NextDouble() - 0.5f) * 5f;
                c.DrawLineSegment(x, y0, x + drift, y0 - len, 1.6f, new Color(0.10f, 0.16f, 0.075f, 0.65f));
            }

            // 小石子：受光面 / 背光面两层圆叠出体积
            for (int i = 0; i < 14; i++)
            {
                float sx = 20f + (float)rng.NextDouble() * 216f;
                float sy = 6f + (float)rng.NextDouble() * 184f;
                float r = 3f + (float)rng.NextDouble() * 4.5f;
                c.DrawDisc(sx, sy, r, new Color(0.46f, 0.42f, 0.36f, 0.9f));
                c.DrawDisc(sx + r * 0.3f, sy + r * 0.35f, r * 0.45f, new Color(0.58f, 0.55f, 0.48f, 0.8f));
                c.DrawDisc(sx - r * 0.25f, sy - r * 0.3f, r * 0.55f, new Color(0.30f, 0.27f, 0.23f, 0.6f));
            }

            // 暗色土斑（大而软，只染土壤层不脏草皮）
            ApplyBlotches(c, new Color(0.15f, 0.115f, 0.085f), 0.26f, 11, 0.012f, 200f);

            // 灰烬碎屑：亮灰细点（关卡主题点题）
            for (int i = 0; i < 14; i++)
            {
                float ax = (float)rng.NextDouble() * 250f + 3f;
                float ay = (float)rng.NextDouble() * 244f + 3f;
                c.DrawDisc(ax, ay, 1f + (float)rng.NextDouble() * 1.4f, new Color(0.62f, 0.60f, 0.55f, 0.5f));
            }

            ApplyTileEdges(c, new Color(0.66f, 0.74f, 0.44f), new Color(0.10f, 0.09f, 0.06f),
                new Color(0.13f, 0.11f, 0.09f), new Color(0.09f, 0.075f, 0.06f));
            ApplyGrain(c, 0.075f, 7, 0.055f, 0.055f);
            ApplyVerticalShade(c, 1.07f, 0.80f);
            return c;
        }

        // ---- L2 坍塌矿洞：蓝灰石砖（错缝 + 抖角）+ 倒角 + 苔藓 + 裂纹 ----
        private static Raster BuildTileCaveBrick()
        {
            var c = new Raster(256, 256);
            c.Fill(new Color(0.115f, 0.135f, 0.175f, 1f)); // 砖缝底色，砖体逐块覆盖

            var rng = new System.Random(20260202);
            const float rowH = 64f;
            const float colW = 128f;
            const float joint = 3.5f;
            for (int row = 0; row < 4; row++)
            {
                float y0 = row * rowH;
                float off = (row % 2) * colW * 0.5f;
                for (int bx = -1; bx <= 2; bx++)
                {
                    float x0 = bx * colW + off;
                    // 错缝平铺无缝：以 2 砖为周期取哈希（x0 ≡ 同余的砖共用亮度/抖角）
                    int k = ((bx % 2) + 2) % 2;
                    float b = 1f + (Hash01(row * 7 + 3, k, 501) - 0.5f) * 0.16f;

                    var pts = new Vector2[4];
                    for (int ci = 0; ci < 4; ci++)
                    {
                        float cx = x0 + (ci == 1 || ci == 2 ? colW : 0f);
                        float cy = y0 + (ci >= 2 ? rowH : 0f);
                        pts[ci] = new Vector2(
                            cx + (Hash01(row * 17 + ci, k * 5 + 1, 502) - 0.5f) * 5f,
                            cy + (Hash01(row * 17 + ci, k * 5 + 2, 503) - 0.5f) * 5f);
                    }
                    c.FillPolygon(pts, new Color(0.315f * b, 0.355f * b, 0.43f * b, 1f));

                    // 斜面倒角：砖顶受光条 + 砖底背光条
                    c.FillPolygon(RectPoly(x0 + joint, y0 + rowH - joint - 7, colW - joint * 2, 7),
                        new Color(0.44f * b, 0.48f * b, 0.56f * b, 0.55f));
                    c.FillPolygon(RectPoly(x0 + joint, y0 + joint, colW - joint * 2, 7),
                        new Color(0.20f * b, 0.23f * b, 0.30f * b, 0.55f));

                    // 风化碎坑（部分砖）
                    if (Hash01(row, k + 9, 507) > 0.55f)
                    {
                        float px = x0 + colW * (0.25f + 0.5f * (float)rng.NextDouble());
                        float py = y0 + rowH * (0.3f + 0.4f * (float)rng.NextDouble());
                        c.DrawDisc(px, py, 4f + 3f * (float)rng.NextDouble(), new Color(0.16f, 0.19f, 0.24f, 0.7f));
                    }
                }
            }

            // 苔藓：贴砖顶缘生长的丛簇（矿洞阴湿感）
            for (int i = 0; i < 8; i++)
            {
                float mx = 8f + (float)rng.NextDouble() * 240f;
                float my = Mathf.Floor((float)rng.NextDouble() * 4f) * 64f + 61f;
                for (int j = 0; j < 4; j++)
                {
                    float dx = ((float)rng.NextDouble() - 0.5f) * 22f;
                    float dy = (float)rng.NextDouble() * 7f;
                    c.DrawDisc(mx + dx, my + dy * 0.5f, 3.5f + (float)rng.NextDouble() * 5f,
                        new Color(0.20f, 0.38f, 0.19f, 0.55f));
                }
                c.DrawDisc(mx, my + 2f, 2.5f, new Color(0.34f, 0.55f, 0.28f, 0.7f));
            }

            // 裂纹：随机游走暗缝
            for (int i = 0; i < 4; i++)
            {
                var pts = CrackWalk(rng, 8f + (float)rng.NextDouble() * 240f, 8f + (float)rng.NextDouble() * 240f,
                    6, 14f, 1.9f);
                DrawPolyline(c, pts, 2f, new Color(0.075f, 0.085f, 0.115f, 0.8f));
            }

            // 横缝里的小碎石
            for (int i = 0; i < 6; i++)
            {
                float sx = 4f + (float)rng.NextDouble() * 248f;
                float sy = Mathf.Floor((float)rng.NextDouble() * 4f) * 64f + 1.5f;
                c.DrawDisc(sx, sy, 2.2f, new Color(0.34f, 0.38f, 0.45f, 0.9f));
            }

            ApplyTileEdges(c, new Color(0.55f, 0.60f, 0.70f), new Color(0.055f, 0.065f, 0.095f),
                new Color(0.085f, 0.10f, 0.14f), new Color(0.05f, 0.06f, 0.085f));
            ApplyGrain(c, 0.06f, 23, 0.07f, 0.07f);
            ApplyVerticalShade(c, 1.06f, 0.82f);
            return c;
        }

        // ---- L3 深渊王座：暗红紫黑曜石 + 斜向光泽 + 暗裂纹 + 发光纹路 + 余烬 ----
        private static Raster BuildTileObsidian()
        {
            var c = new Raster(256, 256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float t = y / 255f;
                    c.Set(x, y, Color.Lerp(new Color(0.065f, 0.05f, 0.095f), new Color(0.125f, 0.085f, 0.16f), t));
                }

            var rng = new System.Random(20260303);

            // 斜向玻璃光泽带（黑曜石质感的关键：宽而淡的斜条）
            for (int i = 0; i < 5; i++)
            {
                float cx = 20f + i * 52f + (float)rng.NextDouble() * 18f;
                c.FillPolygon(new[]
                {
                    new Vector2(cx - 16f, -4f), new Vector2(cx + 8f, -4f),
                    new Vector2(cx + 62f, 260f), new Vector2(cx + 38f, 260f),
                }, new Color(0.26f, 0.17f, 0.33f, 0.16f));
            }

            // 暗红矿脉斑块
            ApplyBlotches(c, new Color(0.17f, 0.075f, 0.10f), 0.32f, 61, 0.013f);

            // 裂纹（暗缝）+ 其中三条叠发光纹路（深渊能量纹）
            for (int i = 0; i < 5; i++)
            {
                var pts = CrackWalk(rng, 16f + (float)rng.NextDouble() * 224f, 16f + (float)rng.NextDouble() * 224f,
                    7, 15f, 1.7f);
                DrawPolyline(c, pts, 2.6f, new Color(0.02f, 0.015f, 0.035f, 0.9f));
                if (i < 3)
                {
                    DrawPolyline(c, pts, 1.4f, new Color(0.9f, 0.28f, 0.62f, 0.8f));
                    for (int j = 0; j < pts.Count; j += 2)
                        c.DrawGlow(pts[j].x, pts[j].y, 5f, new Color(0.85f, 0.25f, 0.6f, 0.45f));
                }
            }

            // 余烬亮点
            for (int i = 0; i < 8; i++)
            {
                float ex = 8f + (float)rng.NextDouble() * 240f;
                float ey = 8f + (float)rng.NextDouble() * 240f;
                c.DrawGlow(ex, ey, 5f, new Color(0.95f, 0.35f, 0.18f, 0.4f));
                c.DrawDisc(ex, ey, 1.8f, new Color(1f, 0.5f, 0.28f, 0.95f));
            }

            ApplyTileEdges(c, new Color(0.40f, 0.28f, 0.50f), new Color(0.02f, 0.015f, 0.03f),
                new Color(0.035f, 0.025f, 0.055f), new Color(0.02f, 0.015f, 0.03f));
            ApplyGrain(c, 0.05f, 41, 0.085f, 0.085f);
            ApplyVerticalShade(c, 1.05f, 0.86f);
            return c;
        }

        // ==================================================================
        // 装饰 props（基准勇者身高 1.25u ≈ 125px，装饰都在 0.4-0.7u 量级）
        // ==================================================================

        // ---- 草丛三变体：基部土丘 + 扇形叶簇（下段深色粗、上段亮色细） ----
        private static Raster BuildGrassTuft(int variant)
        {
            var c = new Raster(64, 48);
            var rng = new System.Random(20260600 + variant);
            c.DrawDisc(32f, 5f, 8.5f, new Color(0.19f, 0.145f, 0.105f, 0.95f));

            Color dark, bright;
            switch (variant)
            {
                case 1: dark = new Color(0.24f, 0.33f, 0.11f); bright = new Color(0.58f, 0.66f, 0.26f); break; // 黄绿
                case 2: dark = new Color(0.12f, 0.22f, 0.12f); bright = new Color(0.32f, 0.48f, 0.21f); break; // 暗绿
                default: dark = new Color(0.15f, 0.30f, 0.12f); bright = new Color(0.42f, 0.62f, 0.24f); break; // 正绿
            }

            int n = 9 + variant * 2;
            for (int i = 0; i < n; i++)
            {
                float bx = 32f + ((float)rng.NextDouble() - 0.5f) * 30f;
                float lean = (bx - 32f) * 0.09f + ((float)rng.NextDouble() - 0.5f) * 11f;
                float h = 15f + (float)rng.NextDouble() * 20f;
                float midX = bx + lean * 0.4f;
                c.DrawLineSegment(bx, 4f, midX, 4f + h * 0.5f, 2.8f, dark);
                c.DrawLineSegment(midX, 4f + h * 0.5f, bx + lean, 4f + h, 1.5f, bright);
            }
            return c;
        }

        // ---- 野花：弯茎双叶 + 五瓣白花金黄芯 ----
        private static Raster BuildFlower()
        {
            var c = new Raster(48, 56);
            c.DrawLineSegment(24f, 3f, 21f, 26f, 2.4f, new Color(0.19f, 0.35f, 0.13f, 1f));
            c.DrawLineSegment(21f, 26f, 24f, 41f, 2f, new Color(0.22f, 0.40f, 0.15f, 1f));
            c.FillPolygon(new[] { new Vector2(21.5f, 22f), new Vector2(13f, 27f), new Vector2(10f, 21f), new Vector2(17f, 16.5f) },
                new Color(0.24f, 0.43f, 0.16f, 1f));
            c.FillPolygon(new[] { new Vector2(22f, 12f), new Vector2(30f, 17f), new Vector2(34f, 11f), new Vector2(28f, 6.5f) },
                new Color(0.21f, 0.39f, 0.14f, 1f));
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI * 2f * i / 5f;
                c.DrawDisc(24f + Mathf.Cos(a) * 6f, 43f + Mathf.Sin(a) * 6f, 5.6f, new Color(0.94f, 0.93f, 0.88f, 0.96f));
            }
            c.DrawDisc(24f, 43f, 4.2f, new Color(0.95f, 0.72f, 0.22f, 1f));
            return c;
        }

        // ---- 蘑菇：淡暖灰菌盖（刻意低饱和——LevelBuilder 按关染色成荧光蓝/深渊紫） ----
        private static Raster BuildMushroom()
        {
            var c = new Raster(48, 56);
            // 菌柄 + 右侧背光
            c.FillPolygon(new[] { new Vector2(19f, 4f), new Vector2(29f, 4f), new Vector2(27.5f, 30f), new Vector2(20.5f, 30f) },
                new Color(0.85f, 0.81f, 0.73f, 1f));
            c.FillPolygon(new[] { new Vector2(24.5f, 4f), new Vector2(29f, 4f), new Vector2(27.5f, 30f), new Vector2(24.5f, 30f) },
                new Color(0.72f, 0.68f, 0.60f, 0.8f));
            // 菌盖穹顶（与 BuildSlime 同款半圆弧造法）
            int n = 13;
            var dome = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * (i / (float)(n - 1));
                dome[i] = new Vector2(24f + Mathf.Cos(a) * 20f, 30f + Mathf.Sin(a) * 16f);
                dome[2 * n - 1 - i] = new Vector2(24f + Mathf.Cos(a) * 20f, 30f);
            }
            c.FillPolygon(dome, new Color(0.80f, 0.74f, 0.68f, 1f));
            // 盖顶受光弧
            var arc = new Vector2[7];
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.PI * (0.2f + 0.6f * i / 6f);
                arc[i] = new Vector2(24f + Mathf.Cos(a) * 17.5f, 31f + Mathf.Sin(a) * 14f);
            }
            DrawPolyline(c, new List<Vector2>(arc), 2.2f, new Color(0.92f, 0.88f, 0.82f, 0.9f));
            // 白点斑 + 菌褶阴影线
            c.DrawDisc(17f, 38f, 3.2f, new Color(0.97f, 0.95f, 0.92f, 0.95f));
            c.DrawDisc(29f, 41f, 2.6f, new Color(0.97f, 0.95f, 0.92f, 0.95f));
            c.DrawDisc(24f, 34.5f, 2.2f, new Color(0.97f, 0.95f, 0.92f, 0.9f));
            c.DrawLineSegment(7f, 30f, 41f, 30f, 2.2f, new Color(0.45f, 0.38f, 0.32f, 0.75f));
            return c;
        }

        // ---- 岩石：平滑不规则轮廓 + 受光/背光面 + 描边棱线 ----
        private static Raster BuildRock()
        {
            var c = new Raster(64, 44);
            int n = 9;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                float rx = 20f + Mathf.Sin(a * 2f + 1.3f) * 3.5f + Mathf.Sin(a * 3f) * 1.5f;
                float ry = 13f + Mathf.Sin(a * 3f + 0.7f) * 2.5f;
                pts[i] = new Vector2(32f + Mathf.Cos(a) * rx, 20f + Mathf.Sin(a) * ry);
            }
            c.FillPolygon(pts, new Color(0.40f, 0.40f, 0.44f, 1f));
            c.DrawDisc(25f, 24f, 8f, new Color(0.52f, 0.52f, 0.57f, 0.85f));   // 顶左受光
            c.DrawDisc(38f, 23f, 5.5f, new Color(0.48f, 0.48f, 0.53f, 0.7f));
            c.DrawDisc(36f, 14f, 8f, new Color(0.28f, 0.28f, 0.33f, 0.75f));   // 底部阴影
            c.StrokePolygon(pts, 2f, new Color(0.20f, 0.20f, 0.24f, 0.95f));
            c.DrawLineSegment(22f, 26f, 30f, 29f, 1.6f, new Color(0.72f, 0.72f, 0.78f, 0.85f));
            return c;
        }

        // ---- 藤蔓：S 形主藤 + 左右交替菱叶 + 末端卷须（竖长图，LevelBuilder 拉长垂挂） ----
        private static Raster BuildVine()
        {
            var c = new Raster(48, 160);
            var rng = new System.Random(20260505);
            var pts = new Vector2[13];
            for (int i = 0; i < 13; i++)
            {
                float t = i / 12f;
                pts[i] = new Vector2(24f + Mathf.Sin(t * 5.2f) * 6.5f, 154f - t * 148f);
            }
            for (int i = 0; i < 12; i++)
                c.DrawLineSegment(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, 3.2f, new Color(0.17f, 0.29f, 0.13f, 1f));
            for (int i = 0; i < 7; i++)
            {
                Vector2 p = pts[1 + i];
                float side = i % 2 == 0 ? 1f : -1f;
                float s = 0.8f + (float)rng.NextDouble() * 0.5f;
                float b = 0.85f + (float)rng.NextDouble() * 0.35f;
                c.FillPolygon(new[]
                {
                    new Vector2(p.x, p.y + 2f),
                    new Vector2(p.x + side * 7f * s, p.y + 6f * s),
                    new Vector2(p.x + side * 12f * s, p.y + 1f),
                    new Vector2(p.x + side * 6f * s, p.y - 4f * s),
                }, new Color(0.22f * b, 0.38f * b, 0.15f * b, 0.96f));
            }
            Vector2 tip = pts[12];
            c.DrawLineSegment(tip.x, tip.y, tip.x + 5f, tip.y - 6f, 2f, new Color(0.17f, 0.29f, 0.13f, 1f));
            c.DrawLineSegment(tip.x + 5f, tip.y - 6f, tip.x + 1f, tip.y - 10f, 2f, new Color(0.17f, 0.29f, 0.13f, 1f));
            return c;
        }

        // ---- 树桩：树身皮纹 + 年轮断面 + 根部外张 ----
        private static Raster BuildStump()
        {
            var c = new Raster(64, 56);
            var body = new Vector2[] { new(15f, 4f), new(49f, 4f), new(45f, 34f), new(19f, 34f) };
            c.FillPolygon(body, new Color(0.35f, 0.25f, 0.16f, 1f));
            c.FillPolygon(new[] { new Vector2(37f, 4f), new Vector2(49f, 4f), new Vector2(45f, 34f), new Vector2(37f, 34f) },
                new Color(0.26f, 0.18f, 0.11f, 0.85f));
            for (int i = 0; i < 5; i++)
            {
                float x = 20f + i * 5.5f;
                c.DrawLineSegment(x, 6f, x + 1f, 32f, 1.4f, new Color(0.22f, 0.15f, 0.09f, 0.7f));
            }
            c.FillPolygon(new[] { new Vector2(4f, 0f), new Vector2(19f, 0f), new Vector2(15f, 10f) },
                new Color(0.32f, 0.23f, 0.15f, 1f));
            c.FillPolygon(new[] { new Vector2(45f, 0f), new Vector2(60f, 0f), new Vector2(49f, 9f) },
                new Color(0.30f, 0.21f, 0.13f, 1f));
            // 年轮断面：三圈椭圆填充环
            EllipseFill(c, 32f, 38f, 15f, 6f, new Color(0.56f, 0.45f, 0.30f, 1f));
            EllipseFill(c, 32f, 38f, 10f, 4f, new Color(0.47f, 0.37f, 0.25f, 1f));
            EllipseFill(c, 32f, 38f, 5f, 2f, new Color(0.57f, 0.46f, 0.31f, 1f));
            return c;
        }

        // ---- 远景树剪影：单株大冠乔木，深色低饱和半透明（LevelBuilder 逐株成林 + 染色） ----
        private static Raster BuildTreeFar()
        {
            var c = new Raster(512, 512);
            Color bark = new Color(0.10f, 0.125f, 0.10f, 0.62f);
            Color canopy = new Color(0.105f, 0.14f, 0.105f, 0.62f);
            Color canopyLite = new Color(0.15f, 0.19f, 0.145f, 0.40f);
            // 树干（下宽上窄微倾）+ 根部外张 + 两根主枝
            c.FillPolygon(new[]
            {
                new Vector2(196f, 0f), new Vector2(300f, 0f), new Vector2(282f, 60f),
                new Vector2(268f, 210f), new Vector2(252f, 210f), new Vector2(236f, 70f),
            }, bark);
            c.FillPolygon(new[] { new Vector2(150f, 0f), new Vector2(212f, 0f), new Vector2(196f, 34f) }, bark);
            c.FillPolygon(new[] { new Vector2(288f, 0f), new Vector2(352f, 0f), new Vector2(304f, 30f) }, bark);
            c.DrawLineSegment(258f, 200f, 200f, 262f, 9f, bark);
            c.DrawLineSegment(262f, 210f, 330f, 268f, 8f, bark);
            // 树冠：错落圆冠叠簇 + 上层受光冠
            var blobs = new[]
            {
                new Vector2(190f, 328f), new Vector2(292f, 340f), new Vector2(246f, 408f),
                new Vector2(142f, 296f), new Vector2(352f, 300f), new Vector2(258f, 262f),
            };
            var radii = new[] { 96f, 104f, 88f, 74f, 78f, 84f };
            for (int i = 0; i < blobs.Length; i++)
                c.DrawDisc(blobs[i].x, blobs[i].y, radii[i], canopy);
            for (int i = 0; i < blobs.Length; i++)
                c.DrawDisc(blobs[i].x + 10f, blobs[i].y + 16f, radii[i] * 0.72f, canopyLite);
            return c;
        }

        // ==================================================================
        // 陷阱 / 传送门 / 氛围层
        // ==================================================================

        // ---- 漩涡传送门：三层紫蓝光环 + 对数螺线旋臂 + 内核光斑 + 外圈粒子尘（竖长 192×256） ----
        private static Raster BuildPortalVortex()
        {
            var c = new Raster(192, 256);
            var rng = new System.Random(20260404);
            float cx = 96f, cy = 128f;
            // 环境紫雾 + 上下竖向余晖（漩涡在竖长画布里的拖尾感）
            c.DrawRadialFade(cx, cy, 92f, new Color(0.42f, 0.22f, 0.85f, 0.30f), 2.2f);
            c.DrawRadialFade(cx, 232f, 34f, new Color(0.5f, 0.3f, 0.9f, 0.30f), 2f);
            c.DrawRadialFade(cx, 24f, 34f, new Color(0.5f, 0.3f, 0.9f, 0.30f), 2f);
            // 多层渐变光环（外紫 → 中蓝紫 → 内亮）
            c.DrawRing(cx, cy, 80f, 7f, new Color(0.52f, 0.32f, 1.0f, 0.85f));
            c.DrawRing(cx, cy, 65f, 6f, new Color(0.42f, 0.52f, 1.15f, 0.9f));
            c.DrawRing(cx, cy, 50f, 5f, new Color(0.62f, 0.55f, 1.25f, 0.95f));
            // 三条对数螺线旋臂（内收到核心）
            for (int arm = 0; arm < 3; arm++)
            {
                var pts = new Vector2[14];
                for (int i = 0; i < 14; i++)
                {
                    float t = i / 13f;
                    float ang = arm * (Mathf.PI * 2f / 3f) + t * 4.2f;
                    float r = 46f * Mathf.Pow(0.82f, t * 3f);
                    pts[i] = new Vector2(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
                }
                for (int i = 0; i < 13; i++)
                    c.DrawLineSegment(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, 3f - i * 0.12f,
                        new Color(0.8f, 0.85f, 1.3f, 0.65f));
            }
            // 内核光斑
            c.DrawRadialFade(cx, cy, 40f, new Color(0.85f, 0.75f, 1.30f, 0.85f), 1.8f);
            c.DrawDisc(cx, cy, 17f, new Color(1.0f, 0.95f, 1.35f, 1f));
            c.DrawDisc(cx - 5f, cy + 4f, 7f, new Color(1.0f, 1.0f, 1.4f, 0.9f));
            // 外圈粒子感噪点：光环带上明暗尘点（亮的带微光晕）
            for (int i = 0; i < 34; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 72f + (float)rng.NextDouble() * 22f;
                float px = cx + Mathf.Cos(a) * r;
                float py = cy + Mathf.Sin(a) * r * 0.92f;
                float s = 1.2f + (float)rng.NextDouble() * 2.2f;
                bool bright = rng.Next(0, 3) == 0;
                c.DrawDisc(px, py, s, bright ? new Color(0.85f, 0.8f, 1.2f, 0.75f) : new Color(0.35f, 0.22f, 0.7f, 0.5f));
                if (bright) c.DrawGlow(px, py, 4.5f, new Color(0.7f, 0.6f, 1.1f, 0.5f));
            }
            return c;
        }

        // ---- 金属尖刺条：暗钢底座铆钉 + 五根银灰三面渐变尖刺 + 中脊高光 ----
        private static Raster BuildSpikesMetal()
        {
            var c = new Raster(192, 64);
            c.FillPolygon(RectPoly(0, 0, 192, 14), new Color(0.145f, 0.155f, 0.185f, 1f));
            c.FillPolygon(RectPoly(0, 11, 192, 3), new Color(0.42f, 0.45f, 0.52f, 0.9f));
            c.FillPolygon(RectPoly(0, 0, 192, 3), new Color(0.08f, 0.085f, 0.10f, 1f));
            for (int i = 0; i < 4; i++)
            {
                float bx = 24f + i * 48f;
                c.DrawDisc(bx, 7f, 3.4f, new Color(0.5f, 0.53f, 0.6f, 1f));
                c.DrawDisc(bx - 1f, 8f, 1.3f, new Color(0.75f, 0.78f, 0.85f, 0.9f));
            }
            for (int i = 0; i < 5; i++)
            {
                float x = 19.2f + i * 38.4f;
                c.FillPolygon(new Vector2[] { new(x - 14.5f, 12f), new(x, 61f), new(x + 14.5f, 12f) },
                    new Color(0.58f, 0.61f, 0.67f, 1f));
                c.FillPolygon(new Vector2[] { new(x - 14.5f, 12f), new(x, 61f), new(x - 1.5f, 12f) },
                    new Color(0.40f, 0.42f, 0.48f, 1f));   // 左暗面
                c.FillPolygon(new Vector2[] { new(x - 1.5f, 12f), new(x, 61f), new(x + 14.5f, 12f) },
                    new Color(0.72f, 0.75f, 0.82f, 1f));   // 右亮面
                c.DrawLineSegment(x, 14f, x, 58f, 1.6f, new Color(0.95f, 0.97f, 1.0f, 0.9f));
                c.DrawDisc(x, 59.5f, 1.6f, new Color(1f, 1f, 1.05f, 1f));
            }
            return c;
        }

        // ---- 雾带：垂直软边 + 两端淡出 + 双频噪声团块 + 三团亮芯（半透明白，LevelBuilder 按关染色） ----
        private static Raster BuildFogStrip()
        {
            var c = new Raster(512, 128);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 512; x++)
                {
                    float u = x / 511f;
                    float v = y / 127f;
                    float vert = Mathf.Sin(Mathf.PI * v);
                    vert *= vert; // 中间浓、上下淡出
                    float hEnv = Mathf.Min(Mathf.SmoothStep(0f, 0.16f, u), Mathf.SmoothStep(1f, 0.84f, u));
                    hEnv = 0.10f + 0.90f * hEnv; // 端点残留少量浓度：相邻拼接不露硬断
                    float n = Fbm(x * 0.016f, y * 0.045f, 77);
                    float n2 = Fbm(x * 0.05f, y * 0.12f, 78);
                    float a = 0.55f * vert * hEnv * (0.45f + 0.75f * n) * (0.7f + 0.5f * n2);
                    c.Set(x, y, new Color(0.86f, 0.90f, 0.95f, Mathf.Clamp01(a)));
                }
            c.DrawRadialFade(150f, 64f, 42f, new Color(0.95f, 0.97f, 1f, 0.14f), 2f);
            c.DrawRadialFade(330f, 56f, 50f, new Color(0.95f, 0.97f, 1f, 0.12f), 2f);
            c.DrawRadialFade(430f, 70f, 36f, new Color(0.95f, 0.97f, 1f, 0.10f), 2f);
            return c;
        }

        // ---- 全屏暗角：中心全透明 → 边缘半透明黑（SpriteRenderer 盖全屏，中心不挡战斗） ----
        private static Raster BuildVignette()
        {
            var c = new Raster(512, 512);
            float cx = 255.5f, cy = 255.5f;
            for (int y = 0; y < 512; y++)
                for (int x = 0; x < 512; x++)
                {
                    float dx = (x - cx) / 256f;
                    float dy = (y - cy) / 256f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy); // 1 = 边中点，1.414 = 角
                    float k = Mathf.Clamp01((d - 0.55f) / 0.5f);
                    float a = 0.55f * k * k * (3f - 2f * k);
                    c.Set(x, y, new Color(0.01f, 0.01f, 0.02f, a));
                }
            return c;
        }
    }
}
