using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* NBT 标签类型                                                        */
    /* ================================================================== */

    /// <summary>
    /// NBT 标签类型。数值与 Minecraft 官方的 tag id 一致，
    /// 13（TAG_Short_Array）是 Substrate / NBTExplorer 沿用的非官方扩展，
    /// 少数 mod 与旧存档里会出现，读到时不能当成未知类型报错。
    /// </summary>
    internal enum NbtType
    {
        End = 0,
        Byte = 1,
        Short = 2,
        Int = 3,
        Long = 4,
        Float = 5,
        Double = 6,
        ByteArray = 7,
        String = 8,
        List = 9,
        Compound = 10,
        IntArray = 11,
        LongArray = 12,
        ShortArray = 13,
    }

    internal static class NbtTypeInfo
    {
        private static readonly string[] TagNames =
        {
            "TAG_End", "TAG_Byte", "TAG_Short", "TAG_Int", "TAG_Long",
            "TAG_Float", "TAG_Double", "TAG_Byte_Array", "TAG_String",
            "TAG_List", "TAG_Compound", "TAG_Int_Array", "TAG_Long_Array",
            "TAG_Short_Array",
        };

        private static readonly string[] ShortNames =
        {
            "End", "Byte", "Short", "Int", "Long", "Float", "Double",
            "ByteArray", "String", "List", "Compound", "IntArray", "LongArray",
            "ShortArray",
        };

        /// <summary>下拉框里能选的类型顺序（TAG_End 不属于可新建的类型）</summary>
        public static readonly NbtType[] Creatable =
        {
            NbtType.Byte, NbtType.Short, NbtType.Int, NbtType.Long,
            NbtType.Float, NbtType.Double, NbtType.String,
            NbtType.ByteArray, NbtType.IntArray, NbtType.LongArray, NbtType.ShortArray,
            NbtType.List, NbtType.Compound,
        };

        public static string TagName(NbtType t)
        {
            int i = (int)t;
            return (i >= 0 && i < TagNames.Length) ? TagNames[i] : ("TAG_?" + i);
        }

        public static string ShortName(NbtType t)
        {
            int i = (int)t;
            return (i >= 0 && i < ShortNames.Length) ? ShortNames[i] : ("?" + i);
        }

        /// <summary>给界面用的一行说明：TAG_Int（整数）</summary>
        public static string Describe(NbtType t)
        {
            switch (t)
            {
                case NbtType.Byte: return "TAG_Byte（字节）";
                case NbtType.Short: return "TAG_Short（短整数）";
                case NbtType.Int: return "TAG_Int（整数）";
                case NbtType.Long: return "TAG_Long（长整数）";
                case NbtType.Float: return "TAG_Float（单精度浮点）";
                case NbtType.Double: return "TAG_Double（双精度浮点）";
                case NbtType.String: return "TAG_String（字符串）";
                case NbtType.ByteArray: return "TAG_Byte_Array（字节数组）";
                case NbtType.IntArray: return "TAG_Int_Array（整数数组）";
                case NbtType.LongArray: return "TAG_Long_Array（长整数数组）";
                case NbtType.ShortArray: return "TAG_Short_Array（短整数数组，非官方）";
                case NbtType.List: return "TAG_List（列表）";
                case NbtType.Compound: return "TAG_Compound（复合标签）";
            }
            return TagName(t);
        }

        public static bool IsNumeric(NbtType t)
        {
            return t == NbtType.Byte || t == NbtType.Short || t == NbtType.Int ||
                   t == NbtType.Long || t == NbtType.Float || t == NbtType.Double;
        }

        public static bool IsArray(NbtType t)
        {
            return t == NbtType.ByteArray || t == NbtType.IntArray ||
                   t == NbtType.LongArray || t == NbtType.ShortArray;
        }

        public static bool IsContainer(NbtType t)
        {
            return t == NbtType.List || t == NbtType.Compound;
        }

        /// <summary>新建一个该类型的空标签</summary>
        public static NbtTag CreateEmpty(NbtType t, string name)
        {
            switch (t)
            {
                case NbtType.Byte: return new NbtByte(name, 0);
                case NbtType.Short: return new NbtShort(name, 0);
                case NbtType.Int: return new NbtInt(name, 0);
                case NbtType.Long: return new NbtLong(name, 0);
                case NbtType.Float: return new NbtFloat(name, 0f);
                case NbtType.Double: return new NbtDouble(name, 0d);
                case NbtType.String: return new NbtString(name, "");
                case NbtType.ByteArray: return new NbtByteArray(name, new byte[0]);
                case NbtType.IntArray: return new NbtIntArray(name, new int[0]);
                case NbtType.LongArray: return new NbtLongArray(name, new long[0]);
                case NbtType.ShortArray: return new NbtShortArray(name, new short[0]);
                case NbtType.List: return new NbtList(name);
                case NbtType.Compound: return new NbtCompound(name);
            }
            throw new ArgumentException("不能新建类型 " + TagName(t));
        }
    }

    /* ================================================================== */
    /* 标签基类                                                            */
    /* ================================================================== */

    internal abstract class NbtTag
    {
        /// <summary>标签名。列表里的元素没有自己的名字（用序号显示）</summary>
        public string Name = "";

        public abstract NbtType Type { get; }

        public string TypeName { get { return NbtTypeInfo.TagName(Type); } }

        /// <summary>深拷贝（撤销快照、复制粘贴都靠它）</summary>
        public abstract NbtTag Clone();

        /// <summary>树里「值」那一列显示的文字</summary>
        public abstract string ValueText();

        /// <summary>值的完整文字（编辑框里显示的原文）</summary>
        public virtual string EditText() { return ValueText(); }

        /// <summary>把编辑框里的文字写回标签；不合法时抛 FormatException</summary>
        public abstract void SetFromText(string text);

        /// <summary>值是否与另一个标签相等（容器比较结构）</summary>
        public virtual bool ValueEquals(NbtTag other) { return false; }

        public virtual bool HasChildren { get { return false; } }
        public virtual int ChildCount { get { return 0; } }

        /// <summary>复制粘帖时的类型兼容判断</summary>
        public bool SameType(NbtTag other)
        {
            return other != null && other.Type == Type;
        }

        public override string ToString()
        {
            return NbtTypeInfo.TagName(Type) + " \"" + Name + "\" = " + ValueText();
        }
    }

    /* ---------------------------- 标量 ---------------------------- */

    internal sealed class NbtByte : NbtTag
    {
        public sbyte Value;

        public NbtByte(string name, sbyte value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Byte; } }
        public override NbtTag Clone() { return new NbtByte(Name, Value); }
        public override string ValueText() { return Value.ToString(CultureInfo.InvariantCulture); }
        public override void SetFromText(string text)
        {
            Value = (sbyte)NbtText.ParseLong(text, sbyte.MinValue, sbyte.MaxValue, "字节值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtByte && ((NbtByte)other).Value == Value;
        }
    }

    internal sealed class NbtShort : NbtTag
    {
        public short Value;

        public NbtShort(string name, short value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Short; } }
        public override NbtTag Clone() { return new NbtShort(Name, Value); }
        public override string ValueText() { return Value.ToString(CultureInfo.InvariantCulture); }
        public override void SetFromText(string text)
        {
            Value = (short)NbtText.ParseLong(text, short.MinValue, short.MaxValue, "短整数值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtShort && ((NbtShort)other).Value == Value;
        }
    }

    internal sealed class NbtInt : NbtTag
    {
        public int Value;

        public NbtInt(string name, int value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Int; } }
        public override NbtTag Clone() { return new NbtInt(Name, Value); }
        public override string ValueText() { return Value.ToString(CultureInfo.InvariantCulture); }
        public override void SetFromText(string text)
        {
            Value = (int)NbtText.ParseLong(text, int.MinValue, int.MaxValue, "整数值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtInt && ((NbtInt)other).Value == Value;
        }
    }

    internal sealed class NbtLong : NbtTag
    {
        public long Value;

        public NbtLong(string name, long value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Long; } }
        public override NbtTag Clone() { return new NbtLong(Name, Value); }
        public override string ValueText() { return Value.ToString(CultureInfo.InvariantCulture); }
        public override void SetFromText(string text)
        {
            Value = NbtText.ParseLong(text, long.MinValue, long.MaxValue, "长整数值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtLong && ((NbtLong)other).Value == Value;
        }
    }

    internal sealed class NbtFloat : NbtTag
    {
        public float Value;

        public NbtFloat(string name, float value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Float; } }
        public override NbtTag Clone() { return new NbtFloat(Name, Value); }
        public override string ValueText() { return NbtText.Num(Value); }
        public override void SetFromText(string text)
        {
            Value = (float)NbtText.ParseDouble(text, "单精度浮点值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtFloat && ((NbtFloat)other).Value.Equals(Value);
        }
    }

    internal sealed class NbtDouble : NbtTag
    {
        public double Value;

        public NbtDouble(string name, double value) { Name = name ?? ""; Value = value; }

        public override NbtType Type { get { return NbtType.Double; } }
        public override NbtTag Clone() { return new NbtDouble(Name, Value); }
        public override string ValueText() { return NbtText.Num(Value); }
        public override void SetFromText(string text)
        {
            Value = NbtText.ParseDouble(text, "双精度浮点值");
        }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtDouble && ((NbtDouble)other).Value.Equals(Value);
        }
    }

    internal sealed class NbtString : NbtTag
    {
        public string Value;

        public NbtString(string name, string value) { Name = name ?? ""; Value = value ?? ""; }

        public override NbtType Type { get { return NbtType.String; } }
        public override NbtTag Clone() { return new NbtString(Name, Value); }
        /// <summary>换行会撑破树的一行，按 NBTExplorer 的做法换成 ¶ 显示</summary>
        public override string ValueText() { return Value.Replace('\n', '\u00B6').Replace('\r', '\u00B6'); }
        public override string EditText() { return Value; }
        public override void SetFromText(string text) { Value = text ?? ""; }
        public override bool ValueEquals(NbtTag other)
        {
            return other is NbtString && ((NbtString)other).Value == Value;
        }
    }

    /* ---------------------------- 数组 ---------------------------- */

    internal sealed class NbtByteArray : NbtTag
    {
        public byte[] Value;

        public NbtByteArray(string name, byte[] value) { Name = name ?? ""; Value = value ?? new byte[0]; }

        public override NbtType Type { get { return NbtType.ByteArray; } }
        public override NbtTag Clone() { return new NbtByteArray(Name, (byte[])Value.Clone()); }
        public override string ValueText() { return Value.Length + " 字节"; }
        public override string EditText() { return NbtText.JoinBytes(Value); }
        public override void SetFromText(string text) { Value = NbtText.ParseBytes(text, "字节数组"); }
        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtByteArray;
            if (o == null || o.Value.Length != Value.Length) return false;
            for (int i = 0; i < Value.Length; i++) if (o.Value[i] != Value[i]) return false;
            return true;
        }
    }

    internal sealed class NbtShortArray : NbtTag
    {
        public short[] Value;

        public NbtShortArray(string name, short[] value) { Name = name ?? ""; Value = value ?? new short[0]; }

        public override NbtType Type { get { return NbtType.ShortArray; } }
        public override NbtTag Clone() { return new NbtShortArray(Name, (short[])Value.Clone()); }
        public override string ValueText() { return Value.Length + " 个短整数"; }
        public override string EditText() { return NbtText.JoinShorts(Value); }
        public override void SetFromText(string text) { Value = NbtText.ParseShorts(text, "短整数数组"); }
        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtShortArray;
            if (o == null || o.Value.Length != Value.Length) return false;
            for (int i = 0; i < Value.Length; i++) if (o.Value[i] != Value[i]) return false;
            return true;
        }
    }

    internal sealed class NbtIntArray : NbtTag
    {
        public int[] Value;

        public NbtIntArray(string name, int[] value) { Name = name ?? ""; Value = value ?? new int[0]; }

        public override NbtType Type { get { return NbtType.IntArray; } }
        public override NbtTag Clone() { return new NbtIntArray(Name, (int[])Value.Clone()); }
        public override string ValueText() { return Value.Length + " 个整数"; }
        public override string EditText() { return NbtText.JoinInts(Value); }
        public override void SetFromText(string text) { Value = NbtText.ParseInts(text, "整数数组"); }
        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtIntArray;
            if (o == null || o.Value.Length != Value.Length) return false;
            for (int i = 0; i < Value.Length; i++) if (o.Value[i] != Value[i]) return false;
            return true;
        }
    }

    internal sealed class NbtLongArray : NbtTag
    {
        public long[] Value;

        public NbtLongArray(string name, long[] value) { Name = name ?? ""; Value = value ?? new long[0]; }

        public override NbtType Type { get { return NbtType.LongArray; } }
        public override NbtTag Clone() { return new NbtLongArray(Name, (long[])Value.Clone()); }
        public override string ValueText() { return Value.Length + " 个长整数"; }
        public override string EditText() { return NbtText.JoinLongs(Value); }
        public override void SetFromText(string text) { Value = NbtText.ParseLongs(text, "长整数数组"); }
        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtLongArray;
            if (o == null || o.Value.Length != Value.Length) return false;
            for (int i = 0; i < Value.Length; i++) if (o.Value[i] != Value[i]) return false;
            return true;
        }
    }

    /* ---------------------------- 容器 ---------------------------- */

    internal sealed class NbtList : NbtTag
    {
        /// <summary>元素类型。空列表按 NBT 规范记为 TAG_End</summary>
        public NbtType ElementType = NbtType.End;

        private readonly List<NbtTag> _items = new List<NbtTag>();

        public NbtList(string name) { Name = name ?? ""; }

        public NbtList(string name, NbtType elementType)
        {
            Name = name ?? "";
            ElementType = elementType;
        }

        public List<NbtTag> Items { get { return _items; } }

        public override NbtType Type { get { return NbtType.List; } }

        public override NbtTag Clone()
        {
            var copy = new NbtList(Name, ElementType);
            foreach (var item in _items) copy._items.Add(item.Clone());
            return copy;
        }

        public override string ValueText()
        {
            return _items.Count + " 项（元素 " + NbtTypeInfo.ShortName(ElementType) + "）";
        }

        public override void SetFromText(string text)
        {
            throw new FormatException("列表不能直接改值，请用「添加子标签」逐个添加。");
        }

        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtList;
            if (o == null || o.ElementType != ElementType || o._items.Count != _items.Count) return false;
            for (int i = 0; i < _items.Count; i++)
                if (!_items[i].ValueEquals(o._items[i])) return false;
            return true;
        }

        public override bool HasChildren { get { return _items.Count > 0; } }
        public override int ChildCount { get { return _items.Count; } }

        /// <summary>
        /// 加一个元素。空列表会跟着元素的类型定型（NBT 规范里空列表的类型是 TAG_End，
        /// 第一次添加时才真正确定），类型不同则拒绝。
        /// </summary>
        public void Add(NbtTag tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (ElementType == NbtType.End)
            {
                if (tag.Type == NbtType.End) throw new FormatException("不能往列表里加 TAG_End。");
                ElementType = tag.Type;
            }
            else if (tag.Type != ElementType)
            {
                throw new FormatException(
                    "这个列表的元素类型是 " + NbtTypeInfo.TagName(ElementType) +
                    "，不能加入 " + NbtTypeInfo.TagName(tag.Type) + "。");
            }
            _items.Add(tag);
        }

        public void Insert(int index, NbtTag tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (ElementType == NbtType.End) ElementType = tag.Type;
            else if (tag.Type != ElementType)
                throw new FormatException(
                    "这个列表的元素类型是 " + NbtTypeInfo.TagName(ElementType) +
                    "，不能加入 " + NbtTypeInfo.TagName(tag.Type) + "。");
            _items.Insert(index, tag);
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            if (_items.Count == 0) ElementType = NbtType.End;
        }

        /// <summary>换掉某个元素的类型（编辑器里改类型时用）</summary>
        public void SetElementType(NbtType type)
        {
            if (_items.Count > 0) throw new InvalidOperationException("列表里已经有元素，不能改元素类型。");
            ElementType = type;
        }
    }

    internal sealed class NbtCompound : NbtTag
    {
        private readonly List<NbtTag> _children = new List<NbtTag>();

        public NbtCompound(string name) { Name = name ?? ""; }

        public NbtCompound() { Name = ""; }

        public List<NbtTag> Children { get { return _children; } }

        public override NbtType Type { get { return NbtType.Compound; } }

        public override NbtTag Clone()
        {
            var copy = new NbtCompound(Name);
            foreach (var c in _children) copy._children.Add(c.Clone());
            return copy;
        }

        public override string ValueText() { return _children.Count + " 项"; }

        public override void SetFromText(string text)
        {
            throw new FormatException("复合标签不能直接改值，请编辑它的子标签。");
        }

        public override bool ValueEquals(NbtTag other)
        {
            var o = other as NbtCompound;
            if (o == null || o._children.Count != _children.Count) return false;
            for (int i = 0; i < _children.Count; i++)
            {
                if (_children[i].Name != o._children[i].Name) return false;
                if (!_children[i].ValueEquals(o._children[i])) return false;
            }
            return true;
        }

        public override bool HasChildren { get { return _children.Count > 0; } }
        public override int ChildCount { get { return _children.Count; } }

        public int IndexOf(string name)
        {
            for (int i = 0; i < _children.Count; i++)
                if (_children[i].Name == name) return i;
            return -1;
        }

        public NbtTag Get(string name)
        {
            int i = IndexOf(name);
            return i < 0 ? null : _children[i];
        }

        public bool Has(string name) { return IndexOf(name) >= 0; }

        public void Add(NbtTag tag) { _children.Add(tag); }
        public void Insert(int index, NbtTag tag) { _children.Insert(index, tag); }
        public void RemoveAt(int index) { _children.RemoveAt(index); }
        public void Remove(NbtTag tag) { _children.Remove(tag); }

        /// <summary>重命名时避免同名（同名在 NBT 里是合法的，但游戏读取时后者会覆盖前者）</summary>
        public bool HasOther(string name, NbtTag self)
        {
            foreach (var c in _children)
                if (!ReferenceEquals(c, self) && c.Name == name) return true;
            return false;
        }
    }

    /* ================================================================== */
    /* 遍历                                                                */
    /* ================================================================== */

    /// <summary>树里的一处位置：标签 + 父容器 + 在父容器里的序号</summary>
    internal sealed class NbtSpot
    {
        public NbtTag Tag;
        public NbtCompound ParentCompound;   // 二者只有一个非空
        public NbtList ParentList;
        public int Index;
        public string Path = "";

        public NbtTag ParentTag
        {
            get { return ParentCompound != null ? (NbtTag)ParentCompound : (NbtTag)ParentList; }
        }

        public bool IsRoot { get { return ParentCompound == null && ParentList == null; } }

        /// <summary>从容器里摘掉自己</summary>
        public void Detach()
        {
            if (ParentCompound != null) ParentCompound.RemoveAt(Index);
            else if (ParentList != null) ParentList.RemoveAt(Index);
        }

        /// <summary>把自己放回容器（撤销用）</summary>
        public void Attach()
        {
            if (ParentCompound != null) ParentCompound.Insert(Math.Min(Index, ParentCompound.Children.Count), Tag);
            else if (ParentList != null) ParentList.Insert(Math.Min(Index, ParentList.Items.Count), Tag);
        }
    }

    internal static class NbtWalk
    {
        /// <summary>先序遍历整棵树（含 root 自己）</summary>
        public static IEnumerable<NbtSpot> All(NbtTag root)
        {
            if (root == null) yield break;

            var stack = new Stack<NbtSpot>();
            stack.Push(new NbtSpot { Tag = root, Path = "/" + root.Name });

            while (stack.Count > 0)
            {
                var spot = stack.Pop();
                yield return spot;

                var compound = spot.Tag as NbtCompound;
                if (compound != null)
                {
                    /* 倒着压栈，保证弹出顺序与树里的顺序一致 */
                    for (int i = compound.Children.Count - 1; i >= 0; i--)
                    {
                        var child = compound.Children[i];
                        stack.Push(new NbtSpot
                        {
                            Tag = child,
                            ParentCompound = compound,
                            Index = i,
                            Path = spot.Path + "/" + child.Name,
                        });
                    }
                    continue;
                }

                var list = spot.Tag as NbtList;
                if (list != null)
                {
                    for (int i = list.Items.Count - 1; i >= 0; i--)
                    {
                        stack.Push(new NbtSpot
                        {
                            Tag = list.Items[i],
                            ParentList = list,
                            Index = i,
                            Path = spot.Path + "[" + i + "]",
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 某个标签的路径（从根一路写下来）。复合标签的子项写成 /名字，
        /// 列表元素写成 [序号]，和 NBTExplorer 的路径写法一致。
        /// </summary>
        public static string PathOf(NbtTag root, NbtTag target)
        {
            if (root == null || target == null) return "";
            var sb = new StringBuilder();
            if (FindPath(root, target, sb, false))
            {
                string path = sb.ToString();
                /* 根自己（或根没有名字时）兜底成 "/"，别返回空串 */
                return path.Length == 0 ? "/" : path;
            }
            return "";
        }

        private static bool FindPath(NbtTag node, NbtTag target, StringBuilder sb, bool listElement)
        {
            int mark = sb.Length;
            if (listElement)
            {
                /* 列表元素的分段（[序号]）已由父级写好 */
            }
            else if (node.Name.Length > 0)
            {
                sb.Append('/').Append(node.Name);
            }

            if (ReferenceEquals(node, target)) return true;

            var compound = node as NbtCompound;
            if (compound != null)
            {
                foreach (var child in compound.Children)
                    if (FindPath(child, target, sb, false)) return true;
            }

            var list = node as NbtList;
            if (list != null)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    sb.Append('[').Append(i.ToString(CultureInfo.InvariantCulture)).Append(']');
                    if (FindPath(list.Items[i], target, sb, true)) return true;
                    sb.Length -= (i.ToString(CultureInfo.InvariantCulture).Length + 2);
                }
            }

            sb.Length = mark;
            return false;
        }
    }

    /* ================================================================== */
    /* 文字与数字转换                                                       */
    /* ================================================================== */

    internal static class NbtText
    {
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            /* 整数值不写成 1.0，游戏里看着别扭 */
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        public static long ParseLong(string text, long min, long max, string what)
        {
            if (text == null) throw new FormatException(what + "不能为空。");
            string s = text.Trim();
            if (s.Length == 0) throw new FormatException(what + "不能为空。");

            bool neg = false;
            if (s.StartsWith("-", StringComparison.Ordinal)) { neg = true; s = s.Substring(1); }
            else if (s.StartsWith("+", StringComparison.Ordinal)) { s = s.Substring(1); }

            /* 去掉类型后缀：3b / 3s / 3L */
            if (s.Length > 1)
            {
                char last = s[s.Length - 1];
                if (last == 'b' || last == 'B' || last == 's' || last == 'S' || last == 'l' || last == 'L')
                    s = s.Substring(0, s.Length - 1);
            }

            long value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong u;
                if (!ulong.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u))
                    throw new FormatException("「" + text + "」不是合法的" + what + "。");
                value = unchecked((long)u);
                if (neg) value = unchecked(-value);
            }
            else
            {
                if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    /* long.MinValue 取绝对值后 long 装不下，这里按无符号再试一次 */
                    ulong mag;
                    if (!neg || !ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out mag))
                        throw new FormatException("「" + text + "」不是合法的" + what + "。");
                    value = unchecked(-(long)mag);
                }
                else if (neg) value = -value;
            }

            if (value < min || value > max)
                throw new FormatException("「" + text + "」超出" + what + "的范围（" + min + " ~ " + max + "）。");
            return value;
        }

        public static double ParseDouble(string text, string what)
        {
            if (text == null) throw new FormatException(what + "不能为空。");
            string s = text.Trim();
            if (s.Length == 0) throw new FormatException(what + "不能为空。");

            /* 去掉类型后缀：3.5f / 3.5d */
            if (s.Length > 1)
            {
                char last = s[s.Length - 1];
                if (last == 'f' || last == 'F' || last == 'd' || last == 'D') s = s.Substring(0, s.Length - 1);
            }

            if (string.Equals(s, "NaN", StringComparison.OrdinalIgnoreCase)) return double.NaN;
            if (string.Equals(s, "Infinity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "+Infinity", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
            if (string.Equals(s, "-Infinity", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;

            double value;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("「" + text + "」不是合法的" + what + "。");
            return value;
        }

        /* ---------------- 数组文字 ---------------- */

        private static string[] SplitNumbers(string text)
        {
            if (text == null) return new string[0];
            return text.Split(new[] { ',', ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static byte[] ParseBytes(string text, string what)
        {
            string[] parts = SplitNumbers(text);
            var result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                long v = ParseLong(parts[i], -128, 255, what + "第 " + (i + 1) + " 项");
                result[i] = unchecked((byte)v);
            }
            return result;
        }

        public static short[] ParseShorts(string text, string what)
        {
            string[] parts = SplitNumbers(text);
            var result = new short[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = (short)ParseLong(parts[i], short.MinValue, short.MaxValue, what + "第 " + (i + 1) + " 项");
            return result;
        }

        public static int[] ParseInts(string text, string what)
        {
            string[] parts = SplitNumbers(text);
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = (int)ParseLong(parts[i], int.MinValue, int.MaxValue, what + "第 " + (i + 1) + " 项");
            return result;
        }

        public static long[] ParseLongs(string text, string what)
        {
            string[] parts = SplitNumbers(text);
            var result = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = ParseLong(parts[i], long.MinValue, long.MaxValue, what + "第 " + (i + 1) + " 项");
            return result;
        }

        public static string JoinBytes(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(data[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string JoinShorts(short[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(data[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string JoinInts(int[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(data[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string JoinLongs(long[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(data[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /* ---------------- 文本转义 ---------------- */

        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Unescape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }

                i++;
                char n = s[i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                                break;
                            }
                        }
                        sb.Append("\\u");
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
