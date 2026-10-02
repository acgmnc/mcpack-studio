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

        public static TextBox LogBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.FgMuted,
                BorderStyle = BorderStyle.None,
                Font = Theme.M(8.5f),
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

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "1") + " " + units[i];
        }

        public static void AppendLog(TextBox box, string line)
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
