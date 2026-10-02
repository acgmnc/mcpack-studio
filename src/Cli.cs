using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// 命令行模式：不开界面直接转换，便于批量处理与脚本化验证。
    ///
    ///   MCPackStudio.exe --convert &lt;源&gt; --to &lt;版本或pack_format&gt; --out &lt;输出&gt;
    ///                     [--zip] [--meta-only] [--no-lang] [--exclude &lt;相对路径&gt;]
    ///                     [--analyze] [--report &lt;日志文件&gt;] [--quiet]
    /// </summary>
    internal static class Cli
    {
        public static int Convert(string[] args)
        {
            var sb = new StringBuilder();
            Action<string> log = s => { sb.AppendLine(s); Console.WriteLine(s); };

            /* ---------------- 解析参数 ---------------- */
            string src = null, to = null, outPath = null, reportFile = null;
            bool zip = false, metaOnly = false, noLang = false, analyzeOnly = false, quiet = false;
            var excludes = new List<string>();

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--convert":
                        // 兼容两种写法：--convert <源> 与 --convert --to X <源>
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            src = args[++i];
                        }
                        break;
                    case "--to": to = Next(args, ref i); break;
                    case "--out": outPath = Next(args, ref i); break;
                    case "--report": reportFile = Next(args, ref i); break;
                    case "--exclude": excludes.Add(Next(args, ref i)); break;
                    case "--zip": zip = true; break;
                    case "--meta-only": metaOnly = true; break;
                    case "--no-lang": noLang = true; break;
                    case "--analyze": analyzeOnly = true; break;
                    case "--quiet": quiet = true; break;
                    default:
                        // 未被选项吃掉的裸参数当作源资源包路径
                        if (!a.StartsWith("--", StringComparison.Ordinal) && src == null)
                        {
                            src = a;
                        }
                        else
                        {
                            log("未知参数：" + a);
                            return 2;
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(src))
            {
                log("缺少 --convert <源资源包>");
                return 2;
            }

            /* ---------------- 分析 ---------------- */
            var analysis = PackAnalyzer.Analyze(src);
            if (!analysis.Ok)
            {
                log("分析失败：" + analysis.Error);
                return 1;
            }

            log("源资源包    : " + analysis.SourcePath);
            log("类型        : " + (analysis.IsZip ? "ZIP" : "文件夹"));
            if (analysis.IsZip && !string.IsNullOrEmpty(analysis.ZipInnerPrefix))
                log("内侧前缀    : " + analysis.ZipInnerPrefix + "（已剥离）");
            log("当前版本    : " + analysis.Format.DescribeCurrent());
            log("文件统计    : " + analysis.Counts.Total + " 个文件, " + FormatBytes(analysis.Counts.TotalBytes));
            log("  textures=" + analysis.Counts.Textures
                + " models=" + analysis.Counts.Models
                + " blockstates=" + analysis.Counts.BlockStates
                + " lang=" + analysis.Counts.Lang
                + " sounds=" + analysis.Counts.Sounds);
            log("命名空间    : " + (analysis.Namespaces.Count > 0 ? string.Join(", ", analysis.Namespaces.ToArray()) : "(无)"));
            foreach (string w in analysis.Warnings) log("提示        : " + w);

            if (analyzeOnly)
            {
                Finish(sb, reportFile, quiet, null, analysis);
                return 0;
            }

            if (string.IsNullOrEmpty(to))
            {
                log("缺少 --to <版本号或 pack_format>，例如 --to 1.20.1 或 --to 15");
                return 2;
            }
            if (string.IsNullOrEmpty(outPath))
            {
                log("缺少 --out <输出路径>");
                return 2;
            }

            /* ---------------- 解析目标版本 ---------------- */
            int targetFormat;
            string targetLabel;
            if (!ResolveTarget(to, out targetFormat, out targetLabel))
            {
                log("无法识别的目标版本：" + to);
                log("可以填 Minecraft 版本号（如 1.20.1）或 pack_format（如 15）");
                return 2;
            }
            log("目标版本    : pack_format " + targetFormat + "  ·  " + targetLabel);

            /* ---------------- 规划 ---------------- */
            var opt = new ConvertOptions
            {
                Mode = zip ? OutputMode.Zip : OutputMode.Folder,
                OutputPath = outPath,
                TargetFormat = targetFormat,
                TargetVersionLabel = targetLabel,
                MigrateAssets = !metaOnly,
                MigrateLangContent = !noLang,
                Excluded = new HashSet<string>(excludes, StringComparer.OrdinalIgnoreCase),
            };

            var plan = PlanBuilder.Build(analysis, opt);
            log("");
            log("转换计划    : " + plan.Describe());
            log("触发规则    : " + (plan.RuleIds.Count > 0 ? string.Join(", ", plan.RuleIds.ToArray()) : "(无，只更新版本号)"));
            foreach (string n in plan.Notes) log("  · " + n);

            if (plan.Actions.Count(x => x.Kind == PlanActionKind.Rename || x.Kind == PlanActionKind.Rewrite) > 0)
            {
                log("");
                log("将要变动的内容：");
                foreach (var a in plan.Actions.Where(x => x.Kind == PlanActionKind.Rename || x.Kind == PlanActionKind.Rewrite))
                {
                    log("  [" + (a.Kind == PlanActionKind.Rename ? "重命名" : "改写") + "] " + a.From);
                    if (a.Kind == PlanActionKind.Rename) log("          → " + a.To);
                    foreach (string n in a.Notes) log("          · " + n);
                }
            }

            /* ---------------- 执行 ---------------- */
            log("");
            var report = ConvertExecutor.Execute(plan, null);

            if (!report.Success)
            {
                log("转换失败    : " + report.Error);
                Finish(sb, reportFile, quiet, report, analysis);
                return 1;
            }

            log("转换成功");
            log("输出        : " + report.OutputPath);
            log("输出大小    : " + FormatBytes(report.OutputBytes));
            log("写出文件    : " + report.Written + "（重命名 " + report.Renamed + "，内容改写 " + report.Rewritten
                + "，排除 " + report.Excluded + "）");
            log("耗时        : " + report.ElapsedMs + " 毫秒");
            foreach (string p in report.Problems) log("注意        : " + p);

            /* ---------------- 输出后校验 ---------------- */
            log("");
            log("=== 输出校验 ===");
            var recheck = PackAnalyzer.Analyze(report.OutputPath);
            if (recheck.Ok)
            {
                log("重新分析    : " + recheck.Format.DescribeCurrent());
                log("文件数      : " + recheck.Counts.Total + "（源 " + analysis.Counts.Total + "）");
                if (recheck.Format.EffectiveFormat != targetFormat)
                {
                    log("!! 警告：输出里的 pack_format 是 " + recheck.Format.EffectiveFormat + "，与目标 " + targetFormat + " 不一致");
                }
                else
                {
                    log("版本校验    : 通过，输出 pack_format = " + targetFormat);
                }
            }
            else
            {
                log("!! 输出无法被重新分析：" + recheck.Error);
            }

            // 字节完整性：随机抽查几个文件
            int byteChecked = 0, byteMismatch = 0;
            foreach (var a in plan.Actions.Where(x => x.Kind == PlanActionKind.Keep).Take(50))
            {
                string srcFile = Path.Combine(analysis.RootDir, a.From.Replace('/', Path.DirectorySeparatorChar));
                string dstFile = Path.Combine(report.OutputPath, a.To.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(srcFile) || !File.Exists(dstFile)) continue;
                byteChecked++;
                byte[] s1 = File.ReadAllBytes(srcFile);
                byte[] s2 = File.ReadAllBytes(dstFile);
                if (s1.Length != s2.Length || !s1.SequenceEqual(s2)) byteMismatch++;
            }
            log("字节完整性  : 抽查 " + byteChecked + " 个未改动文件，不一致 " + byteMismatch + " 个");

            Finish(sb, reportFile, quiet, report, analysis);
            return 0;
        }

        private static void Finish(StringBuilder sb, string reportFile, bool quiet, ConvertReport report, PackAnalysis analysis)
        {
            if (!string.IsNullOrEmpty(reportFile))
            {
                try
                {
                    File.WriteAllText(reportFile, sb.ToString(), new UTF8Encoding(false));
                    if (!quiet) Console.WriteLine("");
                    if (!quiet) Console.WriteLine("报告已写入：" + reportFile);
                }
                catch (Exception e)
                {
                    Console.WriteLine("报告写入失败：" + e.Message);
                }
            }
            if (analysis != null) analysis.Cleanup();
        }

        /// <summary>把 --to 的值解析成 pack_format</summary>
        private static bool ResolveTarget(string raw, out int format, out string label)
        {
            format = 0;
            label = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string v = raw.Trim();

            // 纯数字 → 直接当 pack_format
            int direct;
            if (int.TryParse(v, out direct))
            {
                var entry = Settings.FindByFormat(direct);
                if (entry == null) return false;
                format = direct;
                label = Settings.Describe(entry);
                return true;
            }

            // 版本号 → 查表
            int? found = Settings.FindFormatByVersion(v);
            if (!found.HasValue) return false;
            format = found.Value;
            var e2 = Settings.FindByFormat(format);
            label = e2 != null ? Settings.Describe(e2) : v;
            return true;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) return null;
            i++;
            return args[i];
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "1") + " " + units[i];
        }
    }
}
