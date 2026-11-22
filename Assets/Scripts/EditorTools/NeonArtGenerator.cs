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
    public static partial class NeonArtGenerator
    {
        // 输出目录与统一 PPU
        const string OutputRelDir = "Assets/Art/Generated";
        const float PixelsPerUnit = 100f;

        /// <summary>菜单入口：一次性生成全部美术资源。</summary>
        [MenuItem("StarVortex/生成美术资源")]
        public static void GenerateAll()
        {
            int count = 0;
            string absDir = Path.Combine(Application.dataPath, "Art", "Generated");
            if (!Directory.Exists(absDir))
                Directory.CreateDirectory(absDir);

            // ---- 粒子 / 特效（打击感通用件） ----
            Emit(BuildParticleSoft(), absDir, "particle_soft", ref count);
            Emit(BuildParticleSpark(), absDir, "particle_spark", ref count);
            Emit(BuildRingShockwave(), absDir, "ring_shockwave", ref count);
            Emit(BuildGlowDisc(), absDir, "glow_disc", ref count);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NeonArt] 生成 {count} 张特效精灵 → Assets/Art/Generated（角色/背景由 Hifi 管线提供）");
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
            // 光晕类精灵避免紧缩网格裁切：spriteMeshType 要经 TextureImporterSettings 设置（2022.3 无直接属性）
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // ==================================================================
        // 精灵构建
        // ==================================================================

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
    }
}
