using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// 极简 JSON 实现（解析 + 保序输出）。
    /// .NET Framework 4.8 自带的是 JavaScriptSerializer，会丢键顺序且对
    /// pack.mcmeta 的宽松写法支持不好，所以自己实现一份。
    /// 支持完整 JSON 语法：对象 / 数组 / 字符串（含 \uXXXX）/ 数字 / true / false / null。
    /// </summary>
    internal enum JsonKind { Null, Bool, Number, String, Array, Object }

    internal sealed class JsonValue
    {
        public JsonKind Kind;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonValue> Items;                       // Array
        public List<KeyValuePair<string, JsonValue>> Members; // Object（保序）

        public static JsonValue Null() { return new JsonValue { Kind = JsonKind.Null }; }
        public static JsonValue Bool(bool v) { return new JsonValue { Kind = JsonKind.Bool, BoolValue = v }; }
        public static JsonValue Num(double v) { return new JsonValue { Kind = JsonKind.Number, NumberValue = v }; }
        public static JsonValue Str(string v) { return new JsonValue { Kind = JsonKind.String, StringValue = v }; }
        public static JsonValue Arr() { return new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>() }; }
        public static JsonValue Obj() { return new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>() }; }

        public bool IsObject { get { return Kind == JsonKind.Object; } }
        public bool IsArray { get { return Kind == JsonKind.Array; } }
        public bool IsString { get { return Kind == JsonKind.String; } }
        public bool IsNumber { get { return Kind == JsonKind.Number; } }

        /* ---------------- 对象访问 ---------------- */

        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            foreach (var kv in Members)
            {
                if (kv.Key == key) return kv.Value;
            }
            return null;
        }

        public bool Has(string key)
        {
            return Get(key) != null;
        }

        /// <summary>写入/覆盖一个成员，新增时追加到末尾（保持原有顺序）</summary>
        public void Set(string key, JsonValue value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("不是 JSON 对象");
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Key == key)
                {
                    Members[i] = new KeyValuePair<string, JsonValue>(key, value);
                    return;
                }
            }
            Members.Add(new KeyValuePair<string, JsonValue>(key, value));
        }

        public bool Remove(string key)
        {
            if (Kind != JsonKind.Object) return false;
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Key == key)
                {
                    Members.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public IEnumerable<string> Keys()
        {
            if (Kind != JsonKind.Object) yield break;
            foreach (var kv in Members) yield return kv.Key;
        }

        /// <summary>取整数（兼容 34 / 34.0 / "34" 这类写法）</summary>
        public int? AsInt()
        {
            if (Kind == JsonKind.Number) return (int)Math.Round(NumberValue);
            if (Kind == JsonKind.String)
            {
                int v;
                if (int.TryParse(StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            }
            return null;
        }

        /// <summary>
        /// 把任意 JSON 值转成可读文本。用于处理 pack_format 的
        /// 对象形式 {"min_inclusive":..,"max_inclusive":..} 和数组形式。
        /// </summary>
        public string AsText()
        {
            switch (Kind)
            {
                case JsonKind.String: return StringValue;
                case JsonKind.Number: return NumberValue.ToString(CultureInfo.InvariantCulture);
                case JsonKind.Bool: return BoolValue ? "true" : "false";
                case JsonKind.Null: return "";
                default: return Json.Write(this, false);
            }
        }
    }

    internal static class Json
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new JsonException("内容为空");
            var p = new Parser(text);
            p.SkipWhitespace();
            JsonValue v = p.ParseValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw new JsonException("第 " + p.Line + " 行：JSON 结束后还有多余内容");
            return v;
        }

        /// <summary>尝试解析，失败时通过 error 返回原因而不是抛异常</summary>
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonException e)
            {
                value = null;
                error = e.Message;
                return false;
            }
            catch (Exception e)
            {
                value = null;
                error = e.Message;
                return false;
            }
        }

        public static string Write(JsonValue v, bool pretty)
        {
            var sb = new StringBuilder(1024);
            WriteValue(sb, v, pretty, 0);
            return sb.ToString();
        }

        private const int MaxDepth = 200;

        private static void WriteValue(StringBuilder sb, JsonValue v, bool pretty, int depth)
        {
            if (depth > MaxDepth) throw new JsonException("JSON 嵌套层级过深");
            if (v == null) { sb.Append("null"); return; }

            switch (v.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(v.BoolValue ? "true" : "false");
                    break;
                case JsonKind.Number:
                    sb.Append(FormatNumber(v.NumberValue));
                    break;
                case JsonKind.String:
                    WriteString(sb, v.StringValue);
                    break;
                case JsonKind.Array:
                    if (v.Items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty)
                        {
                            sb.Append('\n');
                            Indent(sb, depth + 1);
                        }
                        WriteValue(sb, v.Items[i], pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (v.Members.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < v.Members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty)
                        {
                            sb.Append('\n');
                            Indent(sb, depth + 1);
                        }
                        WriteString(sb, v.Members[i].Key);
                        sb.Append(pretty ? ": " : ":");
                        WriteValue(sb, v.Members[i].Value, pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append('}');
                    break;
            }
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        /// <summary>整数不带小数点，其余用最短往返表示</summary>
        private static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
            {
                return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            }
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("\"\""); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        /* ---------------- 解析器 ---------------- */

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; _i = 0; }

            public bool AtEnd { get { return _i >= _s.Length; } }

            public int Line
            {
                get
                {
                    int line = 1;
                    for (int k = 0; k < _i && k < _s.Length; k++)
                    {
                        if (_s[k] == '\n') line++;
                    }
                    return line;
                }
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { _i++; continue; }
                    // 容忍 BOM
                    if (c == '\uFEFF' && _i == 0) { _i++; continue; }
                    break;
                }
            }

            private char Peek()
            {
                if (_i >= _s.Length) throw new JsonException("第 " + Line + " 行：JSON 意外结束");
                return _s[_i];
            }

            private void Expect(char c)
            {
                if (_i >= _s.Length || _s[_i] != c)
                {
                    throw new JsonException("第 " + Line + " 行：期望 '" + c + "'，实际 "
                        + (_i >= _s.Length ? "文件结束" : "'" + _s[_i] + "'"));
                }
                _i++;
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) throw new JsonException("JSON 嵌套层级过深");
                SkipWhitespace();
                if (AtEnd) throw new JsonException("JSON 意外结束");

                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return JsonValue.Str(ParseString());
                    case 't': ExpectWord("true"); return JsonValue.Bool(true);
                    case 'f': ExpectWord("false"); return JsonValue.Bool(false);
                    case 'n': ExpectWord("null"); return JsonValue.Null();
                    default: return JsonValue.Num(ParseNumber());
                }
            }

            private void ExpectWord(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw new JsonException("第 " + Line + " 行：无法识别的字面量（期望 " + word + "）");
                }
                _i += word.Length;
            }

            private JsonValue ParseObject(int depth)
            {
                Expect('{');
                var obj = JsonValue.Obj();
                SkipWhitespace();
                if (!AtEnd && Peek() == '}') { _i++; return obj; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("第 " + Line + " 行：对象没有闭合");
                    if (Peek() != '"') throw new JsonException("第 " + Line + " 行：对象的键必须是字符串");
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    JsonValue val = ParseValue(depth + 1);
                    obj.Members.Add(new KeyValuePair<string, JsonValue>(key, val));

                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("第 " + Line + " 行：对象没有闭合");
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; return obj; }
                    throw new JsonException("第 " + Line + " 行：对象中期望 ',' 或 '}'，实际 '" + c + "'");
                }
            }

            private JsonValue ParseArray(int depth)
            {
                Expect('[');
                var arr = JsonValue.Arr();
                SkipWhitespace();
                if (!AtEnd && Peek() == ']') { _i++; return arr; }

                while (true)
                {
                    JsonValue val = ParseValue(depth + 1);
                    arr.Items.Add(val);

                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("第 " + Line + " 行：数组没有闭合");
                    char c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; return arr; }
                    throw new JsonException("第 " + Line + " 行：数组中期望 ',' 或 ']'，实际 '" + c + "'");
                }
            }

            private string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new JsonException("第 " + Line + " 行：字符串没有闭合");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();

                    if (c == '\\')
                    {
                        if (AtEnd) throw new JsonException("第 " + Line + " 行：转义序列不完整");
                        char e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 > _s.Length) throw new JsonException("第 " + Line + " 行：\\u 转义不完整");
                                int code;
                                if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber,
                                        CultureInfo.InvariantCulture, out code))
                                {
                                    throw new JsonException("第 " + Line + " 行：\\u 转义不是合法十六进制");
                                }
                                sb.Append((char)code);
                                _i += 4;
                                break;
                            default:
                                throw new JsonException("第 " + Line + " 行：不支持的转义 \\" + e);
                        }
                        continue;
                    }

                    sb.Append(c);
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                if (!AtEnd && (Peek() == '-' || Peek() == '+')) _i++;
                while (!AtEnd)
                {
                    char c = _s[_i];
                    if (char.IsDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { _i++; continue; }
                    break;
                }
                if (_i == start) throw new JsonException("第 " + Line + " 行：无法识别的值");

                string token = _s.Substring(start, _i - start);
                double d;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    throw new JsonException("第 " + Line + " 行：无效数字 '" + token + "'");
                }
                return d;
            }
        }
    }

    internal sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
