using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* SNBT（Minecraft 的文本 NBT 写法）                                    */
    /* ================================================================== */

    /// <summary>
    /// SNBT 的读写。写法与游戏里 /data、/summon 用的那套一致：
    ///   {Name:"Steve",Health:20.0f,Inventory:[{id:"stone",Count:1b}]}
    /// 数组有专属前缀：[B;1,2] 字节、[I;1,2] 整数、[L;1L,2L] 长整数、[S;1s,2s] 短整数。
    /// 数值后缀：b 字节、s 短整数、L 长整数、f 单精度、d 双精度；不带后缀按整数/双精度猜。
    /// </summary>
    internal static class NbtSnbt
    {
        /* ---------------------------- 输出 ---------------------------- */

        public static string Format(NbtTag tag)
        {
            var sb = new StringBuilder();
            WriteTag(sb, tag, -1, "");
            return sb.ToString();
        }

        public static string FormatPretty(NbtTag tag)
        {
            var sb = new StringBuilder();
            WriteTag(sb, tag, 0, "\n");
            return sb.ToString();
        }

        private static void WriteTag(StringBuilder sb, NbtTag tag, int depth, string newline)
        {
            if (tag == null) { sb.Append("null"); return; }

            var compound = tag as NbtCompound;
            if (compound != null) { WriteCompound(sb, compound, depth, newline); return; }

            var list = tag as NbtList;
            if (list != null) { WriteList(sb, list, depth, newline); return; }

            var bytes = tag as NbtByteArray;
            if (bytes != null) { WriteArray(sb, "B", bytes.Value.Length, i => bytes.Value[i].ToString(CultureInfo.InvariantCulture)); return; }

            var shorts = tag as NbtShortArray;
            if (shorts != null) { WriteArray(sb, "S", shorts.Value.Length, i => shorts.Value[i].ToString(CultureInfo.InvariantCulture) + "s"); return; }

            var ints = tag as NbtIntArray;
            if (ints != null) { WriteArray(sb, "I", ints.Value.Length, i => ints.Value[i].ToString(CultureInfo.InvariantCulture)); return; }

            var longs = tag as NbtLongArray;
            if (longs != null) { WriteArray(sb, "L", longs.Value.Length, i => longs.Value[i].ToString(CultureInfo.InvariantCulture) + "L"); return; }

            var str = tag as NbtString;
            if (str != null) { sb.Append('"').Append(NbtText.Escape(str.Value)).Append('"'); return; }

            var b = tag as NbtByte; if (b != null) { sb.Append(b.Value.ToString(CultureInfo.InvariantCulture)).Append('b'); return; }
            var s = tag as NbtShort; if (s != null) { sb.Append(s.Value.ToString(CultureInfo.InvariantCulture)).Append('s'); return; }
            var i2 = tag as NbtInt; if (i2 != null) { sb.Append(i2.Value.ToString(CultureInfo.InvariantCulture)); return; }
            var l2 = tag as NbtLong; if (l2 != null) { sb.Append(l2.Value.ToString(CultureInfo.InvariantCulture)).Append('L'); return; }
            var f = tag as NbtFloat; if (f != null) { sb.Append(Number(f.Value)).Append('f'); return; }
            var d = tag as NbtDouble; if (d != null) { sb.Append(Number(d.Value)).Append('d'); return; }

            sb.Append("null");
        }

        /// <summary>浮点要带小数点，否则再解析回来会变成整数类型</summary>
        private static string Number(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0 && s.IndexOf('e') < 0) s += ".0";
            return s;
        }

        private static void WriteArray(StringBuilder sb, string prefix, int count, Func<int, string> item)
        {
            sb.Append('[').Append(prefix).Append(';');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(item(i));
            }
            sb.Append(']');
        }

        private static void WriteCompound(StringBuilder sb, NbtCompound tag, int depth, string newline)
        {
            if (tag.Children.Count == 0) { sb.Append("{}"); return; }

            sb.Append('{');
            for (int i = 0; i < tag.Children.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, depth + 1, newline);
                sb.Append(QuoteKey(tag.Children[i].Name)).Append(':');
                WriteTag(sb, tag.Children[i], depth + 1, newline);
            }
            NewLine(sb, depth, newline);
            sb.Append('}');
        }

        private static void WriteList(StringBuilder sb, NbtList tag, int depth, string newline)
        {
            if (tag.Items.Count == 0) { sb.Append("[]"); return; }

            sb.Append('[');
            for (int i = 0; i < tag.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, depth + 1, newline);
                WriteTag(sb, tag.Items[i], depth + 1, newline);
            }
            NewLine(sb, depth, newline);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, int depth, string newline)
        {
            if (newline.Length == 0) return;
            sb.Append(newline);
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        /// <summary>键名只在必要时加引号，输出更接近游戏里的写法</summary>
        private static string QuoteKey(string key)
        {
            if (key == null) key = "";
            bool simple = key.Length > 0;
            foreach (char c in key)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '+')) { simple = false; break; }
            }
            return simple ? key : "\"" + NbtText.Escape(key) + "\"";
        }

        /* ---------------------------- 解析 ---------------------------- */

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s ?? ""; _i = 0; }

            public string RootName = "";

            private bool End { get { return _i >= _s.Length; } }
            private char Cur { get { return _i < _s.Length ? _s[_i] : '\0'; } }

            private void SkipWs()
            {
                while (!End && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private Exception Fail(string message)
            {
                return new FormatException(message + "（第 " + (_i + 1) + " 个字符附近）");
            }

            /// <summary>根：允许写成 名字 {...} 的形式，名字就是根标签名</summary>
            public NbtTag ParseRoot()
            {
                SkipWs();
                int save = _i;

                /* 先看是不是「裸名字 + 复合标签」 */
                if (!End && Cur != '{' && Cur != '[')
                {
                    string word = ReadBareWord();
                    SkipWs();
                    if (!End && Cur == '{')
                    {
                        RootName = word;
                    }
                    else
                    {
                        _i = save;   // 不是名字前缀，退回当普通值解析
                    }
                }

                NbtTag tag = ParseValue();
                SkipWs();
                if (!End) throw Fail("结尾有多余内容");
                return tag;
            }

            public NbtTag ParseValue()
            {
                SkipWs();
                if (End) throw Fail("内容不完整");

                char c = Cur;
                if (c == '{') return ParseCompound();
                if (c == '[') return ParseListOrArray();
                if (c == '"' || c == '\'') return new NbtString("", ParseQuoted());

                return ParseScalarOrBareString();
            }

            private NbtCompound ParseCompound()
            {
                var compound = new NbtCompound("");
                _i++;   // {

                while (true)
                {
                    SkipWs();
                    if (End) throw Fail("复合标签没有闭合（缺 }）");
                    if (Cur == '}') { _i++; break; }

                    string key;
                    if (Cur == '"' || Cur == '\'') key = ParseQuoted();
                    else key = ReadBareWord();

                    SkipWs();
                    if (End || Cur != ':') throw Fail("键「" + key + "」后面少了冒号");
                    _i++;

                    NbtTag value = ParseValue();
                    value.Name = key;
                    compound.Add(value);

                    SkipWs();
                    if (!End && Cur == ',') { _i++; continue; }
                    if (!End && Cur == '}') { _i++; break; }
                    throw Fail("复合标签里少了逗号或 }");
                }
                return compound;
            }

            private NbtTag ParseListOrArray()
            {
                /* 先判断数组前缀：[B; [I; [L; [S; */
                int save = _i;
                _i++;   // [
                SkipWs();
                if (!End && (Cur == 'B' || Cur == 'I' || Cur == 'L' || Cur == 'S' || Cur == 'b' || Cur == 'i' || Cur == 'l' || Cur == 's'))
                {
                    char kind = char.ToUpperInvariant(Cur);
                    if (_i + 1 < _s.Length && _s[_i + 1] == ';')
                    {
                        _i += 2;
                        return ParseArrayBody(kind);
                    }
                }
                _i = save;

                var list = new NbtList("");
                _i++;   // [
                while (true)
                {
                    SkipWs();
                    if (End) throw Fail("列表没有闭合（缺 ]）");
                    if (Cur == ']') { _i++; break; }

                    NbtTag item = ParseValue();
                    item.Name = "";
                    list.Add(item);

                    SkipWs();
                    if (!End && Cur == ',') { _i++; continue; }
                    if (!End && Cur == ']') { _i++; break; }
                    throw Fail("列表里少了逗号或 ]");
                }
                return list;
            }

            private NbtTag ParseArrayBody(char kind)
            {
                var items = new List<string>();
                while (true)
                {
                    SkipWs();
                    if (End) throw Fail("数组没有闭合（缺 ]）");
                    if (Cur == ']') { _i++; break; }

                    items.Add(ReadBareWord());

                    SkipWs();
                    if (!End && Cur == ',') { _i++; continue; }
                    if (!End && Cur == ']') { _i++; break; }
                    throw Fail("数组里少了逗号或 ]");
                }

                string[] parts = items.ToArray();
                switch (kind)
                {
                    case 'B':
                        {
                            var data = new byte[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                                data[i] = unchecked((byte)NbtText.ParseLong(parts[i], -128, 255, "字节数组第 " + (i + 1) + " 项"));
                            return new NbtByteArray("", data);
                        }
                    case 'S':
                        {
                            var data = new short[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                                data[i] = (short)NbtText.ParseLong(parts[i], short.MinValue, short.MaxValue, "短整数数组第 " + (i + 1) + " 项");
                            return new NbtShortArray("", data);
                        }
                    case 'I':
                        {
                            var data = new int[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                                data[i] = (int)NbtText.ParseLong(parts[i], int.MinValue, int.MaxValue, "整数数组第 " + (i + 1) + " 项");
                            return new NbtIntArray("", data);
                        }
                    default:
                        {
                            var data = new long[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                                data[i] = NbtText.ParseLong(parts[i], long.MinValue, long.MaxValue, "长整数数组第 " + (i + 1) + " 项");
                            return new NbtLongArray("", data);
                        }
                }
            }

            private string ParseQuoted()
            {
                char quote = Cur;
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Fail("字符串没有闭合");
                    char c = _s[_i];
                    if (c == '\\')
                    {
                        if (_i + 1 >= _s.Length) throw Fail("转义符后面没有内容");
                        sb.Append(c).Append(_s[_i + 1]);
                        _i += 2;
                        continue;
                    }
                    if (c == quote) { _i++; break; }
                    sb.Append(c);
                    _i++;
                }
                return NbtText.Unescape(sb.ToString());
            }

            /// <summary>读到分隔符为止（逗号、右括号、冒号、空白）</summary>
            private string ReadBareWord()
            {
                int start = _i;
                while (!End)
                {
                    char c = _s[_i];
                    if (c == ',' || c == '}' || c == ']' || c == ':' || c == '{' || c == '[' || char.IsWhiteSpace(c)) break;
                    _i++;
                }
                if (_i == start) throw Fail("这里应该有一个值");
                return _s.Substring(start, _i - start);
            }

            private NbtTag ParseScalarOrBareString()
            {
                string word = ReadBareWord();
                if (word.Length == 0) throw Fail("这里应该有一个值");

                char last = word[word.Length - 1];
                string body = word;
                char suffix = '\0';
                if (word.Length > 1 && (last == 'b' || last == 'B' || last == 's' || last == 'S' ||
                                        last == 'l' || last == 'L' || last == 'f' || last == 'F' ||
                                        last == 'd' || last == 'D'))
                {
                    suffix = char.ToLowerInvariant(last);
                    body = word.Substring(0, word.Length - 1);
                }

                if (suffix == 'b') return new NbtByte("", (sbyte)NbtText.ParseLong(body, sbyte.MinValue, sbyte.MaxValue, "字节值"));
                if (suffix == 's') return new NbtShort("", (short)NbtText.ParseLong(body, short.MinValue, short.MaxValue, "短整数值"));
                if (suffix == 'l') return new NbtLong("", NbtText.ParseLong(body, long.MinValue, long.MaxValue, "长整数值"));
                if (suffix == 'f') return new NbtFloat("", (float)NbtText.ParseDouble(body, "单精度浮点值"));
                if (suffix == 'd') return new NbtDouble("", NbtText.ParseDouble(body, "双精度浮点值"));

                /* 没后缀：先按整数，再按浮点，都不行就当字符串（SNBT 里裸词就是字符串） */
                long lv;
                if (long.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out lv))
                {
                    if (lv >= int.MinValue && lv <= int.MaxValue) return new NbtInt("", (int)lv);
                    return new NbtLong("", lv);
                }

                double dv;
                if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                    return new NbtDouble("", dv);

                return new NbtString("", word);
            }
        }

        public static bool TryParse(string text, out NbtTag tag, out string error)
        {
            tag = null;
            error = null;
            if (text == null || text.Trim().Length == 0)
            {
                error = "内容是空的。";
                return false;
            }

            try
            {
                var parser = new Parser(text);
                tag = parser.ParseRoot();
                /* 「Level {...}」这种写法里，名字是解析器认出来的，要挂回标签上，
                   否则 TryParseRoot 拿不到根名，存文件时根名就丢了 */
                if (parser.RootName.Length > 0) tag.Name = parser.RootName;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>解析并把结果整理成「根复合标签 + 根名字」，方便直接当文件存</summary>
        public static bool TryParseRoot(string text, out NbtCompound root, out string rootName, out string error)
        {
            root = null;
            rootName = "";
            error = null;

            NbtTag tag;
            if (!TryParse(text, out tag, out error)) return false;

            var compound = tag as NbtCompound;
            if (compound != null)
            {
                root = compound;
                rootName = compound.Name ?? "";
                return true;
            }

            /* 不是复合标签就包一层，和读文件时的处理保持一致 */
            rootName = tag.Name ?? "";
            var wrapper = new NbtCompound("");
            tag.Name = rootName.Length > 0 ? rootName : "root";
            wrapper.Add(tag);
            root = wrapper;
            return true;
        }
    }

    /* ================================================================== */
    /* JSON 导出                                                           */
    /* ================================================================== */

    /// <summary>
    /// NBT → JSON（对应 NBTExplorer 里 NBTUtil 的 --print-json）。
    /// 只做导出：数值直接写数字，数组写成 JSON 数组，复合标签写成对象。
    /// 大的 long 值超过 2^53 会丢精度，导出时在界面上会说明这一点。
    /// </summary>
    internal static class NbtJson
    {
        public static JsonValue ToJson(NbtTag tag)
        {
            var compound = tag as NbtCompound;
            if (compound != null)
            {
                var obj = JsonValue.Obj();
                foreach (var child in compound.Children) obj.Set(child.Name, ToJson(child));
                return obj;
            }

            var list = tag as NbtList;
            if (list != null)
            {
                var arr = JsonValue.Arr();
                foreach (var item in list.Items) arr.Items.Add(ToJson(item));
                return arr;
            }

            var bytes = tag as NbtByteArray;
            if (bytes != null) { var a = JsonValue.Arr(); foreach (byte v in bytes.Value) a.Items.Add(JsonValue.Num(v)); return a; }

            var shorts = tag as NbtShortArray;
            if (shorts != null) { var a = JsonValue.Arr(); foreach (short v in shorts.Value) a.Items.Add(JsonValue.Num(v)); return a; }

            var ints = tag as NbtIntArray;
            if (ints != null) { var a = JsonValue.Arr(); foreach (int v in ints.Value) a.Items.Add(JsonValue.Num(v)); return a; }

            var longs = tag as NbtLongArray;
            if (longs != null) { var a = JsonValue.Arr(); foreach (long v in longs.Value) a.Items.Add(JsonValue.Num(v)); return a; }

            var str = tag as NbtString;
            if (str != null) return JsonValue.Str(str.Value);

            var b = tag as NbtByte; if (b != null) return JsonValue.Num(b.Value);
            var s = tag as NbtShort; if (s != null) return JsonValue.Num(s.Value);
            var i = tag as NbtInt; if (i != null) return JsonValue.Num(i.Value);
            var l = tag as NbtLong; if (l != null) return JsonValue.Num(l.Value);
            var f = tag as NbtFloat; if (f != null) return JsonValue.Num(f.Value);
            var d = tag as NbtDouble; if (d != null) return JsonValue.Num(d.Value);

            return JsonValue.Null();
        }

        public static string Write(NbtTag tag, bool pretty)
        {
            return Json.Write(ToJson(tag), pretty);
        }
    }
}
