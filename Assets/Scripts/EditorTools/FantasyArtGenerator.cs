using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 《勇者远征》横版占位美术：勇者/怪物/平台/陷阱/弹丸（简单 SDF 形状，够看清轮廓与攻击方式）。
    /// AI 成品图（用户生图 → Python 管线 → Assets/Art/Hifi）到位后经 Art() 优先级自动替换，无需改这里。
    /// </summary>
    public static partial class NeonArtGenerator
    {
        /// <summary>入口：GenerateAll 里在星空美术之后调用。</summary>
        public static void GenerateFantasy()
        {
            string absDir = Path.Combine(Application.dataPath, "Art", "Generated");
            Directory.CreateDirectory(absDir);
            int count = 0;
            Emit(BuildHero(), absDir, "hero_0", ref count);
            Emit(BuildSlime(0), absDir, "slime_0", ref count);
            Emit(BuildSlime(1), absDir, "slime_1", ref count);
            Emit(BuildBat(0), absDir, "bat_0", ref count);
            Emit(BuildBat(1), absDir, "bat_1", ref count);
            Emit(BuildMage(0), absDir, "mage_0", ref count);
            Emit(BuildMage(1), absDir, "mage_1", ref count);
            Emit(BuildElite(0), absDir, "elite_0", ref count);
            Emit(BuildElite(1), absDir, "elite_1", ref count);
            Emit(BuildBoss(0), absDir, "boss_0", ref count);
            Emit(BuildBoss(1), absDir, "boss_1", ref count);
            Emit(BuildSpike(), absDir, "spike", ref count);
            Emit(BuildPlatformTile(), absDir, "platform_tile", ref count);
            Emit(BuildSlash(0), absDir, "slash_0", ref count);
            Emit(BuildSlash(1), absDir, "slash_1", ref count);
            Emit(BuildSlash(2), absDir, "slash_2", ref count);
            Emit(BuildFireballSprite(), absDir, "proj_fireball", ref count);
            Emit(BuildArrowSprite(), absDir, "proj_arrow", ref count);
            Emit(BuildSwordWave(), absDir, "proj_swordwave", ref count);
            Emit(BuildPortal(), absDir, "portal", ref count);
            Emit(BuildLightning(), absDir, "lightning", ref count);
            AssetDatabase.SaveAssets();

            // 打包可用通道：运行时 Resources.Load 的精灵复制一份进 Assets/Resources/Sprites
            // （Resources.Load<Sprite> 要求贴图导入类型是 Sprite——必须显式设置，默认导入是 Texture2D）
            string resDir = Path.Combine(Application.dataPath, "Resources", "Sprites");
            Directory.CreateDirectory(resDir);
            foreach (var name in new[] { "glow_disc", "slash_0", "slash_1", "slash_2" })
            {
                string relPath = $"Assets/Resources/Sprites/{name}.png";
                File.Copy(Path.Combine(absDir, name + ".png"), Path.Combine(resDir, name + ".png"), overwrite: true);
                AssetDatabase.ImportAsset(relPath);
                ApplySpriteImport(relPath);
                count++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[FantasyArt] 横版占位美术完成（含 Resources 通道）");
        }

        // ---------- 勇者：银甲蓝披风小骑士，持剑面右 ----------
        private static Raster BuildHero()
        {
            var c = new Raster(96, 96);
            // 披风（身后蓝）
            var cape = new Vector2[] { new(40, 74), new(20, 40), new(34, 16) };
            c.FillPolygon(cape, new Color(0.23f, 0.42f, 0.85f, 0.9f));
            // 躯干银甲
            var body = new Vector2[] { new(44, 78), new(58, 78), new(62, 40), new(40, 40) };
            c.FillPolygon(body, new Color(0.78f, 0.85f, 0.92f, 1f));
            c.StrokePolygon(body, 2f, new Color(0.4f, 0.5f, 0.62f, 1f));
            // 头盔
            c.DrawDisc(51, 84, 10f, new Color(0.85f, 0.9f, 0.96f, 1f));
            c.DrawDisc(55, 84, 3f, new Color(0.1f, 0.14f, 0.2f, 1f)); // 目缝
            // 剑（右下斜出）
            c.DrawLineSegment(60, 52, 86, 26, 4f, new Color(0.92f, 0.96f, 1f, 1f));
            c.DrawDisc(60, 52, 5f, new Color(0.8f, 0.7f, 0.3f, 1f)); // 剑柄
            // 腿
            c.DrawLineSegment(44, 40, 42, 16, 5f, new Color(0.35f, 0.4f, 0.5f, 1f));
            c.DrawLineSegment(58, 40, 60, 16, 5f, new Color(0.35f, 0.4f, 0.5f, 1f));
            return c;
        }

        // ---------- 史莱姆：绿色果冻穹顶，两帧压扁回弹 ----------
        private static Raster BuildSlime(int frame)
        {
            var c = new Raster(72, 72);
            float squash = frame == 0 ? 1f : 0.78f;
            int n = 14;
            var dome = new Vector2[n * 2];
            float rx = 26f, ry = 20f * squash;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * (i / (float)(n - 1));
                dome[i] = new Vector2(36 + Mathf.Cos(a) * rx, 18 + Mathf.Sin(a) * ry);
                dome[2 * n - 1 - i] = new Vector2(36 + Mathf.Cos(a) * rx, 18);
            }
            c.FillPolygon(dome, new Color(0.35f, 0.85f, 0.45f, 0.92f));
            c.FillPolygon(dome, new Color(0.5f, 1f, 0.55f, 0.35f));
            c.DrawDisc(28, 26, 5f, Color.white);
            c.DrawDisc(44, 26, 5f, Color.white);
            c.DrawDisc(29, 26, 2.5f, Color.black);
            c.DrawDisc(45, 26, 2.5f, Color.black);
            return c;
        }

        // ---------- 蝙蝠：紫身+双翼（两帧上下扑动） ----------
        private static Raster BuildBat(int frame)
        {
            var c = new Raster(80, 80);
            float flap = frame == 0 ? 1f : -0.8f;
            var wingL = new Vector2[] { new(34, 42), new(8, 42 + 16 * flap), new(12, 30) };
            var wingR = new Vector2[] { new(46, 42), new(72, 42 + 16 * flap), new(68, 30) };
            c.FillPolygon(wingL, new Color(0.45f, 0.3f, 0.65f, 0.95f));
            c.FillPolygon(wingR, new Color(0.45f, 0.3f, 0.65f, 0.95f));
            c.DrawDisc(40, 40, 11f, new Color(0.3f, 0.2f, 0.45f, 1f));
            c.DrawDisc(36, 44, 3.5f, new Color(1.2f, 0.9f, 0.3f, 1f));
            c.DrawDisc(44, 44, 3.5f, new Color(1.2f, 0.9f, 0.3f, 1f));
            c.FillPolygon(new Vector2[] { new(36, 50), new(40, 56), new(44, 50) }, new Color(0.3f, 0.2f, 0.45f, 1f));
            return c;
        }

        // ---------- 骷髅法师：紫袍+骷髅头+法杖 ----------
        private static Raster BuildMage(int frame)
        {
            var c = new Raster(88, 88);
            var robe = new Vector2[] { new(44, 60), new(22, 14), new(66, 14) };
            c.FillPolygon(robe, new Color(0.25f, 0.18f, 0.38f, 0.96f));
            c.DrawDisc(44, 64, 12f, new Color(0.9f, 0.9f, 0.85f, 1f));
            c.DrawDisc(39, 66, 3.5f, new Color(0.15f, 0.4f, 1f, 1f));
            c.DrawDisc(49, 66, 3.5f, new Color(0.15f, 0.4f, 1f, 1f));
            c.DrawLineSegment(44, 60, 66, 30, 3.5f, new Color(0.55f, 0.42f, 0.25f, 1f)); // 法杖
            c.DrawGlow(66, 30, 7f, frame == 0 ? new Color(0.6f, 0.4f, 1.4f) : new Color(1.4f, 0.5f, 0.6f));
            c.DrawDisc(66, 30, 5f, frame == 0 ? new Color(0.7f, 0.5f, 1f, 1f) : new Color(1f, 0.4f, 0.5f, 1f));
            return c;
        }

        // ---------- 精英·石像卫士：巨型灰岩人，绿苔+亮眼 ----------
        private static Raster BuildElite(int frame)
        {
            var c = new Raster(128, 128);
            var body = new Vector2[] { new(44, 108), new(84, 108), new(92, 28), new(36, 28) };
            c.FillPolygon(body, new Color(0.52f, 0.54f, 0.56f, 1f));
            c.StrokePolygon(body, 3f, new Color(0.34f, 0.36f, 0.38f, 1f));
            var head = new Vector2[] { new(48, 108), new(80, 108), new(78, 86), new(50, 86) };
            c.FillPolygon(head, new Color(0.58f, 0.6f, 0.62f, 1f));
            float eyeGlow = frame == 0 ? 1.1f : 1.8f;
            c.DrawGlow(56, 98, 6f, new Color(0.4f, eyeGlow, 0.45f));
            c.DrawGlow(72, 98, 6f, new Color(0.4f, eyeGlow, 0.45f));
            c.DrawDisc(56, 98, 3f, new Color(0.3f, 1f, 0.45f, 1f));
            c.DrawDisc(72, 98, 3f, new Color(0.3f, 1f, 0.45f, 1f));
            // 苔藓斑
            c.DrawDisc(46, 40, 6f, new Color(0.35f, 0.55f, 0.3f, 0.8f));
            c.DrawDisc(78, 52, 5f, new Color(0.35f, 0.55f, 0.3f, 0.8f));
            // 臂
            c.DrawLineSegment(36, 88, 22, 52, 9f, new Color(0.5f, 0.52f, 0.55f, 1f));
            c.DrawLineSegment(92, 88, 106, 52, 9f, new Color(0.5f, 0.52f, 0.55f, 1f));
            return c;
        }

        // ---------- BOSS·深渊魔王：巨角暗魔，红核，两帧脉动 ----------
        private static Raster BuildBoss(int frame)
        {
            var c = new Raster(160, 160);
            float pulse = frame == 0 ? 1f : 1.35f;
            c.DrawRadialFade(80, 76, 66f, new Color(0.8f, 0.15f, 0.2f, 0.4f), 2f);
            var body = new Vector2[] { new(52, 118), new(108, 118), new(122, 30), new(38, 30) };
            c.FillPolygon(body, new Color(0.16f, 0.11f, 0.2f, 1f));
            c.StrokePolygon(body, 3f, new Color(0.5f, 0.12f, 0.16f, 1f));
            // 双角
            c.FillPolygon(new Vector2[] { new(56, 118), new(40, 148), new(64, 124) }, new Color(0.35f, 0.3f, 0.36f, 1f));
            c.FillPolygon(new Vector2[] { new(104, 118), new(120, 148), new(96, 124) }, new Color(0.35f, 0.3f, 0.36f, 1f));
            // 亮眼
            c.DrawGlow(66, 102, 7f * pulse, new Color(2f, 0.25f, 0.2f));
            c.DrawGlow(94, 102, 7f * pulse, new Color(2f, 0.25f, 0.2f));
            // 胸口红核
            c.DrawGlow(80, 66, 16f * pulse, new Color(2.2f, 0.3f, 0.2f));
            c.DrawDisc(80, 66, 7f, new Color(1.8f, 0.35f, 0.25f, 1f));
            return c;
        }

        // ---------- 尖刺陷阱：三连三角 ----------
        private static Raster BuildSpike()
        {
            var c = new Raster(96, 48);
            Color col = new Color(0.85f, 0.88f, 0.92f, 1f);
            for (int i = 0; i < 3; i++)
            {
                float x = 16 + i * 32;
                c.FillPolygon(new Vector2[] { new(x - 12, 4), new(x, 44), new(x + 12, 4) }, col);
            }
            c.FillPolygon(new Vector2[] { new(2, 4), new(94, 4), new(94, 12), new(2, 12) },
                new Color(0.3f, 0.32f, 0.36f, 1f));
            return c;
        }

        // ---------- 平台/地面砖：石块+顶缘草皮 ----------
        private static Raster BuildPlatformTile()
        {
            var c = new Raster(64, 64);
            c.FillPolygon(new Vector2[] { new(0, 0), new(64, 0), new(64, 64), new(0, 64) },
                new Color(0.32f, 0.3f, 0.28f, 1f));
            c.FillPolygon(new Vector2[] { new(0, 56), new(64, 56), new(64, 64), new(0, 64) },
                new Color(0.42f, 0.58f, 0.34f, 1f));
            c.DrawLineSegment(8, 44, 20, 44, 2f, new Color(0.24f, 0.22f, 0.2f, 1f));
            c.DrawLineSegment(36, 30, 52, 30, 2f, new Color(0.24f, 0.22f, 0.2f, 1f));
            return c;
        }

        // ---------- 剑气弧光（三段连击各一弧：新月刀光——逐像素高斯弧带，替代旧线段拼弧"空心D"） ----------
        // 设计：凸面朝右（面右挥击；flipX 镜像朝左）；炽亮内锋线 + 柔光带 + 两端收尖；
        // 亮度全由 alpha 承载（运行时 additive + 三段染色 白/金/橙红）。烘焙角与 Juice 代码角叠加后
        // 三段实际倾角约 0°/40°/85°（平挥/斜撩/过顶劈）。
        private static Raster BuildSlash(int stage)
        {
            var c = new Raster(256, 256);
            float cx = 88f, cy = 128f;                       // 弧心偏左：右侧留出凸面空间
            float radius = 96f;
            float sweep = 2.3f + stage * 0.15f;              // 弧长（弧度）——细长比 ≥8:1 才读作"刀光"
            float rot = stage == 0 ? 0f : stage == 1 ? -0.22f : -0.45f; // 烘焙部分倾斜（代码再转，叠加为三段递进倾角）
            float band = stage == 2 ? 15f : 11f;             // 弧带半宽：细带才是"挥砍"，胖带是"饼"
            float bandSigma = band * 0.55f;
            float edgeSigma = 2.2f;

            for (int y = 0; y < c.H; y++)
                for (int x = 0; x < c.W; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float ang = Mathf.Atan2(dy, dx) - rot;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Abs(ang) / (sweep * 0.5f); // 0=弧中心，1=端点
                    if (t > 1.05f) continue;
                    float window = 1f - Mathf.SmoothStep(0.7f, 1.05f, t); // 角度软窗：两端自然淡出
                    if (window <= 0f) continue;
                    float taper = Mathf.Pow(1f - Mathf.Clamp01(t), 0.7f); // 中段最厚，向两端收尖
                    // 半径沿角度内旋（起手半径大→收势小）：同心环段读作"饼"，内旋弧才读作"挥出来的刀光"
                    float rCenter = radius + 4f - (22f + stage * 6f) * t;
                    float dr = Mathf.Abs(r - rCenter);
                    float bandA = Mathf.Exp(-(dr * dr) / (2f * bandSigma * bandSigma)) * taper * window;
                    // 外缘炽亮刀锋线（切割前沿），内缘柔光带
                    float de = Mathf.Abs(r - (rCenter + band * 0.35f));
                    float edgeA = Mathf.Exp(-(de * de) / (2f * edgeSigma * edgeSigma)) * taper * window;
                    float a = Mathf.Clamp01(bandA * 0.6f + edgeA * 1.0f);
                    if (a <= 0.004f) continue;
                    float core = Mathf.Clamp01(edgeA);
                    c.Blend(x, y, new Color(0.98f + 0.02f * core, 0.96f + 0.03f * core, 0.9f + 0.08f * core, a));
                }
            return c;
        }

        // ---------- 弹丸三兄弟 ----------
        private static Raster BuildFireballSprite()
        {
            var c = new Raster(48, 48);
            c.DrawGlow(24, 24, 20f, new Color(1.8f, 0.9f, 0.3f));
            c.DrawDisc(24, 24, 10f, new Color(1f, 0.75f, 0.35f, 1f));
            c.DrawDisc(24, 24, 5f, new Color(1f, 0.98f, 0.85f, 1f));
            return c;
        }

        private static Raster BuildArrowSprite()
        {
            var c = new Raster(64, 24);
            c.DrawLineSegment(6, 12, 56, 12, 3f, new Color(0.9f, 0.82f, 0.6f, 1f));
            c.FillPolygon(new Vector2[] { new(56, 6), new(62, 12), new(56, 18) }, new Color(0.85f, 0.88f, 0.92f, 1f));
            c.FillPolygon(new Vector2[] { new(6, 6), new(14, 12), new(6, 18) }, new Color(0.5f, 0.35f, 0.2f, 1f));
            return c;
        }

        private static Raster BuildSwordWave()
        {
            var c = new Raster(64, 64);
            int n = 12;
            for (int i = 0; i < n - 1; i++)
            {
                float a0 = -1.2f + 2.4f * (i / (float)(n - 1));
                float a1 = -1.2f + 2.4f * ((i + 1) / (float)(n - 1));
                c.DrawLineSegment(14 + Mathf.Cos(a0) * 40, 32 + Mathf.Sin(a0) * 40,
                    14 + Mathf.Cos(a1) * 40, 32 + Mathf.Sin(a1) * 40, 6f, new Color(0.7f, 0.95f, 1.4f, 0.95f));
            }
            return c;
        }

        // ---------- 传送门：竖长发光漩涡 ----------
        private static Raster BuildPortal()
        {
            var c = new Raster(96, 192);
            for (int i = 0; i < 6; i++)
            {
                float y = 40 + i * 22;
                float r = 30f - Mathf.Abs(i - 2.5f) * 6f;
                c.DrawRadialFade(48, y, r, new Color(0.4f, 1.1f, 1.3f, 0.4f), 2f);
            }
            c.DrawRing(48, 96, 30f, 5f, new Color(0.5f, 1.2f, 1.4f, 0.9f));
            c.DrawRing(48, 96, 38f, 2f, new Color(0.4f, 1f, 1.2f, 0.5f));
            return c;
        }

        // ---------- 落雷 ----------
        private static Raster BuildLightning()
        {
            var c = new Raster(64, 128);
            float x = 32;
            for (int i = 0; i < 8; i++)
            {
                float y0 = 120 - i * 15;
                float y1 = 120 - (i + 1) * 15;
                float nx = x + ((i % 2 == 0) ? 12 : -12);
                c.DrawLineSegment(x, y0, nx, y1, 4f, new Color(0.75f, 0.9f, 1.6f, 1f));
                x = nx;
            }
            return c;
        }
    }
}
