using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// ZIP 读写。用 .NET 内置的 ZipArchive，统一正斜杠路径并做路径穿越防护。
    /// </summary>
    internal static class ZipIo
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>统一条目名：反斜杠转正斜杠、去掉前导 ./ 与 /</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.Replace('\\', '/');
            while (n.StartsWith("./", StringComparison.Ordinal)) n = n.Substring(2);
            while (n.StartsWith("/", StringComparison.Ordinal)) n = n.Substring(1);
            while (n.Contains("//")) n = n.Replace("//", "/");
            return n;
        }

        /// <summary>判断 ZIP 是否包含 pack.mcmeta，并返回它所在的目录前缀（"" 或 "xxx/"）</summary>
        public static bool TryFindPackRoot(string zipPath, out string prefix, out string error)
        {
            prefix = null;
            error = null;
            try
            {
                using (var fs = File.OpenRead(zipPath))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        string name = Normalize(entry.FullName);
                        if (name.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = "";
                            return true;
                        }
                        if (name.EndsWith("/pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = name.Substring(0, name.Length - "pack.mcmeta".Length);
                            return true;
                        }
                    }
                }
                error = "这个 ZIP 里找不到 pack.mcmeta，不是有效的资源包";
                return false;
            }
            catch (InvalidDataException)
            {
                error = "这个文件不是有效的 ZIP（扩展名是 .zip 但内容损坏）";
                return false;
            }
            catch (Exception e)
            {
                error = "无法读取 ZIP：" + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 解压到目录。stripPrefix 非空时会剥离该前缀（用于处理多套一层目录的资源包）。
        /// 会拒绝路径穿越条目。
        /// </summary>
        public static void ExtractTo(string zipPath, string destDir, string stripPrefix)
        {
            string destFull = Path.GetFullPath(destDir);
            Directory.CreateDirectory(destFull);

            using (var fs = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string rel = Normalize(entry.FullName);
                    if (rel.Length == 0) continue;
                    if (!string.IsNullOrEmpty(stripPrefix))
                    {
                        if (!rel.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        rel = rel.Substring(stripPrefix.Length);
                    }
                    if (rel.Length == 0) continue;

                    string target = Path.GetFullPath(Path.Combine(destFull, rel));
                    if (!IsInside(destFull, target)) continue;  // 拒绝 ../ 穿越

                    bool isDir = entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\");
                    if (isDir)
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = File.Create(target))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        /// <summary>把目录打包成 ZIP（Deflated）。skip 返回 true 的相对路径会被跳过。</summary>
        public static int CreateFromDirectory(string sourceDir, string outputZip, Func<string, bool> skip)
        {
            string srcFull = Path.GetFullPath(sourceDir);
            string outFull = Path.GetFullPath(outputZip);

            Directory.CreateDirectory(Path.GetDirectoryName(outFull));

            var files = new List<string>();
            CollectFiles(srcFull, srcFull, files, skip);
            files.Sort(StringComparer.OrdinalIgnoreCase);

            // 输出文件若在源目录内，需要排除，否则会把自己打进去
            var final = new List<string>();
            foreach (string f in files)
            {
                if (string.Equals(Path.GetFullPath(f), outFull, StringComparison.OrdinalIgnoreCase)) continue;
                final.Add(f);
            }

            using (var fs = File.Create(outFull))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string file in final)
                {
                    string rel = RelPath(srcFull, file);
                    var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using (var input = File.OpenRead(file))
                    using (var output = entry.Open())
                    {
                        input.CopyTo(output);
                    }
                }
            }
            return final.Count;
        }

        /// <summary>按给定的 相对路径 → 内容 打包成 ZIP 字节</summary>
        public static byte[] CreateFromEntries(IDictionary<string, byte[]> entries)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var kv in entries.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var entry = zip.CreateEntry(Normalize(kv.Key), CompressionLevel.Optimal);
                        using (var s = entry.Open())
                        {
                            s.Write(kv.Value, 0, kv.Value.Length);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        private static void CollectFiles(string root, string dir, List<string> acc, Func<string, bool> skip)
        {
            foreach (string f in Directory.GetFiles(dir))
            {
                string rel = RelPath(root, f);
                if (skip != null && skip(rel)) continue;
                acc.Add(f);
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                CollectFiles(root, d, acc, skip);
            }
        }

        public static bool IsInside(string parentFull, string childFull)
        {
            if (string.Equals(parentFull, childFull, StringComparison.OrdinalIgnoreCase)) return true;
            string p = parentFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return childFull.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>相对路径（.NET Framework 没有 Path.GetRelativePath）</summary>
        public static string RelPath(string rootFull, string childFull)
        {
            string root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string child = Path.GetFullPath(childFull);
            if (child.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                child = child.Substring(root.Length);
            }
            return child.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
        }

        /// <summary>安全删除目录（只允许删临时目录下的东西）</summary>
        public static void SafeDelete(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                if (!Directory.Exists(dir)) return;
                string full = Path.GetFullPath(dir);
                string temp = Path.GetFullPath(Path.GetTempPath());
                if (!IsInside(temp, full)) return;
                Directory.Delete(full, true);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        /// <summary>删除我们自己创建的目录（不做临时目录限制，但拒绝盘符根与过浅路径）</summary>
        public static void DeleteDirectory(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                if (!Directory.Exists(dir)) return;
                string full = Path.GetFullPath(dir);

                string root = Path.GetPathRoot(full);
                if (string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                if (full.Split(Path.DirectorySeparatorChar).Length < 3) return;

                Directory.Delete(full, true);
            }
            catch
            {
                // 忽略
            }
        }
    }
}
