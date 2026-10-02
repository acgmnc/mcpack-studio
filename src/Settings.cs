using System;
using System.Collections.Generic;
using System.Linq;

namespace McPackStudio
{
    /// <summary>
    /// 版本对照表。数据来源：原项目 Little100/Minecraft-Resourcespack-Editor 的
    /// public/version_map/version_map.json（GPL-3.0），由脚本生成为 C# 表。
    /// 每项为 { pack_format, 该格式对应的全部版本号（从新到旧） }。
    /// </summary>
    internal static class Settings
    {
        public const string AppName = "MCPack Studio";
        public const string AppVersion = "1.1.0";
        public const string AppTagline = "Minecraft 资源包工具箱";

        /// <summary>pack_format 自这个值起使用 min_format / max_format 新格式（1.21.9+）</summary>
        public const int NewFormatThreshold = 69;

        private static readonly object[,] VersionTable = new object[,]
        {
            { 1, new[] { "1.8.9", "1.8.8", "1.8.7", "1.8.6", "1.8.5", "1.8.4", "1.8.3", "1.8.2", "1.8.2-pre7", "1.8.2-pre6", "1.8.2-pre5", "1.8.2-pre4", "1.8.2-pre3", "1.8.2-pre2", "1.8.2-pre1", "1.8.1", "1.8.1-pre5", "1.8.1-pre4", "1.8.1-pre3", "1.8.1-pre2", "1.8.1-pre1", "1.8", "1.8-pre3", "1.8-pre2", "1.8-pre1", "14w34d", "14w34c", "14w34b", "14w34a", "14w33c", "14w33b", "14w33a", "14w32d", "14w32c", "14w32b", "14w32a", "14w31a", "14w30c", "14w30b", "14w30a", "14w29b", "14w29a", "14w28b", "14w28a", "14w27b", "14w27a", "14w26c", "14w26b", "14w26a", "14w25b", "14w25a", "14w21b", "14w21a", "14w20b", "14w20a", "14w19a", "14w18b", "14w18a", "14w17a", "14w11b", "14w11a", "14w10c", "14w10b", "14w10a", "14w08a", "14w07a", "14w06b", "14w06a", "14w05b", "14w05a", "14w04b", "14w04a", "14w03b", "14w03a", "14w02c", "14w02b", "14w02a", "1.7.10", "1.7.10-pre4", "1.7.10-pre3", "1.7.10-pre2", "1.7.10-pre1", "1.7.9", "1.7.8", "1.7.7", "1.7.6", "1.7.6-pre2", "1.7.6-pre1", "1.7.5", "1.7.4", "1.7.3", "13w49a", "13w48b", "13w48a", "13w47e", "13w47d", "13w47c", "13w47b", "13w47a", "1.7.2", "1.7.1", "1.7", "13w43a", "13w42b", "13w42a", "13w41b", "13w41a" } },
            { 2, new[] { "1.10.2", "1.10.1", "1.10", "1.10-pre2", "1.10-pre1", "16w21b", "16w21a", "16w20a", "1.9.4", "1.9.3", "1.9.3-pre3", "1.9.3-pre2", "1.9.3-pre1", "16w15b", "16w15a", "16w14a", "1.9.2", "1.9.1", "1.9.1-pre3", "1.9.1-pre2", "1.9.1-pre1", "1.9", "1.9-pre4", "1.9-pre3", "1.9-pre2", "1.9-pre1", "16w07b", "16w07a", "16w06a", "16w05b", "16w05a", "16w04a", "16w03a", "16w02a", "15w51b", "15w51a", "15w50a", "15w49b", "15w49a", "15w47c", "15w47b", "15w47a", "15w46a", "15w45a", "15w44b", "15w44a", "15w43c", "15w43b", "15w43a", "15w42a", "15w41b", "15w41a", "15w40b", "15w40a", "15w39c", "15w39b", "15w39a", "15w38b", "15w38a", "15w37a", "15w36d", "15w36c", "15w36b", "15w36a", "15w35e", "15w35d", "15w35c", "15w35b", "15w35a", "15w34d", "15w34c", "15w34b", "15w34a", "15w33c", "15w33b", "15w33a", "15w32c", "15w32b", "15w32a", "15w31c", "15w31b", "15w31a" } },
            { 3, new[] { "17w47b", "17w47a", "17w46a", "17w45b", "17w45a", "17w43b", "17w43a", "1.12.2", "1.12.2-pre2", "1.12.2-pre1", "1.12.1", "1.12.1-pre1", "17w31a", "1.12", "1.12-pre7", "1.12-pre6", "1.12-pre5", "1.12-pre4", "1.12-pre3", "1.12-pre2", "1.12-pre1", "17w18b", "17w18a", "17w17b", "17w17a", "17w16b", "17w16a", "17w15a", "17w14a", "17w13b", "17w13a", "17w06a", "1.11.2", "1.11.1", "16w50a", "1.11", "1.11-pre1", "16w44a", "16w43a", "16w42a", "16w41a", "16w40a", "16w39c", "16w39b", "16w39a", "16w38a", "16w36a", "16w35a", "16w33a", "16w32b", "16w32a" } },
            { 4, new[] { "1.14.4", "1.14.4-pre7", "1.14.4-pre6", "1.14.4-pre5", "1.14.4-pre4", "1.14.4-pre3", "1.14.4-pre2", "1.14.4-pre1", "1.14.3", "1.14.3-pre4", "1.14.3-pre3", "1.14.3-pre2", "1.14.3-pre1", "1.14.2", "1.14.2-pre4", "1.14.2-pre3", "1.14.2-pre2", "1.14.2-pre1", "1.14.1", "1.14.1-pre2", "1.14.1-pre1", "1.14", "1.14-pre5", "1.14-pre4", "1.14-pre3", "1.14-pre2", "1.14-pre1", "18w50a", "18w49a", "18w48b", "18w48a", "18w47b", "18w47a", "18w46a", "18w45a", "18w44a", "18w43c", "18w43b", "18w43a" } },
            { 5, new[] { "1.16.1", "1.16", "1.16-pre8", "1.16-pre7", "1.16-pre6", "1.16-pre5", "1.16-pre4", "1.16-pre3", "1.16-pre2", "1.16-pre1", "20w22a", "20w21a", "20w20b", "20w20a", "20w19a", "20w18a", "20w17a", "20w16a", "20w15a", "20w14a", "20w13b", "20w13a", "20w12a", "20w11a", "20w10a", "20w09a", "20w08a", "20w07a", "20w06a", "1.15.2", "1.15.2-pre2", "1.15.2-pre1", "1.15.1", "1.15.1-pre1", "1.15", "1.15-pre7", "1.15-pre6", "1.15-pre5", "1.15-pre4", "1.15-pre3", "1.15-pre2", "1.15-pre1", "19w46b", "19w46a", "19w45b", "19w45a", "19w44a", "19w42a", "19w41a", "19w40a", "19w39a", "19w38b", "19w38a", "19w37a", "19w36a", "19w35a", "19w34a" } },
            { 6, new[] { "1.16.5", "1.16.5-rc1", "1.16.4", "1.16.4-rc1", "1.16.4-pre2", "1.16.4-pre1", "1.16.3", "1.16.3-rc1", "1.16.2", "1.16.2-rc2", "1.16.2-rc1", "1.16.2-pre3", "1.16.2-pre2", "1.16.2-pre1", "20w30a", "20w29a", "20w28a", "20w27a" } },
            { 7, new[] { "1.17.1", "1.17.1-rc2", "1.17.1-rc1", "1.17.1-pre3", "1.17.1-pre2", "1.17.1-pre1", "1.17", "1.17-rc2", "1.17-rc1", "1.17-pre5", "1.17-pre4", "1.17-pre3", "1.17-pre2", "1.17-pre1", "21w20a", "21w19a", "21w18a", "21w17a", "21w16a", "21w15a", "21w14a", "21w13a", "21w11a", "21w10a", "21w08b", "21w08a", "21w07a", "21w06a", "21w05b", "21w05a", "21w03a", "20w51a", "20w49a", "20w48a", "20w46a", "20w45a" } },
            { 8, new[] { "1.18.2", "1.18.2-rc1", "1.18.2-pre3", "1.18.2-pre2", "1.18.2-pre1", "22w07a", "22w06a", "22w05a", "22w03a", "1.18.1", "1.18.1-rc3", "1.18.1-rc2", "1.18.1-rc1", "1.18.1-pre1", "1.18", "1.18-rc4", "1.18-rc3", "1.18-rc2", "1.18-rc1", "1.18-pre8", "1.18-pre7", "1.18-pre6", "1.18-pre5", "1.18-pre4", "1.18-pre3", "1.18-pre2", "1.18-pre1", "21w44a", "21w43a", "21w42a", "21w41a", "21w40a", "21w39a", "21w38a", "21w37a" } },
            { 9, new[] { "1.19.2", "1.19.2-rc2", "1.19.2-rc1", "1.19.1", "1.19.1-rc3", "1.19.1-rc2", "1.19.1-rc1", "1.19.1-pre6", "1.19.1-pre5", "1.19.1-pre4", "1.19.1-pre3", "1.19.1-pre2", "1.19.1-pre1", "22w24a", "1.19", "1.19-rc3", "1.19-rc2", "1.19-rc1", "1.19-pre5", "1.19-pre4", "1.19-pre3", "1.19-pre2", "1.19-pre1", "22w19a", "22w18a", "22w17a", "22w16b", "22w16a", "22w15a", "22w14a", "22w13oneblockatatime", "22w13a", "22w12a", "22w11a" } },
            { 11, new[] { "22w42a", "22w41a" } },
            { 12, new[] { "1.19.3", "1.19.3-rc3", "1.19.3-rc2", "1.19.3-rc1", "1.19.3-pre3", "1.19.3-pre2", "1.19.3-pre1", "22w46a", "22w45a", "22w44a", "22w43a" } },
            { 13, new[] { "1.19.4", "1.19.4-rc3", "1.19.4-rc2", "1.19.4-rc1", "1.19.4-pre4", "1.19.4-pre3", "1.19.4-pre2", "1.19.4-pre1", "23w07a", "23w06a", "23w05a", "23w04a", "23w03a" } },
            { 15, new[] { "1.20.1", "1.20.1-rc1", "1.20", "1.20-rc1", "1.20-pre7", "1.20-pre6", "1.20-pre5", "1.20-pre4", "1.20-pre3", "1.20-pre2", "1.20-pre1", "23w18a", "23w17a", "23w16a", "23w14a", "23w13a_or_b", "23w13a", "23w12a" } },
            { 18, new[] { "1.20.2", "1.20.2-rc2", "1.20.2-rc1", "1.20.2-pre4", "1.20.2-pre3", "1.20.2-pre2", "1.20.2-pre1", "23w35a", "23w33a", "23w32a", "23w31a" } },
            { 22, new[] { "1.20.4", "1.20.4-rc1", "1.20.3", "1.20.3-rc1", "1.20.3-pre4", "1.20.3-pre3", "1.20.3-pre2", "1.20.3-pre1", "23w51b", "23w51a", "23w46a", "23w45a", "23w44a", "23w43b", "23w43a", "23w42a", "23w41a", "23w40a" } },
            { 32, new[] { "1.20.6", "1.20.6-rc1", "1.20.5", "1.20.5-rc3", "1.20.5-rc2", "1.20.5-rc1", "1.20.5-pre4", "1.20.5-pre3", "1.20.5-pre2", "1.20.5-pre1", "24w14a", "24w13a", "24w12a", "24w11a", "24w10a", "24w09a", "24w07a", "24w06a", "24w05b", "24w05a", "24w04a", "24w03b", "24w03a", "23w51a" } },
            { 34, new[] { "1.21.1", "1.21.1-rc1", "1.21", "1.21-rc1", "1.21-pre4", "1.21-pre3", "1.21-pre2", "1.21-pre1", "24w21b", "24w21a", "24w20a", "24w19b", "24w19a", "24w18a" } },
            { 42, new[] { "1.21.3", "1.21.3-rc1", "1.21.2", "1.21.2-rc2", "1.21.2-rc1", "1.21.2-pre5", "1.21.2-pre4", "1.21.2-pre3", "1.21.2-pre2", "1.21.2-pre1", "24w40a", "24w39a", "24w38a", "24w37a", "24w36a", "24w35a", "24w34a", "24w33a" } },
            { 46, new[] { "1.21.4", "1.21.4-rc3", "1.21.4-rc2", "1.21.4-rc1", "1.21.4-pre3", "1.21.4-pre2", "1.21.4-pre1", "24w46a", "24w45a", "24w44a" } },
            { 55, new[] { "1.21.5", "1.21.5-rc2", "1.21.5-rc1", "1.21.5-pre3", "1.21.5-pre2", "1.21.5-pre1", "25w07a", "25w06a", "25w05a", "25w04a", "25w03a", "25w02a", "25w01a" } },
            { 63, new[] { "1.21.6", "1.21.6-rc1", "1.21.6-pre4", "1.21.6-pre3", "1.21.6-pre2", "1.21.6-pre1", "25w21a", "25w20a", "25w19a", "25w18a", "25w17a", "25w16a", "25w15a" } },
            { 64, new[] { "1.21.8", "1.21.8-rc1", "1.21.7", "1.21.7-rc2", "1.21.7-rc1", "1.21.7-pre2", "1.21.7-pre1", "25w31a", "25w30a" } },
            { 69, new[] { "1.21.10", "1.21.10-rc1", "1.21.9", "1.21.9-rc1", "1.21.9-pre4", "1.21.9-pre3", "1.21.9-pre2", "1.21.9-pre1", "25w36b", "25w36a", "25w35a", "25w34b", "25w34a", "25w33a", "25w32a" } },
            { 75, new[] { "1.21.11", "1.21.11-rc1", "1.21.11-pre5", "1.21.11-pre4", "1.21.11-pre3", "1.21.11-pre2", "1.21.11-pre1", "25w46a", "25w45a", "25w44a", "25w43a", "25w42a", "25w41a" } },
            { 76, new[] { "26.1 Snapshot 1" } },
            { 77, new[] { "26.1 Snapshot 2" } },
            { 78, new[] { "26.1 Snapshot 3" } },
            { 79, new[] { "26.1 Snapshot 5" } },
            { 80, new[] { "26.1 Snapshot 6" } },
            { 81, new[] { "26.1 Snapshot 7", "26.1 Snapshot 8" } },
            { 82, new[] { "26.1 Snapshot 10" } },
            { 83, new[] { "26.1 Snapshot 11" } },
            { 84, new[] { "26.1" } },
            { 85, new[] { "26.1.1" } },
            { 86, new[] { "26.2 Snapshot 1" } },
            { 87, new[] { "26.2 Snapshot 2" } },
            { 88, new[] { "26.2 Snapshot 3" } },
            { 89, new[] { "26.2 Snapshot 4" } },
            { 90, new[] { "26.2" } },
            { 91, new[] { "26.2.1" } },
            { 92, new[] { "26.3 Snapshot 1" } },
            { 93, new[] { "26.3 Snapshot 2" } },
            { 94, new[] { "26.3 Snapshot 3" } },
            { 95, new[] { "26.3" } },
            { 96, new[] { "26.3.1" } },
            { 97, new[] { "26.4 Snapshot 1" } },
            { 98, new[] { "26.4 Snapshot 2" } },
            { 99, new[] { "26.4 Snapshot 3" } },
        };

