using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 像素美术导入管线：Assets/Art/Pixel 下的所有帧统一按像素风规则导入。
    /// 面试点：
    /// ① 全项目只有一个 PPU（32）——角色、怪物、特效同一像素密度，画面才不会出现"粗细不一的贴纸感"；
    /// ② Point 过滤 + 不压缩 + 无 Mipmap：双线性过滤会把像素边缘糊开，压缩会产生色块；
    /// ③ 角色 pivot 统一在脚底中心：帧已在离线脚本里按身体中线和脚底对齐，Unity 里不需要逐帧调 pivot，
    ///    翻转（flipX）也以脚底为轴，转身时人物不会横跳；Fx 目录（弹丸、特效）与 Env 目录（背景、地块）pivot 在中心；
    /// ④ 地块用九宫格：同名 .border.txt 里写"左 下 右 上"四个像素边距，导入时写进 spriteBorder，
    ///    SpriteRenderer 平铺模式下角不变形、边和中间平铺，任意长度的地面都能用同一张小图拼出来。
    /// </summary>
    public static class PixelArtPipeline
    {
        public const string ArtRoot = "Assets/Art/Pixel";
        public const float PixelsPerUnit = 32f;
        private const string FxFolder = ArtRoot + "/Fx/";
        private const string EnvFolder = ArtRoot + "/Env/";

        public static bool IsPixelArt(string assetPath) =>
            assetPath.Replace('\\', '/').StartsWith(ArtRoot + "/", StringComparison.Ordinal);

        /// <summary>把像素导入规则写进 TextureImporter（导入前回调与构建期补救共用这一份规则）。</summary>
        public static void ApplyImportRules(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)AlignmentFor(importer.assetPath);
            settings.spriteMeshType = SpriteMeshType.FullRect; // 整矩形网格：紧凑网格会在帧间切换时裁出不同轮廓（平铺模式也要求它）
            settings.spriteBorder = BorderFor(importer.assetPath);
            importer.SetTextureSettings(settings);
        }

        /// <summary>
        /// 构建期补救：若帧在导入规则脚本编译之前就被导入过（首次打开项目时可能发生），按规则重新导入。
        /// </summary>
        public static void EnsureImported()
        {
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(ArtRoot)) return;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer || !NeedsReimport(importer)) continue;
                ApplyImportRules(importer);
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// 读取某角色某动作的全部帧（文件名约定：动作名_两位序号.png，例如 Attack_00.png）。
        /// 用文件名前缀精确匹配：模糊搜索会把 AirAttack_00 误算进 Attack 动作。
        /// </summary>
        public static Sprite[] LoadFrames(string character, string action)
        {
            string dir = $"{ArtRoot}/{character}";
            if (!Directory.Exists(dir)) return Array.Empty<Sprite>();

            return Directory.GetFiles(dir, action + "_*.png")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => AssetDatabase.LoadAssetAtPath<Sprite>(p))
                .Where(s => s != null)
                .ToArray();
        }

        /// <summary>递归创建资产目录（AssetDatabase.CreateFolder 只能逐级创建）。</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool NeedsReimport(TextureImporter importer)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            return importer.textureType != TextureImporterType.Sprite
                || !Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)
                || importer.filterMode != FilterMode.Point
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.mipmapEnabled
                || settings.spriteAlignment != (int)AlignmentFor(importer.assetPath)
                || settings.spriteBorder != BorderFor(importer.assetPath);
        }

        /// <summary>九宫格边距：读取同名 .border.txt（"左 下 右 上"，像素）；没有则为 0。</summary>
        private static Vector4 BorderFor(string assetPath)
        {
            string file = Path.ChangeExtension(assetPath, null) + ".border.txt";
            if (!File.Exists(file)) return Vector4.zero;
            var parts = File.ReadAllText(file).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return Vector4.zero;
            float Read(int i) => float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            return new Vector4(Read(0), Read(1), Read(2), Read(3));
        }

        private static SpriteAlignment AlignmentFor(string assetPath)
        {
            string path = assetPath.Replace('\\', '/');
            return path.StartsWith(FxFolder, StringComparison.Ordinal) || path.StartsWith(EnvFolder, StringComparison.Ordinal)
                ? SpriteAlignment.Center
                : SpriteAlignment.BottomCenter;
        }
    }

    /// <summary>导入前回调：新放进 Assets/Art/Pixel 的帧自动套用像素导入规则，无需手动设置。</summary>
    internal sealed class PixelArtImportRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!PixelArtPipeline.IsPixelArt(assetPath)) return;
            PixelArtPipeline.ApplyImportRules((TextureImporter)assetImporter);
        }
    }
}
