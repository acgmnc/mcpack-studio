using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* 数据模型                                                            */
    /* ================================================================== */

    internal sealed class PackFormatInfo
    {
        public int? PackFormat;          // 单一格式数字
        public int? MinFormat;           // 1.21.9+ 新格式
        public int? MaxFormat;
        public bool UsesNewFormat;       // 原始文件里是否出现了 min_format / max_format
        public bool HasPackFormat;
        public bool HasSupportedFormats;
        public string RawPackFormat;     // 原始写法（可能是对象或数组）
        public string Description;
        public string ParseError;        // pack.mcmeta 解析失败时的原因

        /// <summary>用于比较"当前版本"的有效格式号</summary>
        public int? EffectiveFormat
        {
            get
            {
                if (PackFormat.HasValue) return PackFormat;
                if (MinFormat.HasValue) return MinFormat;
                return null;
            }
        }

        public string DescribeCurrent()
        {
            if (!string.IsNullOrEmpty(ParseError)) return "pack.mcmeta 解析失败";
            var e = EffectiveFormat;
            if (!e.HasValue) return "未声明 pack_format";
            VersionEntry entry = Settings.FindByFormat(e.Value);
            string range = entry != null ? Settings.Describe(entry) : "未知版本";
            string extra = UsesNewFormat ? "（min/max_format 新格式）" : "";
            return "pack_format " + e.Value + "  ·  " + range + extra;
        }
    }

    internal sealed class PackCounts
    {
        public int Total;
        public int Textures;
        public int Models;
        public int BlockStates;
        public int ItemModels;
        public int Lang;
        public int Sounds;
        public int Json;
        public int Other;
        public long TotalBytes;
    }

    internal sealed class PackAnalysis
    {
        public string SourcePath;
        public bool IsZip;
        public string RootDir;               // 资源包根目录（目录模式为原目录，ZIP 模式为解压出的临时目录）
        public string TempDir;               // 需要清理的临时目录
        public string ZipInnerPrefix;        // ZIP 内侧的多余前缀
        public bool PackMcmetaAtRoot;
        public PackFormatInfo Format;
        public PackCounts Counts;
        public List<string> Namespaces = new List<string>();
        public List<string> Warnings = new List<string>();
        public bool Ok;
        public string Error;

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(SourcePath)) return "";
                try
                {
                    return IsZip
                        ? Path.GetFileNameWithoutExtension(SourcePath)
                        : new DirectoryInfo(SourcePath.TrimEnd('\\', '/')).Name;
                }
                catch { return SourcePath; }
            }
        }

        public void Cleanup()
        {
            if (!string.IsNullOrEmpty(TempDir)) ZipIo.SafeDelete(TempDir);
        }
    }

    /* ================================================================== */
    /* 分析器                                                              */
    /* ================================================================== */

    internal static class PackAnalyzer
    {
        /// <summary>分析一个 ZIP 或文件夹，返回包信息；失败时 Ok=false 且 Error 有值</summary>
        public static PackAnalysis Analyze(string path)
        {
            var result = new PackAnalysis { SourcePath = path, Counts = new PackCounts() };

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Error = "没有选择输入路径";
                return result;
            }

            if (Directory.Exists(path))
            {
                result.IsZip = false;
                result.RootDir = Path.GetFullPath(path);
            }
            else if (File.Exists(path))
            {
                result.IsZip = true;
                string prefix, err;
                if (!ZipIo.TryFindPackRoot(path, out prefix, out err))
                {
                    result.Error = err;
                    return result;
                }
                result.ZipInnerPrefix = prefix;

                string temp = Path.Combine(Path.GetTempPath(), "mcpack_conv_" + Guid.NewGuid().ToString("N"));
                try
                {
                    ZipIo.ExtractTo(path, temp, prefix);
                }
                catch (Exception e)
                {
                    ZipIo.SafeDelete(temp);
                    result.Error = "解压失败：" + e.Message;
                    return result;
                }
                result.TempDir = temp;
                result.RootDir = temp;
            }
            else
            {
                result.Error = "路径不存在：" + path;
                return result;
            }

            ScanContents(result);
            return result;
        }

        private static void ScanContents(PackAnalysis a)
        {
            string mcmeta = Path.Combine(a.RootDir, "pack.mcmeta");
            a.PackMcmetaAtRoot = File.Exists(mcmeta);

            if (a.PackMcmetaAtRoot)
            {
                a.Format = ReadFormat(mcmeta, a.Warnings);
            }
            else
            {
                a.Format = new PackFormatInfo();
                a.Warnings.Add("资源包根目录没有 pack.mcmeta —— 无法确定它的目标版本，也无法写入新的 pack_format");
            }

            var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(a.RootDir, "*", SearchOption.AllDirectories))
            {
                string rel = ZipIo.RelPath(a.RootDir, file);
                a.Counts.Total++;
                try { a.Counts.TotalBytes += new FileInfo(file).Length; }
                catch { }

                string lower = rel.ToLowerInvariant();
                string ext = Path.GetExtension(lower);

                // 命名空间：assets/<ns>/...
                if (lower.StartsWith("assets/"))
                {
                    string rest = rel.Substring("assets/".Length);
                    int slash = rest.IndexOf('/');
                    if (slash > 0) namespaces.Add(rest.Substring(0, slash));
                }

                if (lower.Contains("/textures/") || lower.StartsWith("textures/")) a.Counts.Textures++;
                else if (lower.Contains("/models/") || lower.StartsWith("models/")) a.Counts.Models++;
                else if (lower.Contains("/blockstates/") || lower.StartsWith("blockstates/")) a.Counts.BlockStates++;
                else if (lower.Contains("/items/") || lower.StartsWith("items/")) a.Counts.ItemModels++;
                else if (lower.Contains("/lang/") || lower.StartsWith("lang/")) a.Counts.Lang++;
                else if (lower.Contains("/sounds/") || lower.StartsWith("sounds/")) a.Counts.Sounds++;
                else if (ext == ".json" || ext == ".mcmeta") a.Counts.Json++;
                else a.Counts.Other++;
            }

            a.Namespaces = namespaces.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            a.Ok = true;
        }

        /// <summary>读取 pack.mcmeta 里的版本声明（兼容单数字、对象、数组三种写法）</summary>
        public static PackFormatInfo ReadFormat(string mcmetaPath, List<string> warnings)
        {
            var info = new PackFormatInfo();
            string text;
            try
            {
                text = File.ReadAllText(mcmetaPath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                info.ParseError = "读取失败：" + e.Message;
                return info;
            }

            JsonValue root;
            string err;
            if (!Json.TryParse(text, out root, out err))
            {
                info.ParseError = err;
                return info;
            }

            JsonValue pack = root.Get("pack");
            if (pack == null || !pack.IsObject)
            {
                info.ParseError = "缺少 pack 对象";
                return info;
            }

            info.UsesNewFormat = pack.Has("min_format") || pack.Has("max_format") || pack.Has("supported_formats");
            info.HasSupportedFormats = pack.Has("supported_formats") || pack.Has("supported_format");

            JsonValue desc = pack.Get("description");
            if (desc != null) info.Description = desc.AsText();

            JsonValue pf = pack.Get("pack_format");
            if (pf != null)
            {
                info.HasPackFormat = true;
                info.RawPackFormat = pf.WriteCompat();

                // 范围写法本身就说明这个包在用「多版本兼容」的新式声明
                if (pf.IsObject || pf.IsArray) info.UsesNewFormat = true;

                int? n = ExtractFormatNumber(pf);
                if (n.HasValue)
                {
                    info.PackFormat = n;
                }
                else if (pf.IsObject || pf.IsArray)
                {
                    // 能识别类型但取不到数字，提示一下
                    if (warnings != null)
                        warnings.Add("pack_format 是范围写法（" + info.RawPackFormat + "），但无法取出有效的格式号");
                }
            }

            JsonValue minF = pack.Get("min_format");
            if (minF != null) info.MinFormat = ExtractFormatNumber(minF);
            JsonValue maxF = pack.Get("max_format");
            if (maxF != null) info.MaxFormat = ExtractFormatNumber(maxF);

            return info;
        }

        /// <summary>从各种写法里抽取格式数字；对象取 min_inclusive，数组取首元素</summary>
        private static int? ExtractFormatNumber(JsonValue v)
        {
            if (v == null) return null;
            if (v.IsNumber || v.IsString) return v.AsInt();

            if (v.IsObject)
            {
                JsonValue min = v.Get("min_inclusive");
                if (min != null) return ExtractFormatNumber(min);
                JsonValue any = v.Get("min");
                if (any != null) return ExtractFormatNumber(any);
                JsonValue max = v.Get("max_inclusive");
                if (max != null) return ExtractFormatNumber(max);
                return null;
            }

            if (v.IsArray && v.Items.Count > 0) return ExtractFormatNumber(v.Items[0]);
            return null;
        }
    }

    internal static class JsonValueExt
    {
        /// <summary>紧凑输出，用于把原始写法显示给用户</summary>
        public static string WriteCompat(this JsonValue v)
        {
            if (v == null) return "";
            return Json.Write(v, false);
        }
    }
}