        private static List<VersionEntry> _cache;

        /// <summary>正式版判断：纯数字加点，且不含 pre / rc / snapshot 之类后缀</summary>
        public static bool IsRelease(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            foreach (char c in version)
            {
                if (!char.IsDigit(c) && c != '.') return false;
            }
            string[] parts = version.Split('.');
            foreach (string p in parts)
            {
                if (p.Length == 0) return false;
            }
            return true;
        }

        /// <summary>全部版本条目，按 pack_format 升序</summary>
        public static List<VersionEntry> AllVersions()
        {
            if (_cache != null) return _cache;

            var list = new List<VersionEntry>();
            int rows = VersionTable.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                int format = (int)VersionTable[i, 0];
                string[] versions = (string[])VersionTable[i, 1];
                list.Add(new VersionEntry(format, versions));
            }
            list.Sort((a, b) => a.Format.CompareTo(b.Format));
            _cache = list;
            return list;
        }

        /// <summary>
        /// 面向界面的版本条目：只保留正式版做显示，
        /// 没有正式版时（快照专用格式）才回落到全部版本。
        /// </summary>
        public static List<VersionEntry> DisplayVersions()
        {
            var result = new List<VersionEntry>();
            foreach (var e in AllVersions())
            {
                string[] releases = e.Versions.Where(IsRelease).ToArray();
                string[] use = releases.Length > 0 ? releases : e.Versions;
                result.Add(new VersionEntry(e.Format, use) { AllVersions = e.Versions });
            }
            return result;
        }

