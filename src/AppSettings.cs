using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// 极简的本地设置存取（key=value 文本文件）。
    /// 目前只用来记住上次停留的子模块，以后要加开关也放这里。
    /// </summary>
    internal static class AppSettings
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCPackStudio");
        private static readonly string File_ = Path.Combine(Dir, "settings.ini");

        private static Dictionary<string, string> _cache;

        private static Dictionary<string, string> All()
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(File_))
                {
                    foreach (string line in File.ReadAllLines(File_, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        _cache[t.Substring(0, eq).Trim()] = t.Substring(eq + 1);
                    }
                }
            }
            catch
            {
                // 读不到就用默认值
            }
            return _cache;
        }

        public static string Get(string key, string fallback)
        {
            string v;
            return All().TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            All()[key] = value ?? "";
            Save();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# MCPack Studio 本地设置（可安全删除）");
                foreach (var kv in _cache)
                {
                    sb.AppendLine(kv.Key + "=" + kv.Value);
                }
                File.WriteAllText(File_, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 存不下来不影响使用
            }
        }

        /* ---------------- 上次停留的位置 ---------------- */

        public static void LoadSelection(out string moduleId, out string subId)
        {
            moduleId = Get("last.module", null);
            subId = Get("last.sub", null);
        }

        public static void SaveSelection(string moduleId, string subId)
        {
            if (string.IsNullOrEmpty(moduleId) || string.IsNullOrEmpty(subId)) return;
            All()["last.module"] = moduleId;
            All()["last.sub"] = subId;
            Save();
        }
    }
}
