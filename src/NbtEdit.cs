using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace McPackStudio
{
    /* ================================================================== */
    /* 查找规则                                                            */
    /* ================================================================== */

    /// <summary>文字匹配方式（对齐 NBTExplorer 的 Wildcard / String / Value 三类查找）</summary>
    internal enum NbtMatchMode
    {
        Exact,      // 完全相同
        Contains,   // 包含
        Wildcard,   // 通配符：* 任意多字符，? 一个字符
        Regex,      // 正则
    }

    /// <summary>数值比较方式</summary>
    internal enum NbtValueCompare
    {
        None,       // 不按值筛选
        Equal,
        NotEqual,
        Greater,
        Less,
        Contains,   // 字符串包含
        StartsWith,
    }

    /// <summary>
    /// 一条查找/替换规则。名称、类型、值三个条件同时满足才算命中；
    /// 留空的条件表示「不限制」。
    /// </summary>
    internal sealed class NbtSearchRule
    {
        public string NamePattern = "";
        public NbtMatchMode NameMode = NbtMatchMode.Wildcard;
        public bool IgnoreCase = true;

        public bool FilterByType;
        public NbtType Type;

        public NbtValueCompare ValueCompare = NbtValueCompare.None;
        public string ValueText = "";

        /// <summary>替换时用的新值（只在「替换」里用得到）</summary>
        public string ReplaceValue = "";

        public string Describe()
        {
            var sb = new StringBuilder();
            if (NamePattern.Length == 0) sb.Append("名称不限");
            else sb.Append("名称").Append(ModeName(NameMode)).Append("「").Append(NamePattern).Append("」");

            if (FilterByType) sb.Append("，类型 ").Append(NbtTypeInfo.TagName(Type));

            if (ValueCompare != NbtValueCompare.None)
                sb.Append("，值").Append(CompareName(ValueCompare)).Append("「").Append(ValueText).Append("」");

            return sb.ToString();
        }

        private static string ModeName(NbtMatchMode m)
        {
            switch (m)
            {
                case NbtMatchMode.Exact: return "等于";
                case NbtMatchMode.Contains: return "包含";
                case NbtMatchMode.Wildcard: return "匹配";
                case NbtMatchMode.Regex: return "正则匹配";
            }
            return "匹配";
        }

        private static string CompareName(NbtValueCompare c)
        {
            switch (c)
            {
                case NbtValueCompare.Equal: return "等于";
                case NbtValueCompare.NotEqual: return "不等于";
                case NbtValueCompare.Greater: return "大于";
                case NbtValueCompare.Less: return "小于";
                case NbtValueCompare.Contains: return "包含";
                case NbtValueCompare.StartsWith: return "以…开头";
            }
            return "";
        }
    }

    internal static class NbtSearch
    {
        /// <summary>把通配符写法翻译成正则（NBTExplorer 的 * 与 ? 语义）</summary>
        public static string WildcardToRegex(string pattern)
        {
            var sb = new StringBuilder();
            foreach (char c in pattern)
            {
                if (c == '*') sb.Append(".*");
                else if (c == '?') sb.Append('.');
                else sb.Append(Regex.Escape(c.ToString()));
            }
            return sb.ToString();
        }

        public static bool MatchText(string text, string pattern, NbtMatchMode mode, bool ignoreCase)
        {
            if (pattern == null) pattern = "";
            if (text == null) text = "";

            /* 空模式 = 不限制 */
            if (pattern.Length == 0) return true;

            StringComparison cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (mode)
            {
                case NbtMatchMode.Exact:
                    return string.Equals(text, pattern, cmp);

                case NbtMatchMode.Contains:
                    return text.IndexOf(pattern, cmp) >= 0;

                case NbtMatchMode.Wildcard:
                    {
                        var re = new Regex("^" + WildcardToRegex(pattern) + "$",
                            RegexOptions.Singleline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
                        return re.IsMatch(text);
                    }

                case NbtMatchMode.Regex:
                    {
                        var re = new Regex(pattern,
                            RegexOptions.Singleline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
                        return re.IsMatch(text);
                    }
            }
            return false;
        }

        /// <summary>规则里的正则能不能编译（界面上要提前提示，别等查找时才炸）</summary>
        public static string ValidatePattern(NbtSearchRule rule)
        {
            try
            {
                if (rule.NamePattern != null && rule.NamePattern.Length > 0 &&
                    (rule.NameMode == NbtMatchMode.Regex || rule.NameMode == NbtMatchMode.Wildcard))
                    new Regex(rule.NameMode == NbtMatchMode.Regex
                        ? rule.NamePattern
                        : "^" + WildcardToRegex(rule.NamePattern) + "$").IsMatch("");
            }
            catch (Exception e)
            {
                return "名称的正则写法有问题：" + e.Message;
            }
            return null;
        }

        public static bool Match(NbtTag tag, NbtSearchRule rule)
        {
            if (tag == null || rule == null) return false;
            if (!MatchText(tag.Name, rule.NamePattern, rule.NameMode, rule.IgnoreCase)) return false;
            if (rule.FilterByType && tag.Type != rule.Type) return false;
            return MatchValue(tag, rule);
        }

        private static bool MatchValue(NbtTag tag, NbtSearchRule rule)
        {
            if (rule.ValueCompare == NbtValueCompare.None) return true;

            string want = rule.ValueText ?? "";

            if (rule.ValueCompare == NbtValueCompare.Contains || rule.ValueCompare == NbtValueCompare.StartsWith)
            {
                string text = tag.ValueText();
                StringComparison cmp = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (rule.ValueCompare == NbtValueCompare.Contains) return text.IndexOf(want, cmp) >= 0;
                return text.StartsWith(want, cmp);
            }

            /* 数值比较：把标签的值与目标值都转成 double 比 */
            double actual;
            if (!TryNumber(tag, out actual)) return false;

            double target;
            try { target = NbtText.ParseDouble(want, "查找值"); }
            catch (FormatException) { return false; }

            switch (rule.ValueCompare)
            {
                case NbtValueCompare.Equal: return actual == target;
                case NbtValueCompare.NotEqual: return actual != target;
                case NbtValueCompare.Greater: return actual > target;
                case NbtValueCompare.Less: return actual < target;
            }
            return false;
        }

        public static bool TryNumber(NbtTag tag, out double value)
        {
            value = 0;
            var b = tag as NbtByte; if (b != null) { value = b.Value; return true; }
            var s = tag as NbtShort; if (s != null) { value = s.Value; return true; }
            var i = tag as NbtInt; if (i != null) { value = i.Value; return true; }
            var l = tag as NbtLong; if (l != null) { value = l.Value; return true; }
            var f = tag as NbtFloat; if (f != null) { value = f.Value; return true; }
            var d = tag as NbtDouble; if (d != null) { value = d.Value; return true; }
            return false;
        }

        /// <summary>先序遍历找全部命中项</summary>
        public static List<NbtSpot> FindAll(NbtTag root, NbtSearchRule rule, int limit)
        {
            var hits = new List<NbtSpot>();
            if (root == null || rule == null) return hits;
            if (limit <= 0) limit = int.MaxValue;

            foreach (var spot in NbtWalk.All(root))
            {
                if (Match(spot.Tag, rule))
                {
                    hits.Add(spot);
                    if (hits.Count >= limit) break;
                }
            }
            return hits;
        }
    }

    /* ================================================================== */
    /* 撤销 / 重做                                                         */
    /* ================================================================== */

    /// <summary>
    /// 快照式撤销（和 NBTExplorer 的 SnapshotList 一个思路）：
    /// 每次改动前把整棵树的深拷贝压栈。实现简单、绝不会出现「撤销到一半状态不对」，
    /// 代价是内存 —— 所以超大文件会自动关掉撤销并在界面上说明。
    /// </summary>
    internal sealed class NbtHistory
    {
        private sealed class Snapshot
        {
            public NbtTag Root;
            public string RootName;
        }

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();

        /// <summary>保留多少步（NBTExplorer 默认也是几十步这个量级）</summary>
        public const int Limit = 32;

        /// <summary>树太大就不做快照，避免一次编辑吃掉几百 MB</summary>
        public const int MaxNodes = 400000;

        public bool Disabled;

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }
        public int UndoDepth { get { return _undo.Count; } }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>改动前调用，记录当前状态</summary>
        public void Push(NbtCompound root, string rootName)
        {
            if (Disabled || root == null) return;

            _undo.Add(new Snapshot { Root = root.Clone(), RootName = rootName });
            while (_undo.Count > Limit) _undo.RemoveAt(0);
            _redo.Clear();
        }

        /// <summary>
        /// 丢掉刚刚压入的那一步。用于「先记快照、再验证输入」的写法：
        /// 验证失败时不能把没生效的操作留在撤销栈里。
        /// </summary>
        public void PopLast()
        {
            if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
        }

        public bool Undo(ref NbtCompound root, ref string rootName)
        {
            if (_undo.Count == 0) return false;

            var snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(new Snapshot { Root = root.Clone(), RootName = rootName });

            root = (NbtCompound)snap.Root.Clone();
            rootName = snap.RootName;
            return true;
        }

        public bool Redo(ref NbtCompound root, ref string rootName)
        {
            if (_redo.Count == 0) return false;

            var snap = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(new Snapshot { Root = root.Clone(), RootName = rootName });

            root = (NbtCompound)snap.Root.Clone();
            rootName = snap.RootName;
            return true;
        }
    }

    /* ================================================================== */
    /* 编辑操作                                                            */
    /* ================================================================== */

    internal static class NbtEditOps
    {
        /// <summary>统计节点数（界面显示 + 决定要不要开撤销）</summary>
        public static int CountNodes(NbtTag root)
        {
            int n = 0;
            foreach (var spot in NbtWalk.All(root)) n++;
            return n;
        }

        /// <summary>重名时自动加后缀（_2、_3…），NBT 里允许重名但游戏读取会互相覆盖</summary>
        public static string UniqueName(NbtCompound parent, string want)
        {
            if (want == null) want = "";
            if (parent == null || !parent.Has(want)) return want;

            for (int i = 2; i < 10000; i++)
            {
                string candidate = want + "_" + i.ToString(CultureInfo.InvariantCulture);
                if (!parent.Has(candidate)) return candidate;
            }
            return want + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        public static bool MoveUp(NbtSpot spot)
        {
            if (spot == null || spot.Index <= 0) return false;

            if (spot.ParentCompound != null)
            {
                var list = spot.ParentCompound.Children;
                var tmp = list[spot.Index - 1];
                list[spot.Index - 1] = list[spot.Index];
                list[spot.Index] = tmp;
                spot.Index--;
                return true;
            }
            if (spot.ParentList != null)
            {
                var list = spot.ParentList.Items;
                var tmp = list[spot.Index - 1];
                list[spot.Index - 1] = list[spot.Index];
                list[spot.Index] = tmp;
                spot.Index--;
                return true;
            }
            return false;
        }

        public static bool MoveDown(NbtSpot spot)
        {
            if (spot == null) return false;

            if (spot.ParentCompound != null)
            {
                var list = spot.ParentCompound.Children;
                if (spot.Index >= list.Count - 1) return false;
                var tmp = list[spot.Index + 1];
                list[spot.Index + 1] = list[spot.Index];
                list[spot.Index] = tmp;
                spot.Index++;
                return true;
            }
            if (spot.ParentList != null)
            {
                var list = spot.ParentList.Items;
                if (spot.Index >= list.Count - 1) return false;
                var tmp = list[spot.Index + 1];
                list[spot.Index + 1] = list[spot.Index];
                list[spot.Index] = tmp;
                spot.Index++;
                return true;
            }
            return false;
        }

        /// <summary>能不能粘贴到目标上（NBTExplorer 也是按容器类型分别判断）</summary>
        public static bool CanPaste(NbtTag target, NbtTag clip, out string reason)
        {
            reason = null;
            if (clip == null) { reason = "剪贴板里没有 NBT 标签。"; return false; }

            var compound = target as NbtCompound;
            if (compound != null) return true;

            var list = target as NbtList;
            if (list != null)
            {
                if (list.ElementType != NbtType.End && list.ElementType != clip.Type)
                {
                    reason = "这个列表的元素类型是 " + NbtTypeInfo.TagName(list.ElementType) +
                             "，粘不进 " + NbtTypeInfo.TagName(clip.Type) + "。";
                    return false;
                }
                if (clip.Type == NbtType.End) { reason = "不能粘贴 TAG_End。"; return false; }
                return true;
            }

            reason = "只能粘到 TAG_Compound 或 TAG_List 里。";
            return false;
        }

        /// <summary>粘贴。返回新建的那个标签（调用方负责选中它）</summary>
        public static NbtTag Paste(NbtTag target, NbtTag clip)
        {
            if (target == null || clip == null) return null;
            var copy = clip.Clone();

            var compound = target as NbtCompound;
            if (compound != null)
            {
                copy.Name = UniqueName(compound, copy.Name);
                compound.Add(copy);
                return copy;
            }

            var list = target as NbtList;
            if (list != null)
            {
                copy.Name = "";
                list.Add(copy);
                return copy;
            }
            return null;
        }

        /// <summary>
        /// 把一段 SNBT / 文本值套用到标签上。规则：
        /// 纯数值文本改当前标量；复合标签或列表则整棵替换（只有类型对得上才允许）。
        /// </summary>
        public static void ApplyText(NbtTag tag, string text)
        {
            if (tag == null) return;
            tag.SetFromText(text);
        }
    }
}