        /// <summary>把版本条目格式化成显示文本，如 "1.20 – 1.20.1"</summary>
        public static string Describe(VersionEntry e)
        {
            if (e.Versions.Length == 0) return "未知";
            string newest = e.Versions[0];
            string oldest = e.Versions[e.Versions.Length - 1];
            return newest == oldest ? newest : oldest + " – " + newest;
        }

        /// <summary>根据 pack_format 找条目</summary>
        public static VersionEntry FindByFormat(int format)
        {
            foreach (var e in DisplayVersions())
            {
                if (e.Format == format) return e;
            }
            return null;
        }

        /// <summary>根据版本号精确定位 pack_format（支持下落到同一格式内的其它版本）</summary>
        public static int? FindFormatByVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            string v = version.Trim();

            foreach (var e in AllVersions())
            {
                foreach (string s in e.Versions)
                {
                    if (string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) return e.Format;
                }
            }
            return null;
        }

        /// <summary>该格式属于哪个大版本段（用于决定要不要做资源迁移）</summary>
        public static string DescribeStage(int format)
        {
            if (format <= 3) return "Legacy（1.6 – 1.12.2）";
            if (format <= 12) return "Flattening（1.13 – 1.19.3）";
            if (format <= 15) return "Components（1.19.4 – 1.20.4）";
            if (format <= 34) return "New Model（1.20.5 – 1.21.3）";
            if (format <= 68) return "Items Folder（1.21.4 – 1.21.8）";
            return "New Format（1.21.9+，min/max_format）";
        }
    }

    internal sealed class VersionEntry
    {
        public int Format;
        public string[] Versions;
        /// <summary>未经正式版过滤的完整版本列表</summary>
        public string[] AllVersions;

        public VersionEntry(int format, string[] versions)
        {
            Format = format;
            Versions = versions;
            AllVersions = versions;
        }
    }
}
