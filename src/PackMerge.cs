using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* 合并资源包                                                          */
    /* ================================================================== */

    /// <summary>一个合并来源</summary>
    internal sealed class MergeSource
    {
        public string Path;                 // 文件夹或 ZIP
        public string Label;                // 显示名
        public PackAnalysis Analysis;       // 分析结果（由引擎填充）
        public bool Enabled = true;
        /// <summary>优先级：数字越小越优先（列表从上到下）</summary>
        public int Priority;
    }

    /// <summary>同名文件的冲突记录</summary>
    internal sealed class MergeConflict
    {
        public string RelPath;              // 相对路径（小写用于比较，这里保留原样）
        public List<string> Owners = new List<string>();   // 所有包含该文件的来源 Label
        public string Winner;               // 胜出来源 Label
        public bool SameContent;            // 内容是否完全相同（相同则不算真冲突）
    }

    /// <summary>合并过程中被跳过的文件</summary>
    internal sealed class MergeSkip
    {
        public string RelPath;
        public string Source;
        public string Reason;
    }

    /// <summary>合并决定：每个文件保留谁的版本</summary>
    internal sealed class MergeDecision
    {
        public string RelPath;
        public int WinnerIndex = -1;        // 来源下标；-1 表示排除
        public bool Excluded;
        public List<int> OwnerIndexes = new List<int>();
    }

    internal sealed class MergePlan
    {
        public List<MergeSource> Sources = new List<MergeSource>();
        public List<MergeDecision> Decisions = new List<MergeDecision>();
        public List<string> Notes = new List<string>();

        /// <summary>输出设置</summary>
        public string OutputPath;
        public StageOutput Mode = StageOutput.Folder;
        public string NewDescription;       // 为空则沿用胜出来源的描述
        public int? TargetFormat;           // 为空则沿用胜出来源的 pack_format

        public int ConflictCount
        {
            get { return Decisions.Count(d => d.OwnerIndexes.Count > 1); }
        }

        public int RealConflictCount
        {
            get { return Conflicts.Count(c => !c.SameContent); }
        }

        public List<MergeConflict> Conflicts = new List<MergeConflict>();

        public int KeptCount { get { return Decisions.Count(d => !d.Excluded && d.WinnerIndex >= 0); } }
        public int ExcludedCount { get { return Decisions.Count(d => d.Excluded); } }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("来源 ").Append(Sources.Count(s => s.Enabled)).Append(" 个");
            sb.Append("，文件 ").Append(Decisions.Count);
            if (ConflictCount > 0)
                sb.Append("，同名冲突 ").Append(ConflictCount)
                  .Append("（内容不同 ").Append(RealConflictCount).Append("）");
            if (ExcludedCount > 0) sb.Append("，已排除 ").Append(ExcludedCount);
            return sb.ToString();
        }
    }

    internal static class PackMerger
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// 扫描所有来源，建立「相对路径 → 各来源的版本」索引，产出待决定的列表。
        /// 只读，不改动任何来源。
        /// </summary>
        public static MergePlan Plan(List<MergeSource> sources, Action<string> note)
        {
            var plan = new MergePlan();
            plan.Sources = sources;

            /* ---- 逐个来源分析 ---- */
            int idx = 0;
            foreach (var s in sources)
            {
                s.Priority = idx;
                idx++;

                if (!s.Enabled)
                {
                    plan.Notes.Add("跳过（未启用）：" + s.Label);
                    continue;
                }

                if (note != null) note("分析 " + s.Label + " …");
                var a = PackAnalyzer.Analyze(s.Path);
                s.Analysis = a;

                if (!a.Ok)
                {
                    s.Enabled = false;
                    plan.Notes.Add("跳过（无法读取）：" + s.Label + " —— " + a.Error);
                    continue;
                }
                if (!a.PackMcmetaAtRoot)
                {
                    s.Enabled = false;
                    plan.Notes.Add("跳过（根目录没有 pack.mcmeta）：" + s.Label);
                    continue;
                }
                plan.Notes.Add(s.Label + "：" + a.Counts.Total + " 个文件，"
                               + (a.Format != null ? a.Format.DescribeCurrent() : "版本未知"));
            }

            /* ---- 建立索引：路径 → 来源下标列表（按优先级排序）---- */
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (!s.Enabled || s.Analysis == null) continue;

                foreach (string f in Directory.GetFiles(s.Analysis.RootDir, "*", SearchOption.AllDirectories))
                {
                    string rel = ZipIo.RelPath(s.Analysis.RootDir, f);
                    if (rel.Length == 0) continue;

                    List<int> owners;
                    if (!map.TryGetValue(rel, out owners))
                    {
                        owners = new List<int>();
                        map[rel] = owners;
                    }
                    owners.Add(i);
                }
            }

            /* ---- 逐个路径生成决定（优先级小的胜出）---- */
            var ordered = map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string rel in ordered)
            {
                var owners = map[rel].OrderBy(i => sources[i].Priority).ToList();
                var d = new MergeDecision
                {
                    RelPath = rel,
                    OwnerIndexes = owners,
                    WinnerIndex = owners[0],
                };
                plan.Decisions.Add(d);

                if (owners.Count > 1)
                {
                    var c = new MergeConflict { RelPath = rel, Winner = sources[owners[0]].Label };
                    foreach (int oi in owners) c.Owners.Add(sources[oi].Label);
                    c.SameContent = AllSameContent(sources, rel, owners);
                    plan.Conflicts.Add(c);
                }
            }

            return plan;
        }

        /// <summary>各来源的同一个文件是否内容完全一致（一致就不算真冲突，无需用户决定）</summary>
        private static bool AllSameContent(List<MergeSource> sources, string rel, List<int> owners)
        {
            byte[] first = null;
            foreach (int oi in owners)
            {
                var a = sources[oi].Analysis;
                if (a == null) return false;
                string full = Path.Combine(a.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return false;

                byte[] cur;
                try { cur = File.ReadAllBytes(full); }
                catch { return false; }

                if (first == null) first = cur;
                else if (!BytesEqual(first, cur)) return false;
            }
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>把用户的冲突选择应用回决定列表；conflictWinners 为 relPath → 来源下标</summary>
        public static void ApplyConflictChoices(MergePlan plan, IDictionary<string, int> conflictWinners)
        {
            if (conflictWinners == null) return;
            foreach (var d in plan.Decisions)
            {
                int chosen;
                if (conflictWinners.TryGetValue(d.RelPath, out chosen))
                {
                    if (chosen < 0) { d.Excluded = true; d.WinnerIndex = -1; }
                    else if (d.OwnerIndexes.Contains(chosen)) { d.Excluded = false; d.WinnerIndex = chosen; }
                }
            }
        }

        /// <summary>
        /// 执行合并。产出到暂存目录后原子替换目标。
        /// </summary>
        public static WriteReport Execute(MergePlan plan, Action<int, int, string> progress)
        {
            var notes = new List<string>();
            Action<string> note = t => notes.Add(t);

            var report = StagedWriter.Write(
                plan.OutputPath,
                plan.Mode,
                staging =>
                {
                    int total = plan.Decisions.Count(d => !d.Excluded && d.WinnerIndex >= 0);
                    int done = 0;

                    foreach (var d in plan.Decisions)
                    {
                        if (d.Excluded || d.WinnerIndex < 0) continue;
                        done++;
                        if (progress != null) progress(done, total, d.RelPath);

                        var src = plan.Sources[d.WinnerIndex];
                        string full = Path.Combine(src.Analysis.RootDir,
                                                  d.RelPath.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(full)) continue;

                        StagedWriter.WriteBytes(staging, d.RelPath, File.ReadAllBytes(full));
                    }

                    /* ---- pack.mcmeta：合并版本与描述 ---- */
                    RewriteMcmeta(plan, staging, note);
                },
                note);

            report.Notes.AddRange(notes);
            return report;
        }

        /// <summary>把胜出来源的 pack.mcmeta 拿来，按需覆盖 description 与 pack_format</summary>
        private static void RewriteMcmeta(MergePlan plan, string staging, Action<string> note)
        {
            string mcmeta = Path.Combine(staging, "pack.mcmeta");
            string text = File.Exists(mcmeta) ? File.ReadAllText(mcmeta, Encoding.UTF8) : null;

            if (text == null)
            {
                /* 没有 pack.mcmeta（所有来源都没有），生成一个最小的 */
                text = "{\n  \"pack\": {\n    \"pack_format\": 15,\n    \"description\": \"MCPack Studio 合并输出\"\n  }\n}\n";
                note("来源没有 pack.mcmeta，已生成默认的");
            }

            JsonValue root;
            string err;
            if (!Json.TryParse(text, out root, out err) || root == null || !root.IsObject)
            {
                note("pack.mcmeta 解析失败（" + err + "），保持原样");
                StagedWriter.WriteText(staging, "pack.mcmeta", text);
                return;
            }

            JsonValue pack = root.Get("pack");
            if (pack == null || !pack.IsObject)
            {
                pack = JsonValue.Obj();
                root.Set("pack", pack);
            }

            if (plan.TargetFormat.HasValue)
            {
                /* 新式范围声明与旧的单一数字互斥，统一改回单一数字 */
                pack.Remove("min_format");
                pack.Remove("max_format");
                pack.Remove("supported_formats");
                pack.Set("pack_format", JsonValue.Num(plan.TargetFormat.Value));
                note("pack_format 已设为 " + plan.TargetFormat.Value);
            }

            if (!string.IsNullOrEmpty(plan.NewDescription))
            {
                pack.Set("description", JsonValue.Str(plan.NewDescription));
                note("描述已更新");
            }

            StagedWriter.WriteText(staging, "pack.mcmeta", Json.Write(root, true) + "\n");
        }
    }
}
