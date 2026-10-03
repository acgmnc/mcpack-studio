using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace McPackStudio
{
    /* ================================================================== */
    /* 批量重命名                                                          */
    /* ================================================================== */

    internal enum RenameMode
    {
        FindReplace,    // 查找替换（可选正则）
        Prefix,         // 加前缀
        Suffix,         // 加后缀
        Number,         // 序号编号
    }

    internal enum RenameScope
    {
        FileNameOnly,   // 只改文件名，保留扩展名
        WithExtension,  // 文件名和扩展名一起改
        FullPath,       // 对完整相对路径做替换（可以改目录名）
    }

    internal sealed class RenameRule
    {
        public RenameMode Mode = RenameMode.FindReplace;
        public RenameScope Scope = RenameScope.FileNameOnly;

        public string Find = "";
        public string Replace = "";
        public bool UseRegex;
        public bool CaseSensitive = true;

        public string Prefix = "";
        public string Suffix = "";

        public string NumberFormat = "{0:000}";
        public int NumberStart = 1;
        public int NumberStep = 1;

        /// <summary>筛选：只处理这些扩展名（逗号分隔，空 = 全部）</summary>
        public string ExtFilter = "";
        /// <summary>筛选：只处理这个相对目录下的文件（空 = 全部）</summary>
        public string DirFilter = "";
        /// <summary>筛选：文件名必须包含这个子串（空 = 不限）</summary>
        public string ContainsFilter = "";
    }

    internal sealed class RenameItem
    {
        public string From;
        public string To;
        public string Reason;      // 没变或出错时说明原因
        public bool Changed;
        public bool Conflict;      // 目标路径与另一个文件撞车
        public bool Invalid;       // 新名字不合法
    }

    internal sealed class RenamePlan
    {
        public string RootDir;                 // 源资源包根目录
        public List<RenameItem> Items = new List<RenameItem>();
        public List<string> Notes = new List<string>();

        public int ChangedCount { get { return Items.Count(i => i.Changed && !i.Conflict && !i.Invalid); } }
        public int ConflictCount { get { return Items.Count(i => i.Conflict); } }
        public int InvalidCount { get { return Items.Count(i => i.Invalid); } }

        public bool CanApply { get { return ChangedCount > 0 && ConflictCount == 0 && InvalidCount == 0; } }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("共 ").Append(Items.Count).Append(" 个文件，将要改名 ").Append(ChangedCount);
            if (ConflictCount > 0) sb.Append("，冲突 ").Append(ConflictCount);
            if (InvalidCount > 0) sb.Append("，非法 ").Append(InvalidCount);
            return sb.ToString();
        }
    }

    internal static class PackRenamer
    {
        private static readonly char[] BadChars = Path.GetInvalidFileNameChars();

        public static RenamePlan Plan(string rootDir, RenameRule rule, Action<string> note)
        {
            var plan = new RenamePlan { RootDir = Path.GetFullPath(rootDir) };

            if (!Directory.Exists(plan.RootDir))
            {
                plan.Notes.Add("目录不存在：" + plan.RootDir);
                return plan;
            }

            /* ---- 正则先单独校验一次：不合法就直接整体作废，避免逐文件重复报错 ---- */
            bool regexBroken = false;
            if (rule.Mode == RenameMode.FindReplace && rule.UseRegex && !string.IsNullOrEmpty(rule.Find))
            {
                try
                {
                    var opts = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    new Regex(rule.Find, opts);
                }
                catch (Exception e)
                {
                    regexBroken = true;
                    plan.Notes.Add("正则表达式不合法：" + e.Message);
                }
            }

            /* ---- 收集候选文件 ---- */
            var files = Directory.GetFiles(plan.RootDir, "*", SearchOption.AllDirectories)
                                 .Select(f => ZipIo.RelPath(plan.RootDir, f))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            if (note != null) note("扫描到 " + files.Count + " 个文件");

            if (regexBroken)
            {
                /* 正则坏了就全部标成非法，让界面直接拦住执行 */
                foreach (string rel in files)
                {
                    plan.Items.Add(new RenameItem
                    {
                        From = rel,
                        To = rel,
                        Changed = true,
                        Invalid = true,
                        Reason = "正则表达式不合法",
                    });
                }
                return plan;
            }

            var extSet = new HashSet<string>(
                (rule.ExtFilter ?? "")
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.TrimStart('.').ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            string dirFilter = (rule.DirFilter ?? "").Trim().Replace('\\', '/').Trim('/');

            /* ---- 逐个计算新名字 ---- */
            int seq = rule.NumberStart;
            var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rel in files)
            {
                string ext = Path.GetExtension(rel);
                string nameNoExt = rel.Substring(0, rel.Length - ext.Length);

                /* 筛选 */
                if (extSet.Count > 0)
                {
                    string e = ext.TrimStart('.').ToLowerInvariant();
                    if (!extSet.Contains(e)) continue;
                }
                if (dirFilter.Length > 0)
                {
                    string dir = rel.Contains('/') ? rel.Substring(0, rel.LastIndexOf('/')) : "";
                    if (!dir.Equals(dirFilter, StringComparison.OrdinalIgnoreCase) &&
                        !dir.StartsWith(dirFilter + "/", StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!string.IsNullOrEmpty(rule.ContainsFilter) &&
                    rel.IndexOf(rule.ContainsFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string newRel = rel;
                string reason = null;

                switch (rule.Mode)
                {
                    case RenameMode.FindReplace:
                        {
                            if (string.IsNullOrEmpty(rule.Find)) { reason = "查找内容为空"; break; }

                            string subject, tail;
                            if (rule.Scope == RenameScope.FileNameOnly)
                            {
                                /* 只替文件名，扩展名不动 */
                                subject = Path.GetFileName(nameNoExt);
                                tail = ext;
                            }
                            else if (rule.Scope == RenameScope.WithExtension)
                            {
                                subject = Path.GetFileName(rel);
                                tail = "";
                            }
                            else
                            {
                                subject = rel;
                                tail = "";
                            }

                            string replaced;
                            if (rule.UseRegex)
                            {
                                try
                                {
                                    var opts = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                                    replaced = Regex.Replace(subject, rule.Find, rule.Replace ?? "", opts);
                                }
                                catch (Exception e)
                                {
                                    reason = "正则不合法：" + e.Message;
                                    replaced = subject;
                                }
                            }
                            else
                            {
                                var cmp = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                                replaced = ReplaceAll(subject, rule.Find, rule.Replace ?? "", cmp);
                            }

                            if (reason == null)
                            {
                                if (rule.Scope == RenameScope.FullPath)
                                    newRel = replaced;
                                else
                                {
                                    string dir = rel.Contains('/') ? rel.Substring(0, rel.LastIndexOf('/') + 1) : "";
                                    newRel = dir + replaced + tail;
                                }
                            }
                            break;
                        }

                    case RenameMode.Prefix:
                        {
                            string dir = rel.Contains('/') ? rel.Substring(0, rel.LastIndexOf('/') + 1) : "";
                            string fn = Path.GetFileName(rel);
                            newRel = dir + (rule.Prefix ?? "") + fn;
                            break;
                        }

                    case RenameMode.Suffix:
                        {
                            string dir = rel.Contains('/') ? rel.Substring(0, rel.LastIndexOf('/') + 1) : "";
                            newRel = dir + Path.GetFileName(nameNoExt) + (rule.Suffix ?? "") + ext;
                            break;
                        }

                    case RenameMode.Number:
                        {
                            string dir = rel.Contains('/') ? rel.Substring(0, rel.LastIndexOf('/') + 1) : "";
                            string numText;
                            try { numText = string.Format(rule.NumberFormat, seq); }
                            catch { numText = seq.ToString(); }
                            newRel = dir + numText + ext;
                            seq += rule.NumberStep;
                            break;
                        }
                }

                var item = new RenameItem { From = rel, To = ZipIo.Normalize(newRel), Reason = reason };

                if (reason == null)
                {
                    item.Changed = !string.Equals(item.From, item.To, StringComparison.Ordinal);

                    /* 合法性 */
                    if (item.Changed)
                    {
                        if (string.IsNullOrWhiteSpace(item.To) || item.To.EndsWith("/"))
                        {
                            item.Invalid = true;
                            item.Reason = "新名字为空";
                        }
                        else
                        {
                            string leaf = item.To.Contains('/')
                                ? item.To.Substring(item.To.LastIndexOf('/') + 1) : item.To;
                            if (leaf.IndexOfAny(BadChars) >= 0)
                            {
                                item.Invalid = true;
                                item.Reason = "新名字含有非法字符";
                            }
                            if (!ZipIo.IsInside(Path.GetFullPath(plan.RootDir), Path.GetFullPath(
                                    Path.Combine(plan.RootDir, item.To.Replace('/', Path.DirectorySeparatorChar)))))
                            {
                                item.Invalid = true;
                                item.Reason = "新路径逃出了资源包目录";
                            }
                        }
                    }
                }

                plan.Items.Add(item);

                if (item.Changed && !item.Invalid)
                {
                    if (targets.ContainsKey(item.To))
                    {
                        item.Conflict = true;
                        item.Reason = "与 " + targets[item.To] + " 改成了同一个名字";
                    }
                    else
                    {
                        targets[item.To] = item.From;
                    }
                }
            }

            /* ---- 目标路径撞上「不会被改名的既有文件」也算冲突 ---- */
            var untouched = new HashSet<string>(
                plan.Items.Where(i => !i.Changed).Select(i => i.From), StringComparer.OrdinalIgnoreCase);

            foreach (var it in plan.Items.Where(i => i.Changed && !i.Conflict && !i.Invalid))
            {
                if (untouched.Contains(it.To))
                {
                    it.Conflict = true;
                    it.Reason = "目标路径已被另一个不改名的文件占用";
                }
            }

            /* ---- 改名后是否自我覆盖（A→B 且 B→A 这类链）---- */
            var changedFrom = new HashSet<string>(
                plan.Items.Where(i => i.Changed && !i.Invalid).Select(i => i.From), StringComparer.OrdinalIgnoreCase);
            foreach (var it in plan.Items.Where(i => i.Changed && !i.Conflict && !i.Invalid))
            {
                if (changedFrom.Contains(it.To))
                {
                    /* 目标也是某个即将改名的文件 —— 只要最终目标集合不重复就安全，
                       执行时用「先全部读出来再写」的方式规避 */
                    it.Reason = "目标同时也被改名（执行时会安全处理）";
                }
            }

            return plan;
        }

        private static string ReplaceAll(string text, string find, string replace, StringComparison cmp)
        {
            if (string.IsNullOrEmpty(find)) return text;
            var sb = new StringBuilder();
            int i = 0;
            while (true)
            {
                int at = text.IndexOf(find, i, cmp);
                if (at < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, at - i);
                sb.Append(replace);
                i = at + find.Length;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 执行改名。先完整写到暂存目录，再原子替换 outputPath。
        /// outputPath 必须与 RootDir 不同 —— 传 RootDir 会导致源被替换掉。
        /// </summary>
        public static WriteReport Execute(RenamePlan plan, string outputPath, Action<int, int, string> progress)
        {
            var notes = new List<string>();
            Action<string> note = t => notes.Add(t);

            string problem = StagedWriter.CheckOutput(plan.RootDir, outputPath);
            if (problem != null)
            {
                var bad = new WriteReport { Ok = false, Error = problem };
                bad.Notes.Add(problem);
                return bad;
            }

            var report = StagedWriter.Write(
                outputPath,
                StageOutput.Folder,
                staging =>
                {
                    var moves = plan.Items.Where(i => i.Changed && !i.Conflict && !i.Invalid).ToList();
                    var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in moves) renameMap[m.From] = m.To;

                    var all = Directory.GetFiles(plan.RootDir, "*", SearchOption.AllDirectories)
                                       .Select(f => ZipIo.RelPath(plan.RootDir, f))
                                       .ToList();
                    int total = all.Count;
                    int done = 0;

                    foreach (string rel in all)
                    {
                        done++;
                        string target;
                        if (!renameMap.TryGetValue(rel, out target)) target = rel;
                        if (progress != null) progress(done, total, rel + (target != rel ? "  →  " + target : ""));

                        string full = Path.Combine(plan.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(full)) continue;
                        StagedWriter.WriteBytes(staging, target, File.ReadAllBytes(full));
                    }

                    note("改名 " + moves.Count + " 个，复制 " + (total - moves.Count) + " 个");
                },
                note);

            report.Notes.AddRange(notes);
            return report;
        }
    }
}
