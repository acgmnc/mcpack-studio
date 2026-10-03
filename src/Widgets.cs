using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 圆角卡片容器                                                        */
    /* ================================================================== */

    internal sealed class Card : Panel
    {
        public string Title;
        public string Hint;

        /// <summary>标题栏高度；设置了 Title 才会预留</summary>
        public const int HeaderHeight = 40;

        /// <summary>内容区容器，往这里加控件并用 Dock 排版</summary>
        public readonly Panel Body;

        /// <summary>内容区内边距，需要按内容精算卡片高度时可以调小</summary>
        public Padding BodyPadding = new Padding(18, 10, 18, 14);

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgPanel;

            Body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(18, 10, 18, 14),
            };
            Controls.Add(Body);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            // 有标题时把内容区压到标题下面，避免内容画到标题上
            var p = BodyPadding;
            Body.Padding = new Padding(p.Left, string.IsNullOrEmpty(Title) ? 12 : p.Top, p.Right, p.Bottom);
            Body.Location = new Point(0, string.IsNullOrEmpty(Title) ? 0 : HeaderHeight);
            Body.Size = new Size(Width, Math.Max(0, Height - Body.Location.Y));
            Body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Body != null)
            {
                int top = string.IsNullOrEmpty(Title) ? 0 : HeaderHeight;
                Body.Location = new Point(0, top);
                Body.Size = new Size(Width, Math.Max(0, Height - top));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(r, 8))
            {
                using (var b = new SolidBrush(Theme.BgPanel))
                    g.FillPath(b, path);
                using (var pen = new Pen(Theme.Line, 1))
                    g.DrawPath(pen, path);
            }

            if (string.IsNullOrEmpty(Title)) return;

            using (var f = Theme.F(9.5f, FontStyle.Bold))
            {
                using (var b = new SolidBrush(Theme.Fg))
                {
                    g.DrawString(Title, f, b, new PointF(18, 12));
                }

                if (!string.IsNullOrEmpty(Hint))
                {
                    float titleW = g.MeasureString(Title, f).Width;
                    float x = 18 + titleW + 12;
                    float avail = Width - x - 18;
                    if (avail > 60)
                    {
                        using (var hf = Theme.F(8.5f))
                        using (var hb = new SolidBrush(Theme.FgFaint))
                        {
                            g.DrawString(Clip(g, Hint, hf, avail), hf, hb, new PointF(x, 14));
                        }
                    }
                }
            }

            using (var pen = new Pen(Theme.LineSoft, 1))
            {
                g.DrawLine(pen, 1, HeaderHeight, Width - 2, HeaderHeight);
            }
        }

        /// <summary>把文字按宽度截断并加省略号</summary>
        private static string Clip(Graphics g, string text, Font font, float maxWidth)
        {
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            for (int len = text.Length - 1; len > 1; len--)
            {
                string candidate = text.Substring(0, len) + "…";
                if (g.MeasureString(candidate, font).Width <= maxWidth) return candidate;
            }
            return "";
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /* ================================================================== */
    /* 按钮                                                                */
    /* ================================================================== */

    internal enum BtnKind { Normal, Primary, Outline, Danger, Ghost }

    internal sealed class FlatBtn : Button
    {
        private bool _hover, _down;
        public BtnKind Kind = BtnKind.Normal;

        public FlatBtn(string text, BtnKind kind, int width)
        {
            Text = text;
            Kind = kind;
            Width = width;
            Height = 30;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.F(9f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, fg;

            switch (Kind)
            {
                case BtnKind.Primary:
                    fill = _down ? Theme.Mix(Theme.Accent, Color.Black, 0.18)
                         : _hover ? Theme.AccentHover : Theme.Accent;
                    border = fill;
                    fg = Color.White;
                    break;
                case BtnKind.Outline:
                    fill = _hover ? Theme.BgHover : Theme.BgPanel;
                    border = _hover ? Theme.Accent : Theme.LineStrong;
                    fg = Theme.Fg;
                    break;
                case BtnKind.Danger:
                    fill = _hover ? Color.FromArgb(72, 34, 40) : Theme.BgPanel;
                    border = _hover ? Theme.Danger : Theme.LineStrong;
                    fg = _hover ? Theme.Danger : Theme.FgMuted;
                    break;
                case BtnKind.Ghost:
                    fill = _hover ? Theme.BgHover : Color.Transparent;
                    border = Color.Transparent;
                    fg = _hover ? Theme.Fg : Theme.FgMuted;
                    break;
                default:
                    fill = _down ? Theme.BgActive : (_hover ? Theme.BgHover : Theme.BgPanelAlt);
                    border = Theme.LineStrong;
                    fg = Theme.Fg;
                    break;
            }

            if (!Enabled)
            {
                fill = Theme.BgPanel;
                border = Theme.LineSoft;
                fg = Theme.FgFaint;
            }

            using (var path = Card.Rounded(r, 6))
            {
                if (fill != Color.Transparent)
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                }
                if (border != Color.Transparent)
                {
                    using (var pen = new Pen(border, 1)) g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /* ================================================================== */
    /* 复选框 / 单选框（统一字号）                                          */
    /* ================================================================== */

    internal sealed class ToolCheck : CheckBox
    {
        public ToolCheck(string text, bool check, int width)
        {
            Text = text;
            Checked = check;
            Width = width;
            Height = 24;
            AutoSize = false;
            ForeColor = Theme.FgMuted;
            Font = Theme.F(9f);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
        }
    }

    internal sealed class ToolRadio : RadioButton
    {
        public ToolRadio(string text, bool check)
        {
            Text = text;
            Checked = check;
            AutoSize = true;
            ForeColor = Theme.FgMuted;
            Font = Theme.F(9f);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
        }
    }

    /* ================================================================== */
    /* 深色列表                                                            */
    /* ================================================================== */

    /// <summary>
    /// 统一风格的明细列表。
    ///
    /// ListView 的列头颜色在 WinForms 里没有公开属性可控，只能把 OwnerDraw 打开自绘，
    /// 否则在深色主题下会露出一条系统配色的白条（和整体完全不搭）。
    /// </summary>
    internal sealed class ToolList : ListView
    {
        public ToolList()
        {
            View = View.Details;
            FullRowSelect = true;
            GridLines = false;
            HideSelection = false;
            MultiSelect = false;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.BgInput;
            ForeColor = Theme.Fg;
            Font = Theme.F(8.5f);
            OwnerDraw = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            /* ListView 默认不双缓冲，自绘会闪；用反射打开它的 DoubleBuffered */
            try
            {
                var prop = typeof(ListView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop != null) prop.SetValue(this, true, null);
            }
            catch { }

            /* 滚动条由系统绘制，得让它走深色主题 */
            HandleCreated += (s, e) =>
            {
                DarkMode.ApplyTo(Handle);
                DarkMode.ApplyDeferred(this, 4);
            };

            /*
             * 兜底背景 + 滚动条深色化。
             *
             * OwnerDraw 只对「真实存在的列/项」触发绘制事件，
             * 列宽加起来没占满控件时，右侧会露出一条系统配色的白边
             * （ListView 自身没有横跨整行的画布事件可用）。
             * 这里先手动画一次背景，把那条白边盖掉。
             *
             * 尺寸一变滚动条就可能出现/消失，这时要重新套一次主题。
             */
            Paint += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.BgInput))
                    e.Graphics.FillRectangle(b, ClientRectangle);

                /* 滚动条可能在使用过程中才出现（加了几行数据、窗口变小），
                   出现时是系统浅色。这里保留若干次补救机会，
                   次数用光就停，避免每帧都去枚举子窗口。 */
                if (_themeChecks < 20)
                {
                    _themeChecks++;
                    DarkMode.ApplyThemeToTree(Handle, 0);
                }
            };

            SizeChanged += (s, e) => DarkMode.ApplyDeferred(this, 2);
        }

        private int _themeChecks;

        /// <summary>
        /// 按 (标题, 宽度) 建列。宽度传 -1 表示这一列吃掉剩余宽度。
        ///
        /// 必须让列宽之和填满控件：列宽不足时 ListView 会在右侧留一条
        /// 系统配色的空白填充带，自绘也盖不掉（它由内部子窗口绘制），
        /// 在深色主题下就是一条刺眼的白边。
        /// </summary>
        public void SetColumns(params object[] pairs)
        {
            _specs.Clear();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                _specs.Add(new ColSpec
                {
                    Title = Convert.ToString(pairs[i]),
                    Width = Convert.ToInt32(pairs[i + 1]),
                });

            Columns.Clear();
            foreach (var s in _specs) Columns.Add(s.Title, s.Width < 0 ? 100 : s.Width);

            SizeChanged += (s, e) => FitColumns();
            FitColumns();
        }

        private sealed class ColSpec
        {
            public string Title;
            public int Width;
        }

        private readonly List<ColSpec> _specs = new List<ColSpec>();

        /// <summary>把标了 -1 的列撑到剩余宽度，保证列宽之和 == 控件宽度</summary>
        private void FitColumns()
        {
            if (_specs.Count == 0 || Columns.Count != _specs.Count) return;

            int total = ClientSize.Width;
            if (total <= 0) return;

            int fixedSum = 0;
            int flexCount = 0;
            foreach (var s in _specs)
            {
                if (s.Width < 0) flexCount++;
                else fixedSum += s.Width;
            }
            if (flexCount == 0) return;

            int flex = (total - fixedSum) / flexCount;
            if (flex < 40) flex = 40;      // 太窄就没意义了，交给横向滚动条

            for (int i = 0; i < _specs.Count; i++)
            {
                int w = _specs[i].Width < 0 ? flex : _specs[i].Width;
                if (Columns[i].Width != w) Columns[i].Width = w;
            }
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            var g = e.Graphics;
            var r = e.Bounds;

            using (var b = new SolidBrush(Theme.BgPanelAlt)) g.FillRectangle(b, r);
            using (var pen = new Pen(Theme.Line, 1))
            {
                g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                g.DrawLine(pen, r.Right - 1, r.Top + 4, r.Right - 1, r.Bottom - 5);
            }

            var textRect = new Rectangle(r.X + 8, r.Y, Math.Max(0, r.Width - 12), r.Height);
            TextRenderer.DrawText(g, e.Header.Text, Theme.F(8.5f, FontStyle.Bold), textRect,
                Theme.FgMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            /* 整行的背景与选中态在这里画；文字由 OnDrawSubItem 负责 */
            var r = e.Bounds;
            Color fill;

            if (e.Item.Selected)
                fill = Focused ? Theme.AccentSoft : Theme.BgActive;
            else if ((e.ItemIndex & 1) == 1)
                fill = Theme.BgPanelAlt;          // 斑马纹，长列表更好读
            else
                fill = Theme.BgInput;

            using (var b = new SolidBrush(fill))
                e.Graphics.FillRectangle(b, new Rectangle(0, r.Top, ClientSize.Width, r.Height));
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            var r = e.Bounds;
            if (r.Width <= 0 || r.Height <= 0) return;

            Color fg = e.Item.Selected ? Theme.Fg : (e.Item.ForeColor == Color.Empty ? ForeColor : e.Item.ForeColor);

            /* 第一列用不着缩进；后面的列加一点左内边距 */
            var textRect = new Rectangle(r.X + 8, r.Y, Math.Max(0, r.Width - 12), r.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.F(8.5f), textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /* ================================================================== */
    /* 键值报告表                                                          */
    /* ================================================================== */

    internal sealed class ReportGrid : DataGridView
    {
        public ReportGrid()
        {
            Dock = DockStyle.Fill;
            ReadOnly = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            RowHeadersVisible = false;
            BorderStyle = BorderStyle.None;
            BackgroundColor = Theme.BgInput;
            /* 顶部留 1px：DataGridView 会在内容区最上沿画一道系统配色的白线，
               各种边框属性都关不掉，用内边距把它挤出可视范围。 */
            Padding = new Padding(0, 1, 0, 0);
            GridColor = Theme.LineSoft;
            Font = Theme.F(8.5f);
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = false;
            EnableHeadersVisualStyles = false;
            ColumnHeadersHeight = 30;
            RowTemplate.Height = 24;

            ColumnHeadersDefaultCellStyle.BackColor = Theme.BgPanelAlt;
            ColumnHeadersDefaultCellStyle.ForeColor = Theme.FgMuted;
            ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.BgPanelAlt;
            ColumnHeadersDefaultCellStyle.Font = Theme.F(8.5f, FontStyle.Bold);

            DefaultCellStyle.BackColor = Theme.BgInput;
            DefaultCellStyle.ForeColor = Theme.Fg;
            DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            DefaultCellStyle.SelectionForeColor = Theme.Fg;
            DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            Columns.Add("k", "项目");
            Columns.Add("v", "内容");
            Columns[0].FillWeight = 22;
            Columns[1].FillWeight = 78;

            /* 单元格背景设了深色还不够 —— 滚动条、行头是系统画的子窗口，
               不套深色主题的话右边和下边会各挂一条浅灰条。 */
            HandleCreated += (s, e) =>
            {
                DarkMode.ApplyTo(Handle);
                DarkMode.ApplyDeferred(this, 4);
            };
            SizeChanged += (s, e) => DarkMode.ApplyDeferred(this, 2);
        }

        /// <summary>
        /// 盖掉最外圈那一条白边。
        ///
        /// DataGridView 画完内容后会在边框位置补一圈系统配色的边线，
        /// BorderStyle.None 挡不住（AdvancedBorderStyle 是只读的），
        /// 在深色主题下就是表格顶上一条纯白线（实测 RGB 255,255,255）。
        /// 这里先用背景色铺满整块，再把父类的内容绘制裁到内侧 1px 以内，
        /// 让它画不到边框那一圈。
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            int w = Width, h = Height;
            if (w < 4 || h < 4) { base.OnPaint(e); return; }

            using (var b = new SolidBrush(Theme.BgInput))
                e.Graphics.FillRectangle(b, 0, 0, w, h);

            var old = e.Graphics.Clip;
            e.Graphics.SetClip(new Rectangle(1, 1, w - 2, h - 2));
            base.OnPaint(e);
            e.Graphics.Clip = old;
        }

        public void Add(string key, string value)
        {
            Rows.Add(key, value);
        }

        public void AddHeader(string text)
        {
            int i = Rows.Add("", text);
            Rows[i].DefaultCellStyle.ForeColor = Theme.Accent;
            Rows[i].DefaultCellStyle.Font = Theme.F(8.5f, FontStyle.Bold);
        }

        public void AddMono(string key, string value)
        {
            int i = Rows.Add(key, value);
            Rows[i].DefaultCellStyle.Font = Theme.M(8.5f);
        }
    }

    /* ================================================================== */
    /* 小工具                                                              */
    /* ================================================================== */

    internal static class Ui
    {
        public static Label Caption(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
        }

        public static Label Body(string text, float size)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Fg,
                Font = Theme.F(size),
                BackColor = Color.Transparent,
            };
        }

        public static Label Muted(string text, float size)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(size),
                BackColor = Color.Transparent,
            };
        }

        public static TextBox ReadOnlyBox(string mono)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.M(9f),
            };
        }

        /// <summary>
        /// 只读日志框。
        ///
        /// 用 RichTextBox 而不是多行 TextBox：多行 TextBox 的滚动条是原生控件，
        /// 在深色主题下滚动块的滑块始终是白的（实测各种主题名都压不住，
        /// 只有 17px 宽的一条，但很显眼）。RichTextBox 可以用 ScrollBars.None
        /// 完全去掉系统滚动条 —— 它自带鼠标滚轮和键盘滚动，不影响使用。
        /// </summary>
        public static RichTextBox LogBox()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.None,
                WordWrap = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.FgMuted,
                BorderStyle = BorderStyle.None,
                Font = Theme.M(8.5f),
                DetectUrls = false,
                ShortcutsEnabled = true,
            };
        }

        public static void StyleCombo(ComboBox c)
        {
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.BackColor = Theme.BgInput;
            c.ForeColor = Theme.Fg;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Theme.F(9.5f);
            c.MaxDropDownItems = 22;
            /* 版本说明文字比控件宽，不设这两项展开列表里会被截断 */
            c.DropDownWidth = 520;
            c.IntegralHeight = false;
            c.DropDownHeight = 340;
        }

        /// <summary>
        /// 统一下拉框外观。用自绘的 FlatSelect 而不是原生 ComboBox：
        /// 原生 ComboBox 的边框与下拉按钮由系统绘制，是纯白的
        /// （实测 RGB 255,255,255），BackColor / FlatStyle / SetWindowTheme
        /// 都改不动它，在深色界面上就是一圈刺眼的白框。
        /// </summary>
        public static void StyleSelect(FlatSelect c)
        {
            c.BackColor = Theme.BgInput;
            c.ForeColor = Theme.Fg;
            c.Font = Theme.F(9.5f);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "1") + " " + units[i];
        }

        public static void AppendLog(RichTextBox box, string line)
        {
            box.AppendText(line + Environment.NewLine);
            try
            {
                box.SelectionStart = box.TextLength;
                box.ScrollToCaret();
            }
            catch { }
        }
    }

    /* ================================================================== */
    /* 带 API Key 的多行输入（命令模式之类用到，暂时留空实现）                 */
    /* ================================================================== */
}
