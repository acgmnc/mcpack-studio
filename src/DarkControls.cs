using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 深色选项卡                                                          */
    /* ================================================================== */

    /// <summary>
    /// 深色选项卡容器，替代原生 TabControl。
    ///
    /// 原生 TabControl 的页眉由系统绘制，最后一个页签右边那片空白
    /// 总是浅灰的 —— 它没有公开的颜色属性、不支持 OwnerDraw、
    /// 父容器上自绘也会被它自己的 WM_PAINT 盖回去，
    /// 试过挂父容器 Paint、DrawItem 里铺满、SetWindowTheme 三种办法都压不住。
    ///
    /// 所以干脆自己搭：上面一条标签栏，下面一个内容面板，
    /// 每个标签页自己的子控件放回 Content 里。
    ///
    /// 用法：
    ///     var tabs = new PageTabs();
    ///     tabs.AddPage("改动明细", previewPanel);
    ///     tabs.AddPage("转换报告", reportPanel);
    /// </summary>
    internal sealed class PageTabs : Control
    {
        private const int HeadH = 30;
        private const int Gap = 4;
        private const int MinW = 84;

        private readonly List<string> _titles = new List<string>();
        private readonly List<Control> _pages = new List<Control>();
        private readonly List<Rectangle> _rects = new List<Rectangle>();

        private int _index;
        private int _hover = -1;

        /// <summary>标签页内容区，往里加控件</summary>
        public readonly Panel Content;

        public event EventHandler SelectedIndexChanged;

        public PageTabs()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgPanel;

            Content = new Panel { BackColor = Theme.BgPanel };
            Controls.Add(Content);
        }

        public int TabCount { get { return _titles.Count; } }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (_titles.Count == 0) return;
                int v = value < 0 ? 0 : (value >= _titles.Count ? _titles.Count - 1 : value);
                if (v == _index) { SyncVisible(); return; }
                _index = v;
                SyncVisible();
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void AddPage(string title, Control page)
        {
            if (page == null) return;

            _titles.Add(title ?? "");
            _pages.Add(page);

            page.Visible = _titles.Count - 1 == _index;
            Content.Controls.Add(page);

            Recalc();
            SyncVisible();
            Invalidate();
        }

        private void SyncVisible()
        {
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].Visible = i == _index;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            Recalc();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalc();
        }

        private void Recalc()
        {
            if (Content != null)
                Content.Bounds = new Rectangle(0, HeadH, Width, Math.Max(0, Height - HeadH));

            _rects.Clear();
            int x = 0;
            foreach (string t in _titles)
            {
                int w = TextRenderer.MeasureText(t, Theme.F(9f, FontStyle.Bold)).Width + 34;
                if (w < MinW) w = MinW;
                _rects.Add(new Rectangle(x, 0, w, HeadH - 2));
                x += w + Gap;
            }

            foreach (Control p in _pages)
            {
                if (Content != null && p.Parent == Content)
                    p.Bounds = new Rectangle(0, 0, Content.ClientSize.Width, Content.ClientSize.Height);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit >= 0) SelectedIndex = hit;
            base.OnMouseClick(e);
        }

        private int HitTest(Point p)
        {
            if (p.Y >= HeadH) return -1;
            for (int i = 0; i < _rects.Count; i++)
                if (_rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BgPanel);

            for (int i = 0; i < _rects.Count; i++)
            {
                var r = _rects[i];
                bool sel = i == _index;
                bool hot = i == _hover && !sel;

                Color fill = sel ? Theme.BgInput : (hot ? Theme.BgHover : Theme.BgPanelAlt);
                Color border = sel ? Theme.Accent : (hot ? Theme.LineStrong : Theme.Line);
                Color fg = sel ? Theme.Fg : Theme.FgMuted;

                using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                using (var pen = new Pen(border, 1))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height);

                if (sel)
                {
                    using (var pen = new Pen(Theme.Accent, 2))
                        g.DrawLine(pen, r.X + 1, r.Y + 1, r.Right - 2, r.Y + 1);
                }

                TextRenderer.DrawText(g, _titles[i],
                    Theme.F(9f, sel ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(r.X + 4, r.Y, r.Width - 8, r.Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /* ================================================================== */
    /* 深色下拉选择框                                                       */
    /* ================================================================== */

    /// <summary>
    /// 深色下拉选择框，替代原生 ComboBox。
    ///
    /// 原生 ComboBox 的边框和下拉按钮由系统绘制，是纯白的
    /// （实测 RGB 255,255,255），在深色界面上是一圈刺眼的白框；
    /// 试过 BackColor、FlatStyle.Flat、SetWindowTheme("DarkMode_Explorer")、
    /// SetWindowTheme(" ") 四种办法都压不住 —— 那块区域根本不响应这些设置。
    /// 所以自己画：跟 FlatBtn 用同一套配色，展开列表也用自绘的。
    /// </summary>
    internal sealed class FlatSelect : Control
    {
        private readonly List<object> _items = new List<object>();
        private int _index = -1;
        private bool _hover;
        private bool _open;

        private ToolStripDropDown _drop;
        private ToolStripControlHost _host;
        private ListBox _list;

        public event EventHandler SelectedIndexChanged;

        public FlatSelect()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgInput;
            ForeColor = Theme.Fg;
            Font = Theme.F(9.5f);
            Height = 26;
            Cursor = Cursors.Hand;
        }

        /// <summary>展开列表的宽度；条目文字比控件宽时要设大一点</summary>
        public int DropDownWidth = 0;

        /// <summary>
        /// 条目集合。刻意做成 ComboBox 那套 API（Items.Add / Items.Count /
        /// Items[i] / Items.Clear），这样替换原生 ComboBox 时调用处几乎不用改。
        /// </summary>
        public sealed class ItemCollection
        {
            private readonly FlatSelect _owner;
            internal ItemCollection(FlatSelect owner) { _owner = owner; }

            public int Count { get { return _owner._items.Count; } }

            public object this[int i]
            {
                get { return _owner._items[i]; }
                set
                {
                    _owner._items[i] = value;
                    if (_owner._list != null && i < _owner._list.Items.Count)
                        _owner._list.Items[i] = Display(value);
                    _owner.Invalidate();
                }
            }

            public void Add(object item)
            {
                _owner._items.Add(item);
                if (_owner._list != null) _owner._list.Items.Add(Display(item));
                _owner.Invalidate();
            }

            public void AddRange(object[] items) { foreach (object o in items) Add(o); }

            public void Clear()
            {
                _owner._items.Clear();
                _owner._index = -1;
                if (_owner._list != null) _owner._list.Items.Clear();
                _owner.Invalidate();
            }

            public int IndexOf(object item) { return _owner._items.IndexOf(item); }
        }

        private ItemCollection _itemsView;

        public ItemCollection Items
        {
            get { return _itemsView ?? (_itemsView = new ItemCollection(this)); }
        }

        public int ItemCount { get { return _items.Count; } }

        /// <summary>当前选中项的显示文字（PackMergePage 用得到）</summary>
        public override string Text
        {
            get { return _index >= 0 && _index < _items.Count ? Display(_items[_index]) : ""; }
            set { }
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= _items.Count) v = _items.Count - 1;
                if (v == _index) return;
                _index = v;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _index >= 0 && _index < _items.Count ? _items[_index] : null; }
            set { SelectedIndex = _items.IndexOf(value); }
        }

        private static string Display(object o)
        {
            return o == null ? "" : o.ToString();
        }

        /* ------------------------------------------------------------ */

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowDropDown();
            base.OnMouseDown(e);
        }

        /* ---- 仅供 --select-test 使用，用来隔离开验证展开/选择逻辑 ---- */
        internal void OpenDropDownForTest() { ShowDropDown(); }
        internal void CloseDropDownForTest() { CloseDrop(); }
        internal void SetIndexForTest(int i) { SelectedIndex = i; CloseDrop(); }

        private void ShowDropDown()
        {
            if (_open || _items.Count == 0) return;
            _list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BgPanelAlt,
                ForeColor = Theme.Fg,
                Font = Theme.F(9.5f),
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 22,
            };
            foreach (object o in _items) _list.Items.Add(Display(o));
            if (_index >= 0) _list.SelectedIndex = _index;

            _list.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.BgPanelAlt))
                    e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, _list.Items[e.Index].ToString(), Theme.F(9.5f),
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                    sel ? Theme.Fg : Theme.FgMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            };

            _list.Click += (s, e) => Commit();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Commit();
                else if (e.KeyCode == Keys.Escape) CloseDrop();
            };

            int w = DropDownWidth > 0 ? DropDownWidth : Width;
            int h = Math.Min(340, _items.Count * 22 + 4);

            _host = new ToolStripControlHost(_list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = new Size(w, h) };
            _drop = new ToolStripDropDown
            {
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = false,
                DropShadowEnabled = true,
                BackColor = Theme.BgPanelAlt,
            };
            _drop.Items.Add(_host);
            _drop.Size = new Size(w, h);
            _drop.Closed += (s, e) => { _open = false; Invalidate(); };

            _open = true;
            _drop.Show(this, new Point(0, Height));
            _list.Focus();
        }

        private void Commit()
        {
            if (_list != null && _list.SelectedIndex >= 0) SelectedIndex = _list.SelectedIndex;
            CloseDrop();
        }

        private void CloseDrop()
        {
            if (_drop != null) { _drop.Close(); _drop = null; }
            _open = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Card.Rounded(r, 6))
            {
                using (var b = new SolidBrush(_hover || _open ? Theme.BgHover : Theme.BgInput))
                    g.FillPath(b, path);
                using (var pen = new Pen(_hover || _open ? Theme.Accent : Theme.LineStrong, 1f))
                    g.DrawPath(pen, path);
            }

            /* 下拉箭头：自己画，别用字形 */
            float cx = Width - 16;
            float cy = Height / 2f;
            using (var pen = new Pen(_hover || _open ? Theme.Accent : Theme.FgMuted, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(cx - 4, cy - 2),
                    new PointF(cx, cy + 2.5f),
                    new PointF(cx + 4, cy - 2),
                });
            }

            string text = _index >= 0 && _index < _items.Count ? Display(_items[_index]) : "";
            TextRenderer.DrawText(g, text, Font,
                new Rectangle(10, 0, Width - 30, Height),
                _index >= 0 ? Theme.Fg : Theme.FgFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /* ================================================================== */
    /* 深色进度条                                                          */
    /* ================================================================== */

    /// <summary>
    /// 深色进度条。原生 ProgressBar 未填充部分是系统浅灰，
    /// 在深色界面上一条白杠特别显眼，而且颜色没法设，只能自绘。
    /// </summary>
    internal sealed class FancyProgress : Control
    {
        private int _value;
        private int _max = 100;

        public FancyProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgInput;
            Height = 16;
            Width = 200;
        }

        public int Maximum
        {
            get { return _max; }
            set
            {
                int m = value < 1 ? 1 : value;
                if (m == _max) return;
                _max = m;
                if (InvokeRequired) { BeginInvoke(new Action(Invalidate)); return; }
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value < 0 ? 0 : (value > _max ? _max : value);
                if (v == _value) return;

                /* 转换有后台线程在推进度，必须切回 UI 线程再改 */
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<int>(SetValue), v);
                    return;
                }
                SetValue(v);
            }
        }

        private void SetValue(int v)
        {
            if (v == _value) return;
            _value = v;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Card.Rounded(r, Math.Max(2, Height / 2)))
            {
                using (var b = new SolidBrush(Theme.BgInput)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.LineStrong, 1)) g.DrawPath(pen, path);

                if (_value > 0)
                {
                    int w = (int)Math.Round((r.Width - 2) * (_value / (double)_max));
                    if (w > 0)
                    {
                        var fill = new Rectangle(1, 1, w, r.Height - 1);
                        var old = g.Clip;
                        using (var inner = Card.Rounded(r, Math.Max(2, Height / 2)))
                        {
                            g.SetClip(inner, CombineMode.Intersect);
                            using (var fb = new LinearGradientBrush(
                                new Rectangle(0, 0, Math.Max(2, r.Width), Math.Max(2, r.Height)),
                                Theme.Accent, Theme.Accent2, 0f))
                            {
                                g.FillRectangle(fb, fill);
                            }
                            g.Clip = old;
                        }
                    }
                }
            }
        }
    }
}
