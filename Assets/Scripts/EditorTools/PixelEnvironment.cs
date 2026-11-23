using System.Collections.Generic;
using StarVortex.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 三关的像素环境：多层视差背景 + 九宫格地块（素材见 Assets/Art/Pixel/Env/L1~L3，出处见 CREDITS.md）。
    /// 面试点：
    /// ① 背景层高 224px = 7u = 相机视口高（正交尺寸 3.5），PPU 与角色同为 32，背景和人物像素一样大——
    ///    之前"贴纸感"的一大来源就是 1024px 的大图缩到屏幕上，背景像素比人物细好几倍；
    /// ② 视差靠 ParallaxScroller 的跟随系数（0 = 无限远贴着相机，1 = 和地面同速），远层慢、近层快；
    ///    每层用 SpriteRenderer 平铺模式铺满"相机全程可能看到的范围"，长度由关卡宽度和系数推出来；
    /// ③ 地面一段一张九宫格小图（几十像素），平铺模式下四角不变形、边和中间重复，任意长度、任意高度的台阶都能拼。
    /// </summary>
    public static class PixelEnvironment
    {
        private const string EnvRoot = PixelArtPipeline.ArtRoot + "/Env";

        /// <summary>地块与碰撞体的底部（世界 y）：低于坠落线 -5 不需要，低于视口底（-0.7）即可，留点余量。</summary>
        public const float TerrainBottomY = -3f;

        /// <summary>相机视口相对锁定地面的底边偏移：SideCamera.groundLift 2.8 − 正交尺寸 3.5。</summary>
        private const float ViewBottomOffset = -0.7f;

        internal readonly struct Layer
        {
            public readonly string Name;
            public readonly float Factor;     // 视差跟随系数（与地面同速 = 1）
            public readonly float BottomLift; // 层底边高出视口底边多少（u）
            public readonly Color Tint;

            public Layer(string name, float factor, float bottomLift, Color tint)
            {
                Name = name;
                Factor = factor;
                BottomLift = bottomLift;
                Tint = tint;
            }
        }

        public sealed class Look
        {
            public string Folder;
            public Color Sky;           // 相机清屏色 = 最远层顶部颜色，震屏露边时看不出缝
            public Color Light;         // 2D 全局光
            public int GroundSurfacePx; // 地块图顶部到"可站立表面"的像素（草叶、石块凸起高出碰撞面）
            public int PlatformSurfacePx;
            internal Layer[] Layers;
            public Sprite Ground => Load(Folder, "Ground");
            public Sprite Platform => Load(Folder, "Platform") ?? Ground;
        }

        private static readonly Look L1 = new()
        {
            Folder = "L1",
            Sky = new Color32(159, 192, 194, 255),
            Light = new Color(0.92f, 0.98f, 0.95f),
            GroundSurfacePx = 5,
            PlatformSurfacePx = 5,
            Layers = new[]
            {
                new Layer("Bg0", 0.06f, 0f, Color.white),                     // 雾天 + 远山树影
                new Layer("Bg1", 0.18f, 0.4f, new Color(1f, 1f, 1f, 0.85f)),  // 远处树
                new Layer("Bg2", 0.34f, 0.1f, Color.white),                   // 中景大树
                new Layer("Bg3", 0.55f, 0f, Color.white),                     // 近景乱石
            },
        };

        private static readonly Look L2 = new()
        {
            Folder = "L2",
            Sky = new Color32(52, 26, 55, 255),
            Light = new Color(1f, 0.86f, 0.8f),
            GroundSurfacePx = 3,
            PlatformSurfacePx = 3,
            Layers = new[]
            {
                new Layer("Bg0", 0.08f, 0f, Color.white),   // 熔岩洞窟
                new Layer("Bg1", 0.3f, 0f, Color.white),    // 岩浆石柱
            },
        };

        private static readonly Look L3 = new()
        {
            Folder = "L3",
            Sky = new Color32(15, 34, 41, 255),
            Light = new Color(0.86f, 0.9f, 1f),
            GroundSurfacePx = 2,
            PlatformSurfacePx = 2,
            Layers = new[]
            {
                new Layer("Bg0", 0.12f, 0f, Color.white),   // 古堡内殿：彩窗与拱门
                new Layer("Bg1", 0.42f, 0.7f, Color.white), // 骷髅石柱（离线压暗过，退到背景里）
            },
        };

        public static Look For(int levelIndex) => levelIndex switch
        {
            2 => L2,
            3 => L3,
            _ => L1,
        };

        // ---------- 背景 ----------

        /// <summary>
        /// 按层生成视差背景。覆盖范围推导：层内一点的世界 x = x0 + camX·(1−f)，屏幕上可见 ⇔ x0 − f·camX ∈ [−半视宽, +半视宽]，
        /// camX ∈ [0, 关卡宽]，所以 x0 要覆盖 [−半视宽 − 余量, f·关卡宽 + 半视宽 + 余量]。
        /// </summary>
        public static void BuildParallax(Look look, float levelWidth, float groundY)
        {
            const float margin = 14f; // ≥ 超宽屏半视宽（3.5 × 21/9 ≈ 8.2）+ 震屏余量
            var root = new GameObject("Parallax");
            float viewBottom = groundY + ViewBottomOffset;

            for (int i = 0; i < look.Layers.Length; i++)
            {
                var layer = look.Layers[i];
                var sprite = Load(look.Folder, layer.Name);
                if (sprite == null)
                {
                    Debug.LogWarning($"[Env] 缺少背景层：{EnvRoot}/{look.Folder}/{layer.Name}.png");
                    continue;
                }

                var band = new GameObject($"{layer.Name}_x{layer.Factor:0.00}");
                band.transform.SetParent(root.transform, false);
                band.AddComponent<ParallaxScroller>().Configure(layer.Factor);

                float left = -margin;
                float right = layer.Factor * levelWidth + margin;
                float height = sprite.bounds.size.y;

                var go = new GameObject("Tiles");
                go.transform.SetParent(band.transform, false);
                go.transform.localPosition = new Vector3((left + right) * 0.5f, viewBottom + layer.BottomLift + height * 0.5f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                sr.size = new Vector2(Snap(right - left, sprite), height);
                sr.color = layer.Tint;
                sr.sortingOrder = -60 + i * 2;
            }
        }

        // ---------- 地块 ----------

        /// <summary>相邻且同高的地面段合并成一块：否则每段两端都会画出侧边，平地上出现一道道竖缝。</summary>
        public static List<GroundSegment> MergeRuns(IEnumerable<GroundSegment> segments)
        {
            var sorted = new List<GroundSegment>(segments);
            sorted.Sort((a, b) => a.startX.CompareTo(b.startX));
            var runs = new List<GroundSegment>();
            foreach (var s in sorted)
            {
                if (s.endX <= s.startX) continue;
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && Mathf.Approximately(last.endX, s.startX) && Mathf.Approximately(last.groundY, s.groundY))
                    last.endX = s.endX;
                else
                    runs.Add(new GroundSegment { startX = s.startX, endX = s.endX, groundY = s.groundY });
            }
            return runs;
        }

        /// <summary>给一块地面挂九宫格平铺视觉（独立子物体，碰撞体留在父物体上不受影响）。</summary>
        public static void AddGroundVisual(Transform owner, Look look, float startX, float endX, float topY)
        {
            var sprite = look.Ground;
            if (sprite == null) return;
            float surface = look.GroundSurfacePx / PixelArtPipeline.PixelsPerUnit;
            float top = topY + surface;
            float height = top - TerrainBottomY;
            AddTiled(owner, "Visual", sprite, new Vector2((startX + endX) * 0.5f, TerrainBottomY + height * 0.5f),
                new Vector2(endX - startX, height), 0);
        }

        /// <summary>悬浮平台视觉：整张平台图的高度，表面对齐平台顶面。</summary>
        public static void AddPlatformVisual(Transform owner, Look look, float startX, float width, float topY)
        {
            var sprite = look.Platform;
            if (sprite == null) return;
            float surface = look.PlatformSurfacePx / PixelArtPipeline.PixelsPerUnit;
            float height = sprite.bounds.size.y;
            float top = topY + surface;
            AddTiled(owner, "Visual", sprite, new Vector2(startX + width * 0.5f, top - height * 0.5f),
                new Vector2(width, height), 0);
        }

        private static void AddTiled(Transform owner, string name, Sprite sprite, Vector2 center, Vector2 size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(owner, false);
            go.transform.position = new Vector3(center.x, center.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
            sr.sortingOrder = order;
        }

        private static float Snap(float width, Sprite sprite)
        {
            // 背景层宽度取整张图的整数倍：平铺接缝落在图片边界上，不会切出半张
            float tile = sprite.bounds.size.x;
            return Mathf.Ceil(width / tile) * tile;
        }

        private static Sprite Load(string folder, string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{EnvRoot}/{folder}/{name}.png");
    }
}
