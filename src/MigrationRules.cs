using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace McPackStudio
{
    internal enum PlanActionKind
    {
        Keep,       // 原样复制
        Rename,     // 路径重命名
        Rewrite,    // 内容改写
        Delete,     // 删除
        Exclude     // 被用户排除
    }

    internal sealed class FileAction
    {
        public string From;                  // 源相对路径
        public string To;                    // 目标相对路径
        public PlanActionKind Kind = PlanActionKind.Keep;
        public List<string> Notes = new List<string>();
        public byte[] NewContent;            // 改写后的字节（Rewrite 用）
        public string RuleId;                // 触发的规则

        public bool IsRenameOnly
        {
            get { return Kind == PlanActionKind.Rename; }
        }
    }

    /// <summary>
    /// 一条迁移规则。PackFormat 小于 Boundary 表示"扁平化之前的旧格式"。
    /// </summary>
    internal sealed class MigrationRule
    {
        public string Id;
        public string Title;
        public string Detail;

        /// <summary>只在跨越这个边界时触发（pack_format &lt; Boundary 视为旧格式）</summary>
        public int Boundary = 4;

        /// <summary>路径前缀重命名：OldPrefix → NewPrefix（相对 pack 根，小写比较）</summary>
        public string OldPrefix;
        public string NewPrefix;

        public bool AppliesToFileName;

        public bool Crosses(int fromFormat, int toFormat)
        {
            bool fromOld = fromFormat < Boundary;
            bool toOld = toFormat < Boundary;
            return fromOld != toOld;
        }

        /// <summary>true 表示从旧格式转向新格式（用 OldPrefix → NewPrefix）</summary>
        public bool IsForward(int fromFormat)
        {
            return fromFormat < Boundary;
        }
    }

    internal static class MigrationRules
    {
        /// <summary>
        /// 全部已知迁移规则。
        ///
        /// 全部集中在 1.13「扁平化」（Flattening，pack_format 3 → 4）这一次变动上，
        /// 这是官方明确记载、可以确定对错的迁移：
        ///   · textures/blocks → textures/block，textures/items → textures/item
        ///   · 语言文件 lang/en_US.lang → lang/en_us.json 时代的上位目录与文件名规范
        ///   · 语言键 block.x → block.minecraft.x（旧版本 tile.x 亦并入）
        ///
        /// 除此之外 Minecraft 各版本还有大量零散变动（个别贴图改名、模型格式调整等），
        /// 那些没有权威对照表，本工具不做猜测性改动 —— 见 README「未做的事」。
        /// </summary>
        public static readonly MigrationRule[] All = new MigrationRule[]
        {
            new MigrationRule
            {
                Id = "flatten-textures-blocks",
                Title = "纹理目录 textures/blocks → textures/block",
                Detail = "1.13 扁平化把方块纹理目录的单复数改掉了；不改的话新版本会找不到贴图。",
                OldPrefix = "textures/blocks/",
                NewPrefix = "textures/block/",
                Boundary = 4,
            },
            new MigrationRule
            {
                Id = "flatten-textures-items",
                Title = "纹理目录 textures/items → textures/item",
                Detail = "同上，物品纹理目录在 1.13 改成了单数。",
                OldPrefix = "textures/items/",
                NewPrefix = "textures/item/",
                Boundary = 4,
            },
        };

        /// <summary>
        /// 语言文件改名规则：扁平化之前地区码用下划线大写（en_US），之后用全小写（en_us）。
        /// 同时 .lang 需要改名为 .json 才是 1.13+ 认的格式（内容转换另行处理）。
        /// </summary>
        public static bool TryMapLangFile(string relPath, int fromFormat, int toFormat, out string newPath, out string note)
        {
            newPath = null;
            note = null;

            string lower = relPath.ToLowerInvariant();
            int idx = lower.IndexOf("/lang/");
            string prefix = "";
            string file;
            if (idx >= 0)
            {
                prefix = relPath.Substring(0, idx + "/lang/".Length);
                file = relPath.Substring(idx + "/lang/".Length);
            }
            else if (lower.StartsWith("lang/"))
            {
                prefix = relPath.Substring(0, "lang/".Length);
                file = relPath.Substring("lang/".Length);
            }
            else
            {
                return false;
            }

            if (file.Contains("/")) return false;   // 语言文件不该有子目录

            bool crossingToNew = fromFormat < 4 && toFormat >= 4;
            bool crossingToOld = fromFormat >= 4 && toFormat < 4;

            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            string ext = System.IO.Path.GetExtension(file);
            if (string.IsNullOrEmpty(stem)) return false;

            // 地区码规范化
            string normalized = NormalizeLocale(stem);
            if (normalized != stem && (crossingToNew || crossingToOld))
            {
                note = "地区码 " + stem + " → " + normalized;
            }

            // 1.13+ 只认 .json
            string newExt = ext;
            if (crossingToNew && (ext.Equals(".lang", StringComparison.OrdinalIgnoreCase) || ext.Length == 0))
            {
                newExt = ".json";
                note = (note == null ? "" : note + "；") + "扩展名 " + (ext.Length == 0 ? "(无)" : ext) + " → .json";
            }

            string candidate = prefix + normalized + newExt;
            if (!string.Equals(candidate, relPath, StringComparison.Ordinal))
            {
                newPath = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 反向（新 → 旧）时把语言文件改回旧版习惯的 .lang —— 旧版本客户端不认 .json。
        /// </summary>
        public static bool TryMapLangFileBackward(string relPath, out string newPath, out string note)
        {
            newPath = null;
            note = null;

            string lower = relPath.ToLowerInvariant();
            if (!lower.EndsWith(".json")) return false;

            int idx = lower.IndexOf("/lang/");
            string prefix;
            string file;
            if (idx >= 0)
            {
                prefix = relPath.Substring(0, idx + "/lang/".Length);
                file = relPath.Substring(idx + "/lang/".Length);
            }
            else if (lower.StartsWith("lang/"))
            {
                prefix = relPath.Substring(0, "lang/".Length);
                file = relPath.Substring("lang/".Length);
            }
            else
            {
                return false;
            }

            if (file.Contains("/")) return false;

            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(stem)) return false;

            // 只处理形如 en_us / zh_cn 的地区码文件，其它 JSON 不动
            if (!Regex.IsMatch(stem, @"^[a-z]{2,3}_[a-z]{2,4}$")) return false;

            string[] parts = stem.Split('_');
            string upper = parts[0] + "_" + parts[1].ToUpperInvariant();

            newPath = prefix + upper + ".lang";
            note = "语言文件回退为旧版格式：" + System.IO.Path.GetFileName(relPath) + " → " + System.IO.Path.GetFileName(newPath);
            return !string.Equals(newPath, relPath, StringComparison.Ordinal);
        }

        /// <summary>把 en_US / zh_CN 这类地区码规范化为 en_us / zh_cn</summary>
        public static string NormalizeLocale(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return stem;
            // xx_YY 或 xx-YY
            var m = Regex.Match(stem, @"^([A-Za-z]{2,3})[-_]([A-Za-z]{2,4})$");
            if (m.Success)
            {
                return m.Groups[1].Value.ToLowerInvariant() + "_" + m.Groups[2].Value.ToLowerInvariant();
            }
            // 已经有 .json 之类后缀的情况由调用方处理，这里只处理纯地区码
            return stem.ToLowerInvariant() == stem ? stem : stem;
        }

        /// <summary>
        /// 语言键迁移：block.x → block.minecraft.x，item.x → item.minecraft.x。
        /// 只管"裸"键（第二段不是已知命名空间），避免把 block.minecraft.x 变成 block.minecraft.minecraft.x。
        /// </summary>
        public static readonly string[] KnownNamespacesHint = new string[]
        {
            "minecraft", "realms"
        };

        public static string MigrateLangKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            string[] groups = new string[] { "block", "item", "tile", "entity" };
            foreach (string g in groups)
            {
                string prefix = g + ".";
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;

                string rest = key.Substring(prefix.Length);
                if (rest.Length == 0) return key;

                // 旧版语言键一律是两段式：block.diamond_ore、item.apple。
                // 出现第三段说明这是 mod 的自定义键（block.mymod.custom），不要乱改。
                if (rest.IndexOf('.') >= 0) return key;

                // 旧版本用 tile. 表示方块
                string targetNs = (g == "tile") ? "block" : g;

                return targetNs + ".minecraft." + rest;
            }
            return key;
        }
    }
}
