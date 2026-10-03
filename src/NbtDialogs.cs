using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 弹窗通用零件                                                        */
    /* ================================================================== */

    internal static class NbtUi
    {
        public static void StyleForm(Form f, string title, int width, int height)
        {
            f.Text = title;
            f.BackColor = Theme.BgBase;
            f.ForeColor = Theme.Fg;
            f.Font = Theme.F(9f);
            f.ClientSize = new Size(width, height);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            DarkMode.Apply(f);
        }

        public static Label Label(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 20),
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
        }

        public static TextBox Box(string text, int x, int y, int w, bool multiline, bool mono)
        {
            var box = new TextBox
            {
                Text = text ?? "",
                Location = new Point(x, y),
                Width = w,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = mono ? Theme.M(9f) : Theme.F(9f),
            };
            return box;
        }

        /// <summary>
        /// 多行文本区。
        ///
        /// 用 RichTextBox 并关掉系统滚动条（ScrollBars.None）：多行 TextBox 的滚动条是原生控件，
        /// 深色主题下滑块始终是白的，各种主题名都压不住（与 Ui.LogBox 同一个理由）。
        /// RichTextBox 自带滚轮与键盘滚动，没有滚动条也不影响使用。
        /// </summary>
        public static RichTextBox Multiline(string text, int x, int y, int w, int h, bool readOnly, bool mono)
        {
            var box = new RichTextBox
            {
                Text = text ?? "",
                Location = new Point(x, y),
                Size = new Size(w, h),
                ReadOnly = readOnly,
                ScrollBars = RichTextBoxScrollBars.None,
                WordWrap = false,
                DetectUrls = false,
                ShortcutsEnabled = true,
                BackColor = readOnly ? Theme.BgPanelAlt : Theme.BgInput,
                ForeColor = Theme.Fg,
                /* 边框必须关掉：FixedSingle 的框线由系统画，是纯白的
                   （实测深色主题下压不住，就是文本框四周那一圈白框）。
                   底色与卡片不同，没有框线也看得出边界。 */
                BorderStyle = BorderStyle.None,
                Font = mono ? Theme.M(9f) : Theme.F(9f),
            };
            return box;
        }

        /// <summary>
        /// 下拉框统一用自绘的 FlatSelect（原生 ComboBox 的白边框压不住，
        /// 与仓库其它页面保持一致），API 刻意模仿 ComboBox，调用处写法不变。
        /// </summary>
        public static FlatSelect Combo(int x, int y, int w, string[] items, int selected)
        {
            var c = new FlatSelect { Location = new Point(x, y), Width = w };
            Ui.StyleSelect(c);
            /* 版本说明这类长文字要比控件宽，展开列表才不会被截断 */
            c.DropDownWidth = Math.Max(w, 320);
            foreach (string s in items) c.Items.Add(s);
            if (c.ItemCount > 0) c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            return c;
        }

        public static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string Ascii(byte[] data)
        {
            var sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            return sb.ToString();
        }
    }

    /* ================================================================== */
    /* 编辑值                                                              */
    /* ================================================================== */

    /// <summary>编辑一个标签的值（对齐 NBTExplorer 的 Edit Value / Edit String / Hex Editor）</summary>
    internal sealed class NbtValueForm : Form
    {
        private readonly NbtTag _tag;
        private TextBox _box;            // 单行值
        private RichTextBox _multi;      // 多行值（字符串、数组）
        private Label _hint;
        private FlatBtn _hexBtn;

        /// <summary>当前在用的输入框内容</summary>
        private string BoxText
        {
            get { return _multi != null ? _multi.Text : _box.Text; }
        }

        private void SetBoxText(string text)
        {
            if (_multi != null) _multi.Text = text;
            else _box.Text = text;
        }

        /// <summary>把焦点放回输入框并全选，方便直接重输</summary>
        private void FocusBox()
        {
            if (_multi != null) { _multi.Focus(); _multi.SelectAll(); }
            else { _box.Focus(); _box.SelectAll(); }
        }

        public NbtValueForm(NbtTag tag)
        {
            _tag = tag;
            bool multiline = tag.Type == NbtType.String || NbtTypeInfo.IsArray(tag.Type) ||
                              NbtTypeInfo.IsContainer(tag.Type);
            bool mono = tag.Type == NbtType.String || NbtTypeInfo.IsArray(tag.Type);

            NbtUi.StyleForm(this, "编辑值 —— " + tag.Name + "（" + NbtTypeInfo.TagName(tag.Type) + "）",
                620, multiline ? 320 : 176);

            Controls.Add(NbtUi.Label("标签类型：" + NbtTypeInfo.Describe(tag.Type), 16, 12, 560));

            if (multiline)
            {
                _multi = NbtUi.Multiline(tag.EditText(), 16, 38, 588, 200, false, mono);
                Controls.Add(_multi);
            }
            else
            {
                _box = NbtUi.Box(tag.EditText(), 16, 38, 588, false, mono);
                Controls.Add(_box);
            }

            _hint = new Label
            {
                Text = HintFor(tag.Type),
                Location = new Point(16, multiline ? 244 : 66),
                Size = new Size(588, 40),
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            Controls.Add(_hint);

            int by = multiline ? 274 : 104;

            if (NbtTypeInfo.IsArray(tag.Type))
            {
                _hexBtn = new FlatBtn("十六进制编辑…", BtnKind.Outline, 130) { Location = new Point(16, by) };
                _hexBtn.Click += (s, e) => OpenHex();
                Controls.Add(_hexBtn);
            }

            var ok = new FlatBtn("确定", BtnKind.Primary, 88) { Location = new Point(428, by) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(522, by) };
            ok.Click += (s, e) => Apply();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static string HintFor(NbtType type)
        {
            switch (type)
            {
                case NbtType.Byte: return "范围 -128 ~ 127，可写十进制或 0x 开头的十六进制。";
                case NbtType.Short: return "范围 -32768 ~ 32767。";
                case NbtType.Int: return "范围 -2147483648 ~ 2147483647。";
                case NbtType.Long: return "64 位整数，可加 L 后缀。";
                case NbtType.Float: return "单精度浮点，可加 f 后缀；NaN / Infinity 也认。";
                case NbtType.Double: return "双精度浮点，可加 d 后缀。";
                case NbtType.String: return "换行按原样保存（树里会显示成 ¶）。";
                case NbtType.ByteArray: return "用逗号或空格分隔；负值按有符号解释。";
                case NbtType.ShortArray: return "用逗号或空格分隔的短整数。";
                case NbtType.IntArray: return "用逗号或空格分隔的整数。";
                case NbtType.LongArray: return "用逗号或空格分隔的长整数，可加 L 后缀。";
                case NbtType.List: return "列表的值由元素决定，请用树形操作增删。";
                case NbtType.Compound: return "复合标签的值由子标签决定，请用树形操作增删。";
            }
            return "";
        }

        private void OpenHex()
        {
            var bytes = _tag as NbtByteArray;
            if (bytes == null) return;

            using (var hex = new NbtHexForm(bytes.Value))
            {
                if (hex.ShowDialog(this) != DialogResult.OK) return;
                bytes.Value = hex.Result;
                SetBoxText(bytes.EditText());
            }
        }

        private void Apply()
        {
            try
            {
                if (NbtTypeInfo.IsContainer(_tag.Type))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                _tag.SetFromText(BoxText);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, "值不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                FocusBox();
            }
        }
    }

    /* ================================================================== */
    /* 十六进制编辑器                                                      */
    /* ================================================================== */

    /// <summary>字节数组的十六进制编辑（NBTExplorer 里那个 HexBox 的简化版）</summary>
    internal sealed class NbtHexForm : Form
    {
        private RichTextBox _hex;
        private RichTextBox _ascii;
        private Label _info;

        public byte[] Result;

        public NbtHexForm(byte[] data)
        {
            var bytes = data ?? new byte[0];
            Result = bytes;

            NbtUi.StyleForm(this, "十六进制编辑 —— " + bytes.Length + " 字节", 760, 460);

            _hex = NbtUi.Multiline(Format(bytes), 16, 32, 470, 340, false, true);
            _hex.TextChanged += (s, e) => UpdateAscii();

            _ascii = NbtUi.Multiline("", 498, 32, 246, 340, true, true);

            _info = new Label
            {
                Location = new Point(16, 378),
                Size = new Size(728, 20),
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };

            Controls.Add(NbtUi.Label("十六进制（每行 16 字节，空格分隔）", 16, 10, 400));
            Controls.Add(NbtUi.Label("ASCII", 498, 10, 200));
            Controls.Add(_hex);
            Controls.Add(_ascii);
            Controls.Add(_info);

            var ok = new FlatBtn("确定", BtnKind.Primary, 88) { Location = new Point(568, 404) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(662, 404) };
            ok.Click += (s, e) => Apply();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);

            UpdateAscii();
        }

        private static string Format(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                    if (i % 16 == 0) sb.AppendLine();
                }
                sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>把编辑框里的十六进制文本解析回字节；不能识别的字符直接跳过</summary>
        private static byte[] Parse(string text, out string error)
        {
            error = null;
            var list = new List<byte>();
            var token = new StringBuilder(2);

            Action flush = () =>
            {
                if (token.Length == 0) return;
                if (token.Length == 1) token.Insert(0, '0');
                byte b;
                if (!byte.TryParse(token.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    throw new FormatException("「" + token + "」不是合法的十六进制字节。");
                list.Add(b);
                token.Length = 0;
            };

            try
            {
                foreach (char c in text)
                {
                    if (Uri.IsHexDigit(c)) { token.Append(c); if (token.Length == 2) flush(); }
                    else if (char.IsWhiteSpace(c) || c == ',' || c == '-') flush();
                    else throw new FormatException("出现了十六进制以外的字符「" + c + "」。");
                }
                flush();
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
            return list.ToArray();
        }

        private void UpdateAscii()
        {
            string err;
            var parsed = Parse(_hex.Text, out err);
            if (parsed == null)
            {
                _ascii.Text = "";
                _info.Text = err;
                _info.ForeColor = Theme.Danger;
                return;
            }
            _ascii.Text = NbtUi.Ascii(parsed);
            _info.Text = "共 " + parsed.Length + " 字节";
            _info.ForeColor = Theme.FgFaint;
        }

        private void Apply()
        {
            string err;
            var parsed = Parse(_hex.Text, out err);
            if (parsed == null)
            {
                MessageBox.Show(this, err, "十六进制内容不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = parsed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /* ================================================================== */
    /* 添加标签                                                            */
    /* ================================================================== */

    internal sealed class NbtAddForm : Form
    {
        private FlatSelect _type;
        private TextBox _name, _value;
        private Label _valueLabel;

        public NbtType PickedType;
        public string PickedName { get { return _name.Text; } }
        public string PickedValue { get { return _value.Text; } }

        public NbtAddForm(NbtType defaultType)
        {
            NbtUi.StyleForm(this, "添加子标签", 560, 250);

            var names = new string[NbtTypeInfo.Creatable.Length];
            int selected = 0;
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = NbtTypeInfo.Describe(NbtTypeInfo.Creatable[i]);
                if (NbtTypeInfo.Creatable[i] == defaultType) selected = i;
            }

            Controls.Add(NbtUi.Label("标签类型", 16, 12, 200));
            _type = NbtUi.Combo(16, 34, 512, names, selected);

            Controls.Add(NbtUi.Label("标签名", 16, 72, 200));
            _name = NbtUi.Box("", 16, 94, 512, false, false);

            _valueLabel = NbtUi.Label("初始值", 16, 132, 200);
            _value = NbtUi.Box("", 16, 154, 512, false, true);
            _value.Font = Theme.F(9f);

            _type.SelectedIndexChanged += (s, e) => SyncValueBox();
            SyncValueBox();

            var ok = new FlatBtn("添加", BtnKind.Primary, 88) { Location = new Point(352, 198) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(446, 198) };
            ok.Click += (s, e) => Accept2();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);

            Controls.Add(_valueLabel);
            Controls.Add(_type);
            Controls.Add(_name);
            Controls.Add(_value);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private NbtType CurrentType
        {
            get
            {
                int i = _type.SelectedIndex;
                if (i < 0 || i >= NbtTypeInfo.Creatable.Length) return NbtType.Compound;
                return NbtTypeInfo.Creatable[i];
            }
        }

        private void SyncValueBox()
        {
            bool container = NbtTypeInfo.IsContainer(CurrentType);
            _value.Enabled = !container;
            _valueLabel.Text = container ? "初始值（容器不需要填）" : "初始值";
            if (container) _value.Text = "";
            else if (_value.Text.Length == 0) _value.Text = DefaultValue(CurrentType);
        }

        private static string DefaultValue(NbtType type)
        {
            if (NbtTypeInfo.IsNumeric(type)) return "0";
            return "";
        }

        private void Accept2()
        {
            if (_name.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "标签名不能为空。", "缺少标签名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return;
            }

            PickedType = CurrentType;
            try
            {
                /* 先试一下值能不能解析，别等加进去才报错 */
                if (!NbtTypeInfo.IsContainer(PickedType))
                {
                    var probe = NbtTypeInfo.CreateEmpty(PickedType, _name.Text.Trim());
                    probe.SetFromText(_value.Text);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, "值不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _value.Focus();
                _value.SelectAll();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /* ================================================================== */
    /* 查找与替换                                                          */
    /* ================================================================== */

    internal sealed class NbtFindForm : Form
    {
        private readonly NbtTag _root;

        private FlatSelect _nameMode, _typeCombo, _compareCombo;
        private TextBox _nameText, _valueText, _replaceText;
        private ToolCheck _typeCheck, _valueCheck;
        private ToolList _results;
        private Label _summary;

        private List<NbtSpot> _hits = new List<NbtSpot>();
        private int _cursor = -1;

        /// <summary>选中某处结果时通知页面（页面负责定位到树上）</summary>
        public Action<NbtSpot> Navigate;
        /// <summary>发生过替换时通知页面（页面负责标脏）</summary>
        public Action Replaced;

        public NbtFindForm(NbtTag root, NbtSearchRule initial)
        {
            _root = root;
            NbtUi.StyleForm(this, "查找与替换 NBT", 860, 560);

            /* ---- 名称 ---- */
            Controls.Add(NbtUi.Label("名称", 16, 12, 120));
            _nameMode = NbtUi.Combo(16, 34, 130, new[] { "包含", "通配符", "正则", "等于" }, 1);
            _nameText = NbtUi.Box(initial != null ? initial.NamePattern : "", 154, 34, 300, false, false);
            Controls.Add(_nameMode);
            Controls.Add(_nameText);

            /* ---- 类型 ---- */
            _typeCheck = new ToolCheck("限定类型", initial != null && initial.FilterByType, 110) { Location = new Point(470, 34) };
            var typeNames = new string[NbtTypeInfo.Creatable.Length];
            int sel = 10;   // 默认 TAG_Compound
            for (int i = 0; i < typeNames.Length; i++)
            {
                typeNames[i] = NbtTypeInfo.TagName(NbtTypeInfo.Creatable[i]);
                if (NbtTypeInfo.Creatable[i] == NbtType.Compound) sel = i;
            }
            _typeCombo = NbtUi.Combo(584, 32, 250, typeNames, sel);
            _typeCheck.CheckedChanged += (s, e) => _typeCombo.Enabled = _typeCheck.Checked;
            _typeCombo.Enabled = _typeCheck.Checked;
            Controls.Add(_typeCheck);
            Controls.Add(_typeCombo);

            /* ---- 值 ---- */
            _valueCheck = new ToolCheck("按值筛选", initial != null && initial.ValueCompare != NbtValueCompare.None, 110) { Location = new Point(16, 74) };
            _compareCombo = NbtUi.Combo(130, 72, 130,
                new[] { "等于", "不等于", "大于", "小于", "包含", "以…开头" }, 0);
            _valueText = NbtUi.Box(initial != null ? initial.ValueText : "", 268, 72, 186, false, false);
            _valueCheck.CheckedChanged += (s, e) => { _compareCombo.Enabled = _valueCheck.Checked; _valueText.Enabled = _valueCheck.Checked; };
            _compareCombo.Enabled = _valueCheck.Checked;
            _valueText.Enabled = _valueCheck.Checked;
            Controls.Add(_valueCheck);
            Controls.Add(_compareCombo);
            Controls.Add(_valueText);

            var findBtn = new FlatBtn("查找全部", BtnKind.Primary, 100) { Location = new Point(470, 70) };
            var nextBtn = new FlatBtn("查找下一个", BtnKind.Outline, 110) { Location = new Point(576, 70) };
            findBtn.Click += (s, e) => RunSearch(true);
            nextBtn.Click += (s, e) => RunSearch(false);
            Controls.Add(findBtn);
            Controls.Add(nextBtn);

            /* ---- 替换 ---- */
            Controls.Add(NbtUi.Label("替换为", 16, 112, 120));
            _replaceText = NbtUi.Box("", 130, 110, 324, false, false);
            var replaceBtn = new FlatBtn("替换选中项", BtnKind.Outline, 120) { Location = new Point(470, 108) };
            var replaceAllBtn = new FlatBtn("全部替换", BtnKind.Danger, 110) { Location = new Point(596, 108) };
            replaceBtn.Click += (s, e) => ReplaceCurrent();
            replaceAllBtn.Click += (s, e) => ReplaceAll();
            Controls.Add(_replaceText);
            Controls.Add(replaceBtn);
            Controls.Add(replaceAllBtn);

            /* ---- 结果 ---- */
            _results = new ToolList { Location = new Point(16, 150), Size = new Size(826, 340), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            _results.SetColumns("路径", -1, "类型", 120, "值", 220);
            _results.SelectedIndexChanged += (s, e) => SelectCurrent(false);
            _results.DoubleClick += (s, e) => SelectCurrent(true);
            Controls.Add(_results);

            _summary = new Label
            {
                Location = new Point(16, 498),
                Size = new Size(700, 20),
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            Controls.Add(_summary);

            var close = new FlatBtn("关闭", BtnKind.Normal, 88) { Location = new Point(754, 494) };
            close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(close);
            CancelButton = close;

            _summary.Text = "提示：名称留空表示不限制；通配符里 * 匹配任意多字符、? 匹配一个字符。";
        }

        private NbtSearchRule BuildRule(out string error)
        {
            error = null;
            var rule = new NbtSearchRule();
            rule.NamePattern = _nameText.Text;
            switch (_nameMode.SelectedIndex)
            {
                case 0: rule.NameMode = NbtMatchMode.Contains; break;
                case 1: rule.NameMode = NbtMatchMode.Wildcard; break;
                case 2: rule.NameMode = NbtMatchMode.Regex; break;
                default: rule.NameMode = NbtMatchMode.Exact; break;
            }
            rule.FilterByType = _typeCheck.Checked;
            rule.Type = NbtTypeInfo.Creatable[Math.Max(0, _typeCombo.SelectedIndex)];

            if (_valueCheck.Checked)
            {
                switch (_compareCombo.SelectedIndex)
                {
                    case 0: rule.ValueCompare = NbtValueCompare.Equal; break;
                    case 1: rule.ValueCompare = NbtValueCompare.NotEqual; break;
                    case 2: rule.ValueCompare = NbtValueCompare.Greater; break;
                    case 3: rule.ValueCompare = NbtValueCompare.Less; break;
                    case 4: rule.ValueCompare = NbtValueCompare.Contains; break;
                    default: rule.ValueCompare = NbtValueCompare.StartsWith; break;
                }
                rule.ValueText = _valueText.Text;
            }

            error = NbtSearch.ValidatePattern(rule);
            return rule;
        }

        private void RunSearch(bool fromStart)
        {
            string error;
            var rule = BuildRule(out error);
            if (error != null)
            {
                MessageBox.Show(this, error, "查找条件有问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _hits = NbtSearch.FindAll(_root, rule, 5000);
            _results.Items.Clear();
            foreach (var spot in _hits)
            {
                var item = new ListViewItem(spot.Path);
                item.SubItems.Add(NbtTypeInfo.ShortName(spot.Tag.Type));
                item.SubItems.Add(spot.Tag.ValueText());
                _results.Items.Add(item);
            }

            _summary.Text = _hits.Count == 0
                ? "没有找到匹配项（" + rule.Describe() + "）"
                : "找到 " + _hits.Count + " 项（" + rule.Describe() + "）" + (_hits.Count >= 5000 ? "，只显示前 5000 项" : "");

            if (_hits.Count == 0) { _cursor = -1; return; }

            if (fromStart)
            {
                _cursor = 0;
                _results.Items[0].Selected = true;
                _results.EnsureVisible(0);
                SelectCurrent(true);
            }
            else
            {
                _cursor = (_cursor + 1) % _hits.Count;
                _results.Items[_cursor].Selected = true;
                _results.EnsureVisible(_cursor);
                SelectCurrent(true);
            }
        }

        private void SelectCurrent(bool jump)
        {
            if (_cursor < 0 || _cursor >= _hits.Count) return;
            if (_results.SelectedIndices.Count > 0) _cursor = _results.SelectedIndices[0];
            if (_cursor < 0 || _cursor >= _hits.Count) return;

            if (jump && Navigate != null) Navigate(_hits[_cursor]);
        }

        private void ReplaceCurrent()
        {
            if (_cursor < 0 || _cursor >= _hits.Count)
            {
                MessageBox.Show(this, "先查一次，再选中要替换的那一项。", "没有选中项", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var spot = _hits[_cursor];
            string error = ReplaceSpot(spot);
            if (error != null)
            {
                MessageBox.Show(this, error, "替换失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Replaced != null) Replaced();
            _summary.Text = "已替换：" + spot.Path;
            RunSearch(true);
        }

        private void ReplaceAll()
        {
            if (_hits.Count == 0)
            {
                MessageBox.Show(this, "先查一次，确认要替换的范围。", "没有结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!PageKit.Confirm(this, "要把 " + _hits.Count + " 项的值都替换成「" + _replaceText.Text + "」吗？\n（撤销可以找回，但整批替换会算一步）"))
                return;

            int done = 0;
            var failed = new List<string>();
            foreach (var spot in _hits)
            {
                string error = ReplaceSpot(spot);
                if (error == null) done++;
                else failed.Add(spot.Path + "：" + error);
            }

            if (done > 0 && Replaced != null) Replaced();
            _summary.Text = "已替换 " + done + " 项" + (failed.Count > 0 ? "，失败 " + failed.Count + " 项" : "");
            if (failed.Count > 0)
            {
                var sb = new StringBuilder("以下项目没能替换：\n");
                for (int i = 0; i < Math.Min(12, failed.Count); i++) sb.AppendLine("· " + failed[i]);
                if (failed.Count > 12) sb.AppendLine("… 还有 " + (failed.Count - 12) + " 项");
                MessageBox.Show(this, sb.ToString(), "部分替换失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RunSearch(true);
        }

        private string ReplaceSpot(NbtSpot spot)
        {
            try
            {
                if (NbtTypeInfo.IsContainer(spot.Tag.Type))
                    return "容器标签不能直接改值，请用树形操作。";
                spot.Tag.SetFromText(_replaceText.Text);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }

    /* ================================================================== */
    /* 另存为                                                              */
    /* ================================================================== */

    internal sealed class NbtSaveAsForm : Form
    {
        private FlatSelect _compression;
        private ToolCheck _little, _header;
        private TextBox _path;

        public string PickedPath { get { return _path.Text; } }
        public NbtCompression PickedCompression;
        public bool PickedLittleEndian { get { return _little.Checked; } }
        public bool PickedBedrockHeader { get { return _header.Checked; } }

        public NbtSaveAsForm(NbtDocument doc, string suggestPath)
        {
            NbtUi.StyleForm(this, "另存为 NBT", 620, 280);

            Controls.Add(NbtUi.Label("保存位置", 16, 12, 200));
            _path = NbtUi.Box(suggestPath, 16, 34, 470, false, false);
            var browse = new FlatBtn("浏览…", BtnKind.Outline, 88) { Location = new Point(494, 33) };
            browse.Click += (s, e) =>
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "另存为 NBT 文件";
                    dlg.Filter = "NBT 数据 (*.dat;*.nbt)|*.dat;*.nbt|所有文件 (*.*)|*.*";
                    dlg.FileName = System.IO.Path.GetFileName(_path.Text);
                    if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.FileName;
                }
            };
            Controls.Add(_path);
            Controls.Add(browse);

            int cur = 0;
            if (doc != null)
            {
                if (doc.Compression == NbtCompression.GZip) cur = 1;
                else if (doc.Compression == NbtCompression.Zlib) cur = 2;
                else if (doc.Compression == NbtCompression.None) cur = 3;
            }

            Controls.Add(NbtUi.Label("压缩方式", 16, 74, 200));
            _compression = NbtUi.Combo(16, 96, 240,
                new[] { "保持原样", "gzip（Java 版常用）", "zlib", "不压缩" }, cur);
            Controls.Add(_compression);

            _little = new ToolCheck("小端字节序（基岩版常见）", doc != null && doc.LittleEndian, 260) { Location = new Point(280, 96) };
            _header = new ToolCheck("写入基岩版 8 字节头（level.dat）", doc != null && doc.HasBedrockHeader, 280) { Location = new Point(280, 126) };
            Controls.Add(_little);
            Controls.Add(_header);

            var note = new Label
            {
                Text = "说明：Java 版存档与数据包用 gzip + 大端；基岩版 level.dat 是「8 字节头 + 小端」。\nMCPack Studio 只改动你编辑过的内容，其余字节按原格式写回。",
                Location = new Point(16, 156),
                Size = new Size(580, 40),
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            Controls.Add(note);

            var ok = new FlatBtn("保存", BtnKind.Primary, 88) { Location = new Point(412, 224) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(506, 224) };
            ok.Click += (s, e) =>
            {
                if (_path.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, "请填保存位置。", "缺少路径", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                int i = _compression.SelectedIndex;
                if (i == 1) PickedCompression = NbtCompression.GZip;
                else if (i == 2) PickedCompression = NbtCompression.Zlib;
                else if (i == 3) PickedCompression = NbtCompression.None;
                else PickedCompression = doc != null ? doc.Compression : NbtCompression.GZip;

                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /* ================================================================== */
    /* 纯文本弹窗（导入 / 导出 SNBT、JSON）                                 */
    /* ================================================================== */

    internal sealed class NbtTextForm : Form
    {
        private readonly RichTextBox _box;

        /// <summary>编辑框里的内容（不能叫 Text，会和 Form.Text 撞名）</summary>
        public string TextValue { get { return _box.Text; } }

        /// <summary>
        /// 文本窗口。readOnly=true 用来展示导出结果；否则「确定」时会把内容交给
        /// validate 校验，校验不通过就停在这个窗口里继续改。
        /// </summary>
        public NbtTextForm(string title, string hint, string text, bool readOnly, Func<string, string> validate)
        {
            NbtUi.StyleForm(this, title, 760, 520);

            var label = new Label
            {
                Text = hint,
                Location = new Point(16, 10),
                Size = new Size(728, 34),
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            Controls.Add(label);

            _box = NbtUi.Multiline(text, 16, 48, 728, 400, readOnly, true);
            Controls.Add(_box);

            if (!readOnly)
            {
                var copy = new FlatBtn("复制到剪贴板", BtnKind.Outline, 120) { Location = new Point(16, 462) };
                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(_box.Text); }
                    catch { }
                };
                Controls.Add(copy);

                var ok = new FlatBtn("确定", BtnKind.Primary, 88) { Location = new Point(552, 462) };
                var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(646, 462) };
                ok.Click += (s, e) =>
                {
                    if (validate != null)
                    {
                        string error = validate(_box.Text);
                        if (error != null)
                        {
                            MessageBox.Show(this, error, "内容有问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    DialogResult = DialogResult.OK;
                    Close();
                };
                cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(ok);
                Controls.Add(cancel);
                AcceptButton = null;
                CancelButton = cancel;
            }
            else
            {
                var copy = new FlatBtn("复制到剪贴板", BtnKind.Outline, 120) { Location = new Point(16, 462) };
                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(_box.Text); }
                    catch { }
                };
                var close = new FlatBtn("关闭", BtnKind.Primary, 88) { Location = new Point(646, 462) };
                close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
                Controls.Add(copy);
                Controls.Add(close);
                CancelButton = close;
            }
        }
    }

    /* ================================================================== */
    /* 单行输入（改名等）                                                   */
    /* ================================================================== */

    internal sealed class NbtInputForm : Form
    {
        private readonly TextBox _box;

        public string Value { get { return _box.Text; } }

        public NbtInputForm(string title, string label, string initial)
        {
            NbtUi.StyleForm(this, title, 460, 140);

            Controls.Add(NbtUi.Label(label, 16, 12, 420));
            _box = NbtUi.Box(initial, 16, 36, 428, false, false);
            Controls.Add(_box);

            var ok = new FlatBtn("确定", BtnKind.Primary, 88) { Location = new Point(268, 88) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(362, 88) };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }
    }

    /* ================================================================== */
    /* 文件选择（打开文件夹时列出来的那批）                                  */
    /* ================================================================== */

    internal sealed class NbtPickForm : Form
    {
        private readonly ToolList _list;

        public string Picked;

        public NbtPickForm(string title, List<string> files)
        {
            NbtUi.StyleForm(this, "选择一个 NBT 文件", 720, 420);

            Controls.Add(NbtUi.Label(title, 16, 10, 680));

            _list = new ToolList { Location = new Point(16, 34), Size = new Size(688, 330), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            _list.SetColumns("文件", 260, "大小", 90, "修改时间", 150, "目录", -1);
            foreach (string f in files)
            {
                var item = new ListViewItem(Path.GetFileName(f));
                try
                {
                    var info = new System.IO.FileInfo(f);
                    item.SubItems.Add(Ui.FormatBytes(info.Length));
                    item.SubItems.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                catch
                {
                    item.SubItems.Add("-");
                    item.SubItems.Add("-");
                }
                item.SubItems.Add(Path.GetDirectoryName(f));
                item.Tag = f;
                _list.Items.Add(item);
            }
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            _list.DoubleClick += (s, e) => Accept();
            Controls.Add(_list);

            var ok = new FlatBtn("打开", BtnKind.Primary, 88) { Location = new Point(518, 374) };
            var cancel = new FlatBtn("取消", BtnKind.Normal, 88) { Location = new Point(612, 374) };
            ok.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "先选中一个文件。", "还没选", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Picked = Convert.ToString(_list.SelectedItems[0].Tag);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

