using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StarVortex.EditorTools
{
    /// <summary>
    /// 游戏文字字符集：烘焙中文字体图集前，先算出游戏里到底会显示哪些字。
    /// 面试点：静态字体图集只包含烘焙过的字，手工维护字表时，改一句文案就可能漏字，
    /// 游戏里显示成方框。这里直接扫描脚本里的字符串字面量，和手工字表取并集，
    /// 文案改到哪、图集就补到哪。日志、Inspector 标签、编辑器菜单这些玩家看不到的文字不计入，避免图集膨胀。
    /// </summary>
    public static class GameTextCharset
    {
        private const string ScriptsRoot = "Assets/Scripts";

        // C# 字符串字面量：普通 / 插值 / 逐字三种写法
        private static readonly Regex StringLiteral = new(@"\$?@?""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        // 这些行里的字符串只出现在日志或编辑器界面，不会显示给玩家
        private static readonly string[] EditorOnlyMarkers =
        {
            "Debug.", "[Header(", "[Tooltip(", "[MenuItem(", "DisplayProgressBar", "DisplayDialog",
        };

        /// <summary>手工字表 ∪ 脚本字面量里的全部中文与全角标点（去重，顺序稳定）。</summary>
        public static string Collect(string baseCharacters)
        {
            var chars = new SortedSet<char>(baseCharacters.Where(IsDisplayCharacter));
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScriptsRoot);

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(file, Encoding.UTF8))
                {
                    string code = line.TrimStart();
                    if (code.StartsWith("//") || EditorOnlyMarkers.Any(marker => code.Contains(marker))) continue;

                    foreach (Match literal in StringLiteral.Matches(code))
                        foreach (char c in literal.Groups[1].Value)
                            if (IsDisplayCharacter(c)) chars.Add(c);
                }
            }
            return new string(chars.ToArray());
        }

        /// <summary>需要进字体图集的字符：CJK 统一汉字与常用全角标点（ASCII 由 TMP 默认字体负责）。</summary>
        private static bool IsDisplayCharacter(char c) =>
            (c >= '\u4e00' && c <= '\u9fff') || "·×—…★、。《》「」『』，！？：；（）".IndexOf(c) >= 0;
    }
}
