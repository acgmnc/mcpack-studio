using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    internal enum OutputMode { Folder, Zip }

    internal sealed class ConvertOptions
    {
        public OutputMode Mode = OutputMode.Folder;
        public string OutputPath;              // 目标文件夹或目标 zip 路径
        public int TargetFormat;
        public string TargetVersionLabel;

        /// <summary>是否执行资源迁移（关掉就只改 pack.mcmeta，等价于原项目行为）</summary>
        public bool MigrateAssets = true;

        /// <summary>是否改写语言文件内容（键名加 minecraft 命名空间）</summary>
        public bool MigrateLangContent = true;

        /// <summary>被用户勾掉、不参与转换的相对路径（小写比较）</summary>
        public HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class ConvertPlan
    {
        public PackAnalysis Analysis;
        public ConvertOptions Options;
        public List<FileAction> Actions = new List<FileAction>();
        public List<string> RuleIds = new List<string>();
        public List<string> Notes = new List<string>();
        public string PackMcmetaSourcePath;
        public string NewDescription;

        public int RenameCount { get { return Actions.Count(x => x.Kind == PlanActionKind.Rename); } }
        public int RewriteCount { get { return Actions.Count(x => x.Kind == PlanActionKind.Rewrite); } }
        public int ExcludedCount { get { return Actions.Count(x => x.Kind == PlanActionKind.Exclude); } }
        public int PassthroughCount { get { return Actions.Count(x => x.Kind == PlanActionKind.Keep); } }
        public int TotalFiles { get { return Actions.Count; } }

        public long EstimatedBytes
        {
            get
            {
                long n = 0;
                foreach (var a in Actions.Where(x => x.Kind != PlanActionKind.Exclude))
                {
                    n += a.NewContent != null ? a.NewContent.Length : 0;
                }
                return n;
            }
        }

        /// <summary>一句话说明这个计划要做什么</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("共 ").Append(TotalFiles).Append(" 个文件");
            if (RenameCount > 0) sb.Append("，重命名 ").Append(RenameCount);
            if (RewriteCount > 0) sb.Append("，改写 ").Append(RewriteCount);
            if (ExcludedCount > 0) sb.Append("，排除 ").Append(ExcludedCount);
            sb.Append("，直接复制 ").Append(PassthroughCount);
            return sb.ToString();
        }
    }

    /* ================================================================== */
    /* 规划                                                                */
    /* ================================================================== */

    internal static class PlanBuilder
    {
        public static ConvertPlan Build(PackAnalysis a, ConvertOptions opt)
        {
            var plan = new ConvertPlan { Analysis = a, Options = opt };

            if (!a.Ok)
            {
                plan.Notes.Add("资源包分析失败：" + a.Error);
                return plan;
            }

            int fromFormat = a.Format != null && a.Format.EffectiveFormat.HasValue
                ? a.Format.EffectiveFormat.Value
                : 0;
            int toFormat = opt.TargetFormat;

            bool metadataOnly = !opt.MigrateAssets;

            /* -------- 收集所有文件 -------- */
            var files = Directory.GetFiles(a.RootDir, "*", SearchOption.AllDirectories)
                                 .Select(f => ZipIo.RelPath(a.RootDir, f))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            /* -------- 触发哪些规则 -------- */
            var activeRules = new List<MigrationRule>();
            if (!metadataOnly)
            {
                foreach (var rule in MigrationRules.All)
                {
                    if (rule.Crosses(fromFormat, toFormat)) activeRules.Add(rule);
                }
            }

            bool langBoundaryCrossed = !metadataOnly && (fromFormat < 4) != (toFormat < 4);

            /* -------- 逐文件生成动作 -------- */
            foreach (string rel in files)
            {
                var action = new FileAction { From = rel, To = rel };

                if (string.Equals(rel, "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                {
                    plan.PackMcmetaSourcePath = Path.Combine(a.RootDir, rel);
                    action.Kind = PlanActionKind.Rewrite;
                    action.RuleId = "pack-mcmeta";
                    action.Notes.Add("写入 pack_format = " + toFormat + "（目标是 " + opt.TargetVersionLabel + "）");
                    plan.Actions.Add(action);
                    continue;
                }

                if (opt.Excluded.Contains(rel))
                {
                    action.Kind = PlanActionKind.Exclude;
                    action.Notes.Add("已按选择排除，不会出现在输出里");
                    plan.Actions.Add(action);
                    continue;
                }

                string newPath = rel;

                // 1) 目录前缀重命名
                if (!metadataOnly)
                {
                    string lower = newPath.Replace('\\', '/').ToLowerInvariant();
                    foreach (var rule in activeRules)
                    {
                        // 方向很关键：旧 → 新用 OldPrefix，新 → 旧要把两者对调
                        bool forward = rule.IsForward(fromFormat);
                        string search = forward ? rule.OldPrefix : rule.NewPrefix;
                        string replacement = forward ? rule.NewPrefix : rule.OldPrefix;

                        int at;
                        if (rule.AppliesToFileName)
                        {
                            at = lower.LastIndexOf(search, StringComparison.Ordinal);
                            if (at < 0) continue;
                            if (at + search.Length != lower.Length) continue;  // 必须是整段后缀
                        }
                        else
                        {
                            at = lower.IndexOf(search, StringComparison.Ordinal);
                            if (at < 0) continue;
                            // 要求前缀从段首开始（避免命中 "mytextures/blocks/"）
                            if (at != 0 && lower[at - 1] != '/') continue;
                        }

                        newPath = rel.Substring(0, at) + replacement + rel.Substring(at + search.Length);
                        action.RuleId = rule.Id;
                        action.Notes.Add(rule.Title);
                        if (!plan.RuleIds.Contains(rule.Id)) plan.RuleIds.Add(rule.Id);
                        break;
                    }
                }

                // 2) 语言文件改名
                if (langBoundaryCrossed)
                {
                    string mapped; string note;
                    bool mappedOk;
                    if (fromFormat < 4)
                    {
                        // 旧 → 新：地区码小写 + .lang 转 .json
                        mappedOk = MigrationRules.TryMapLangFile(newPath, fromFormat, toFormat, out mapped, out note);
                    }
                    else
                    {
                        // 新 → 旧：改回 .lang（旧版客户端不认 .json）
                        mappedOk = MigrationRules.TryMapLangFileBackward(newPath, out mapped, out note);
                    }

                    if (mappedOk)
                    {
                        newPath = mapped;
                        action.RuleId = "flatten-lang-file";
                        action.Notes.Add("语言文件规范化：" + note);
                        if (!plan.RuleIds.Contains("flatten-lang-file")) plan.RuleIds.Add("flatten-lang-file");
                    }
                }

                action.To = newPath;

                // 3) 语言文件内容改写（键名迁移 + 格式转换）
                if (langBoundaryCrossed && IsLangFile(rel))
                {
                    try
                    {
                        string full = Path.Combine(a.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                        var converted = LangRewriter.Rewrite(full, newPath, opt.MigrateLangContent);
                        if (converted != null)
                        {
                            action.NewContent = converted.Bytes;

                            bool extChanged = !string.Equals(
                                Path.GetExtension(rel), Path.GetExtension(newPath), StringComparison.OrdinalIgnoreCase);

                            var bits = new List<string>();
                            if (extChanged) bits.Add("转为 " + Path.GetExtension(newPath).TrimStart('.').ToUpperInvariant() + " 格式");
                            if (converted.ChangedCount > 0) bits.Add("语言键改写 " + converted.ChangedCount + " 条");
                            if (converted.SkippedCount > 0) bits.Add("跳过 " + converted.SkippedCount + " 条已带命名空间的键");

                            if (bits.Count > 0)
                            {
                                action.Notes.Add(string.Join("；", bits.ToArray()));
                                if (converted.ChangedCount > 0 && !plan.RuleIds.Contains("flatten-lang-keys"))
                                    plan.RuleIds.Add("flatten-lang-keys");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        action.Notes.Add("语言文件改写失败，将原样复制：" + e.Message);
                    }
                }

                if (action.NewContent != null) action.Kind = PlanActionKind.Rewrite;
                else if (!string.Equals(action.To, action.From, StringComparison.Ordinal)) action.Kind = PlanActionKind.Rename;
                else action.Kind = PlanActionKind.Keep;

                plan.Actions.Add(action);
            }

            /* -------- 重命名冲突检测 -------- */
            DetectConflicts(plan);

            /* -------- 说明 -------- */
            if (metadataOnly)
            {
                plan.Notes.Add("只改元数据模式：仅重写 pack.mcmeta 的版本号，资源文件全部原样复制。");
            }
            else if (activeRules.Count == 0 && !langBoundaryCrossed)
            {
                plan.Notes.Add("源格式 " + fromFormat + " 与目标格式 " + toFormat +
                    " 之间没有本工具收录的资源迁移规则，因此只更新版本号，资源文件原样复制。");
            }
            else
            {
                plan.Notes.Add("已启用 " + activeRules.Count + " 条路径迁移规则" +
                    (langBoundaryCrossed ? "，并会改写语言文件" : "") + "。");
            }

            return plan;
        }

        private static bool IsLangFile(string rel)
        {
            string lower = rel.ToLowerInvariant();
            if (!lower.Contains("/lang/") && !lower.StartsWith("lang/")) return false;
            return lower.EndsWith(".lang") || lower.EndsWith(".json");
        }

        /// <summary>
        /// 同名冲突：如果 A 要改名成 B，而 B 本来也存在，则保留 A 的原名，
        /// 避免静默覆盖掉原本就在目标位置的文件。
        /// </summary>
        private static void DetectConflicts(ConvertPlan plan)
        {
            var byTarget = new Dictionary<string, List<FileAction>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in plan.Actions)
            {
                if (a.Kind == PlanActionKind.Exclude) continue;
                string key = a.To;
                if (!byTarget.ContainsKey(key)) byTarget[key] = new List<FileAction>();
                byTarget[key].Add(a);
            }

            var originalPaths = new HashSet<string>(
                plan.Actions.Where(x => x.Kind != PlanActionKind.Exclude).Select(x => x.From),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kv in byTarget)
            {
                if (kv.Value.Count <= 1) continue;

                // 找出"本来就在这个路径上"的那个文件，它优先
                FileAction owner = kv.Value.FirstOrDefault(x =>
                    string.Equals(x.From, kv.Key, StringComparison.OrdinalIgnoreCase));

                foreach (var a in kv.Value)
                {
                    if (owner != null && ReferenceEquals(a, owner)) continue;

                    if (owner != null)
                    {
                        a.To = a.From;   // 退回原名，不覆盖
                        a.Notes.Add("目标路径 " + kv.Key + " 已存在同名文件，保持原路径不覆盖");
                        if (a.Kind == PlanActionKind.Rename) a.Kind = PlanActionKind.Keep;
                    }
                    else
                    {
                        // 两个改名文件撞在同一点：后一个退回原名
                        a.To = a.From;
                        a.Notes.Add("多个文件会重命名到同一路径，此文件保持原路径");
                        if (a.Kind == PlanActionKind.Rename) a.Kind = PlanActionKind.Keep;
                    }
                }
            }

            // 冲突处理可能让某些文件退回原名，若原名又被别人占用则再加后缀
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in plan.Actions.Where(x => x.Kind != PlanActionKind.Exclude))
            {
                string candidate = a.To;
                if (seen.Contains(candidate))
                {
                    string dir = Path.GetDirectoryName(candidate.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                    string stem = Path.GetFileNameWithoutExtension(candidate);
                    string ext = Path.GetExtension(candidate);
                    int n = 2;
                    string next;
                    do
                    {
                        next = (dir.Length > 0 ? dir.Replace('\\', '/') + "/" : "") + stem + " (" + n + ")" + ext;
                        n++;
                    } while (seen.Contains(next));
                    a.Notes.Add("输出路径重复，已改名避免覆盖：" + next);
                    a.To = next;
                    candidate = next;
                }
                seen.Add(candidate);
            }
        }
    }

    /* ================================================================== */
    /* 语言文件改写                                                        */
    /* ================================================================== */

    internal sealed class LangRewriteResult
    {
        public byte[] Bytes;
        public int ChangedCount;
        public int SkippedCount;
    }

    internal static class LangRewriter
    {
        /// <summary>
        /// 改写语言文件。做两件事：
        ///   1. 键名迁移 block.x → block.minecraft.x（只在跨越 1.13 边界时）
        ///   2. 内容格式转换 .lang 的 key=value ↔ JSON（1.13 起只认 JSON）
        /// </summary>
        public static LangRewriteResult Rewrite(string sourceFile, string newRelPath, bool migrateKeys)
        {
            byte[] raw = File.ReadAllBytes(sourceFile);
            string text = Encoding.UTF8.GetString(raw);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            bool sourceIsLang = sourceFile.EndsWith(".lang", StringComparison.OrdinalIgnoreCase)
                || sourceFile.EndsWith(".properties", StringComparison.OrdinalIgnoreCase);
            string trimmed = text.TrimStart();
            bool contentLooksJson = trimmed.StartsWith("{", StringComparison.Ordinal);

            var result = new LangRewriteResult();

            if (!sourceIsLang && contentLooksJson)
            {
                /* ---- 源就是 JSON ---- */
                JsonValue root;
                string err;
                if (!Json.TryParse(text, out root, out err) || root == null || !root.IsObject)
                {
                    return null;   // 解析不了就不动，避免弄坏文件
                }

                var pairs = new List<KeyValuePair<string, string>>();
                foreach (var kv in root.Members)
                {
                    pairs.Add(new KeyValuePair<string, string>(kv.Key, kv.Value != null ? kv.Value.AsText() : ""));
                }

                return Emit(pairs, newRelPath, migrateKeys, result);
            }
            else
            {
                /* ---- 源是 .lang（key=value） ---- */
                var pairs = ParseLang(text, result);
                return Emit(pairs, newRelPath, migrateKeys, result);
            }
        }

        /// <summary>解析 key=value 文本，顺带处理常见转义</summary>
        private static List<KeyValuePair<string, string>> ParseLang(string text, LangRewriteResult result)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("//")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);
                if (key.Length == 0) continue;

                pairs.Add(new KeyValuePair<string, string>(key, Unescape(value)));
            }
            return pairs;
        }

        /// <summary>把 \n \t \\ 之类的转义还原成真实字符</summary>
        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case '"': sb.Append('"'); i++; continue;
                        default: break;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>按键名迁移 + 按目标扩展名决定输出格式</summary>
        private static LangRewriteResult Emit(
            List<KeyValuePair<string, string>> pairs, string targetRelPath, bool migrateKeys, LangRewriteResult result)
        {
            bool targetIsJson = targetRelPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

            int changed = 0, skipped = 0;
            var finalPairs = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var kv in pairs)
            {
                string newKey = kv.Key;
                if (migrateKeys)
                {
                    newKey = MigrationRules.MigrateLangKey(kv.Key);
                    if (newKey != kv.Key) changed++;
                    else if (kv.Key.Contains(".minecraft.")) skipped++;
                }
                if (!seen.Add(newKey)) continue;   // 键冲突时保留先出现的
                finalPairs.Add(new KeyValuePair<string, string>(newKey, kv.Value));
            }

            byte[] bytes;
            if (targetIsJson)
            {
                var obj = JsonValue.Obj();
                foreach (var kv in finalPairs) obj.Set(kv.Key, JsonValue.Str(kv.Value));
                bytes = new UTF8Encoding(false).GetBytes(Json.Write(obj, true));
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var kv in finalPairs)
                {
                    sb.Append(kv.Key).Append('=').Append(EscapeForLang(kv.Value)).Append('\n');
                }
                bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            }

            result.ChangedCount = changed;
            result.SkippedCount = skipped;
            result.Bytes = bytes;
            return result;
        }

        /// <summary>写出 .lang 时把换行等还原成转义写法</summary>
        private static string EscapeForLang(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new char[] { '\n', '\r', '\t', '\\' }) < 0) return s;
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }

    /* ================================================================== */
    /* ================================================================== */
    /* pack.mcmeta 重写                                                    */
    /* ================================================================== */

    internal static class PackMetaRewriter
    {
        /// <summary>计算目标 pack.mcmeta 的文本内容</summary>
        public static string Build(string sourceJson, int targetFormat, string targetVersionLabel, out string error)
        {
            error = null;

            JsonValue root;
            string parseErr;
            if (!Json.TryParse(sourceJson ?? "", out root, out parseErr) || root == null || !root.IsObject)
            {
                // 原文件坏了：重新生成一个最小可用的
                root = JsonValue.Obj();
                var fresh = JsonValue.Obj();
                fresh.Set("pack_format", JsonValue.Num(targetFormat));
                fresh.Set("description", JsonValue.Str(
                    "§b由 MCPack Studio 转换至 " + targetVersionLabel + "\n§7原 pack.mcmeta 无法解析"));
                root.Set("pack", fresh);
                error = parseErr;
                return Json.Write(root, true);
            }

            JsonValue pack = root.Get("pack");
            if (pack == null || !pack.IsObject)
            {
                pack = JsonValue.Obj();
                root.Set("pack", pack);
            }

            // 记录原写法，用于决定是否保留 min/max_format 新格式
            bool hadNewFormat = pack.Has("min_format") || pack.Has("max_format");
            JsonValue oldDesc = pack.Get("description");
            string oldDescText = oldDesc != null ? oldDesc.AsText() : null;

            // 清掉所有版本声明字段，再按目标格式写回
            pack.Remove("supported_formats");
            pack.Remove("supported_format");
            pack.Remove("min_format");
            pack.Remove("max_format");
            pack.Remove("pack_format");

            if (targetFormat >= Settings.NewFormatThreshold && hadNewFormat)
            {
                // 原文用的是 1.21.9+ 新格式，保持新格式
                var minArr = JsonValue.Arr();
                minArr.Items.Add(JsonValue.Num(targetFormat));
                minArr.Items.Add(JsonValue.Num(0));

                var maxArr = JsonValue.Arr();
                maxArr.Items.Add(JsonValue.Num(999));
                maxArr.Items.Add(JsonValue.Num(0));

                pack.Set("pack_format", JsonValue.Num(targetFormat));
                pack.Set("min_format", minArr);
                pack.Set("max_format", maxArr);
            }
            else
            {
                pack.Set("pack_format", JsonValue.Num(targetFormat));
            }

            // 追加一行转换标记，方便事后追溯（保留原描述）
            string marker = "§8[已由 MCPack Studio 转换至 " + targetVersionLabel + "]";
            string newDesc;
            if (string.IsNullOrEmpty(oldDescText))
            {
                newDesc = marker;
            }
            else if (oldDescText.Contains("MCPack Studio 转换至"))
            {
                newDesc = oldDescText;   // 已经有标记就不再叠加
            }
            else
            {
                newDesc = oldDescText + "\n" + marker;
            }
            pack.Set("description", JsonValue.Str(newDesc));

            string output = Json.Write(root, true);

            // 自检：生成的必须是合法 JSON
            JsonValue verify;
            string verifyErr;
            if (!Json.TryParse(output, out verify, out verifyErr))
            {
                error = "内部错误：生成的 pack.mcmeta 不是合法 JSON（" + verifyErr + "）";
            }
            return output;
        }
    }

    /* ================================================================== */
    /* 执行                                                                */
    /* ================================================================== */

    internal sealed class ConvertReport
    {
        public bool Success;
        public string Error;
        public string OutputPath;
        public int TotalFiles;
        public int Written;
        public int Renamed;
        public int Rewritten;
        public int Excluded;
        public long OutputBytes;
        public long ElapsedMs;
        public List<string> RulesApplied = new List<string>();
        public List<string> Notes = new List<string>();
        public List<string> Problems = new List<string>();
        public string PackMcmetaBefore;
        public string PackMcmetaAfter;

        public string Describe()
        {
            if (!Success) return "转换失败：" + Error;
            var sb = new StringBuilder();
            sb.Append("转换完成，输出 ").Append(Written).Append(" 个文件");
            if (Renamed > 0) sb.Append("，重命名 ").Append(Renamed).Append(" 个");
            if (Rewritten > 0) sb.Append("，改写内容 ").Append(Rewritten).Append(" 个");
            if (Excluded > 0) sb.Append("，排除 ").Append(Excluded).Append(" 个");
            sb.Append("，用时 ").Append(ElapsedMs).Append(" 毫秒");
            return sb.ToString();
        }
    }

    internal static class ConvertExecutor
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static ConvertReport Execute(ConvertPlan plan, Action<int, int, string> progress)
        {
            var report = new ConvertReport();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            plan.Notes.ForEach(n => report.Notes.Add(n));

            string stagingDir = null;
            string backupDir = null;
            bool committed = false;

            try
            {
                var a = plan.Analysis;
                if (!a.Ok) throw new InvalidOperationException(a.Error ?? "资源包不可用");
                if (!a.PackMcmetaAtRoot) throw new InvalidOperationException("资源包根目录没有 pack.mcmeta，无法转换");

                string outPath = Path.GetFullPath(plan.Options.OutputPath);
                bool outputIsZip = plan.Options.Mode == OutputMode.Zip;

                /* ---- 目标位置合法性检查 ---- */
                if (!outputIsZip)
                {
                    if (ZipIo.IsInside(a.RootDir, outPath) && !string.Equals(a.RootDir, outPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("输出目录不能在输入目录内部，否则会递归复制自己");

                    if (string.Equals(a.RootDir, outPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("输出路径与输入路径相同，请选择另一个位置（工具不会原地覆盖）");
                }

                /* ---- 准备暂存目录（先完整生成，再一次性就位，避免写坏源） ---- */
                // 暂存目录尽量和目标放在同一个卷上，这样最后可以用 Move（瞬间完成）；
                // 跨卷时退回「复制 + 删除」。
                stagingDir = MakeStagingDir(outPath);

                /* ---- 应用 pack.mcmeta ---- */
                string newMcmeta = null;
                string mcmetaErr;
                if (plan.PackMcmetaSourcePath != null && File.Exists(plan.PackMcmetaSourcePath))
                {
                    report.PackMcmetaBefore = File.ReadAllText(plan.PackMcmetaSourcePath, Encoding.UTF8);
                    newMcmeta = PackMetaRewriter.Build(
                        report.PackMcmetaBefore, plan.Options.TargetFormat, plan.Options.TargetVersionLabel, out mcmetaErr);
                    report.PackMcmetaAfter = newMcmeta;
                    if (!string.IsNullOrEmpty(mcmetaErr)) report.Problems.Add("pack.mcmeta：" + mcmetaErr);
                }
                else
                {
                    throw new InvalidOperationException("找不到 pack.mcmeta");
                }

                /* ---- 逐文件写出 ---- */
                int total = plan.Actions.Count;
                int index = 0;
                var writtenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var action in plan.Actions)
                {
                    index++;
                    if (progress != null) progress(index, total, action.From);

                    if (action.Kind == PlanActionKind.Exclude)
                    {
                        report.Excluded++;
                        continue;
                    }

                    string targetRel = action.To;
                    string targetFull = Path.Combine(stagingDir, targetRel.Replace('/', Path.DirectorySeparatorChar));
                    if (!ZipIo.IsInside(stagingDir, targetFull))
                    {
                        report.Problems.Add("跳过越界路径：" + targetRel);
                        continue;
                    }

                    if (!writtenTargets.Add(targetRel))
                    {
                        report.Problems.Add("输出路径重复，已跳过：" + targetRel);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFull));

                    if (string.Equals(action.From, "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(targetFull, newMcmeta, Utf8NoBom);
                    }
                    else if (action.NewContent != null)
                    {
                        File.WriteAllBytes(targetFull, action.NewContent);
                    }
                    else
                    {
                        string sourceFull = Path.Combine(a.RootDir, action.From.Replace('/', Path.DirectorySeparatorChar));
                        File.Copy(sourceFull, targetFull, true);
                    }

                    report.Written++;
                    if (action.Kind == PlanActionKind.Rename) report.Renamed++;
                    else if (action.Kind == PlanActionKind.Rewrite && !string.Equals(action.From, "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                        report.Rewritten++;
                }

                /* ---- 就位 ---- */
                if (outputIsZip)
                {
                    report.OutputPath = outPath;
                    ZipIo.CreateFromDirectory(stagingDir, outPath, null);
                    try { report.OutputBytes = new FileInfo(outPath).Length; } catch { }
                }
                else
                {
                    report.OutputPath = outPath;
                    string parent = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                    if (Directory.Exists(outPath))
                    {
                        // 先挪到备份位置，成功后再删；失败可以还原
                        backupDir = outPath + ".mcpack_backup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        MoveOrCopy(outPath, backupDir);
                    }

                    MoveOrCopy(stagingDir, outPath);
                    stagingDir = null;
                    committed = true;

                    if (backupDir != null)
                    {
                        ZipIo.DeleteDirectory(backupDir);
                        backupDir = null;
                    }
                    try { report.OutputBytes = DirSize(outPath); } catch { }
                }

                report.TotalFiles = total;
                report.RulesApplied.AddRange(plan.RuleIds);
                report.Success = true;
            }
            catch (Exception e)
            {
                report.Success = false;
                report.Error = e.Message;

                // 还原：如果原来的输出目录被挪走了，挪回来
                if (!committed && backupDir != null && !Directory.Exists(plan.Options.OutputPath))
                {
                    try { MoveOrCopy(backupDir, plan.Options.OutputPath); backupDir = null; }
                    catch { }
                }
            }
            finally
            {
                if (stagingDir != null) ZipIo.SafeDelete(stagingDir);
                if (backupDir != null) ZipIo.DeleteDirectory(backupDir);
                sw.Stop();
                report.ElapsedMs = sw.ElapsedMilliseconds;
            }

            return report;
        }

        private static long DirSize(string dir)
        {
            long n = 0;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { n += new FileInfo(f).Length; } catch { }
            }
            return n;
        }

        /// <summary>
        /// 暂存目录。优先放在目标所在卷（最后一步可以用 Move，瞬间完成），
        /// 拿不到就退回系统临时目录，最后一步改用复制。
        /// </summary>
        private static string MakeStagingDir(string outputPath)
        {
            string dir;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    dir = Path.Combine(root, ".mcpack_build_" + Guid.NewGuid().ToString("N").Substring(0, 12));
                }
                else
                {
                    dir = Path.Combine(Path.GetTempPath(), "mcpack_build_" + Guid.NewGuid().ToString("N"));
                }
            }
            catch
            {
                dir = Path.Combine(Path.GetTempPath(), "mcpack_build_" + Guid.NewGuid().ToString("N"));
            }
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>同卷用 Move，跨卷退回「复制 + 删除」</summary>
        private static void MoveOrCopy(string from, string to)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException)
            {
                // 跨卷 Move 会抛 IOException，继续走复制
            }
            catch (UnauthorizedAccessException)
            {
                // 某些情况下也会这样，尝试复制
            }

            CopyDirectory(from, to);
            ZipIo.DeleteDirectory(from);
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, ZipIo.RelPath(from, dir).Replace('/', Path.DirectorySeparatorChar)));
            }
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = ZipIo.RelPath(from, file).Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }
    }
}
