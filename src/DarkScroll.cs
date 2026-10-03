using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 深色滚动条 + 滚动容器                                                */
    /* ================================================================== */

    /// <summary>
    /// 自绘的竖向滚动条。
    ///
    /// 原生滚动条（WS_VSCROLL）的滑块由系统按浅色主题绘制，
    /// 没有公开的颜色属性、不支持 OwnerDraw，SetWindowTheme("DarkMode_Explorer")
    /// 对它也无效 —— 在深色界面里就是一块刺眼的白。
    /// 所以这里自己画一条，并接管拖动与点击。
    /// </summary>
    internal sealed class DarkScrollBar : Control
    {
        private int _value;
        private int _maximum = 1;        // 可滚动范围（内容高 - 视口高）
        private int _viewport = 1;       // 视口高
        private int _content = 1;        // 内容高

        private bool _dragging;
        private int _dragOffset;
        private bool _hoverThumb;
        private bool _hoverTrack;

        public event EventHandler ValueChanged;

        public DarkScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgVoid;
            Width = 11;
            Visible = false;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Clamp(value, 0, _maximum);
                if (v == _value) return;
                _value = v;
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        /// <summary>用内容高与视口高重算滚动范围；返回是否需要显示</summary>
        public bool Update(int contentHeight, int viewportHeight)
        {
            _content = Math.Max(0, contentHeight);
            _viewport = Math.Max(1, viewportHeight);

            int max = Math.Max(0, _content - _viewport);
            bool changed = max != _maximum;
            _maximum = Math.Max(1, max);

            bool need = max > 0;
            if (need != Visible) Visible = need;
            if (_value > max) { _value = max; changed = true; }

            if (changed) Invalidate();
            return need;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>滑块的位置与高度</summary>
        private Rectangle ThumbRect()
        {
            int track = Height;
            if (track <= 0) return Rectangle.Empty;

            double ratio = (double)_viewport / Math.Max(1, _content);
            int h = (int)(track * ratio);
            int minH = 28;
            if (h < minH) h = minH;
            if (h > track) h = track;

            int travel = track - h;
            int y = _maximum <= 0 ? 0 : (int)((double)_value / _maximum * travel);

            return new Rectangle(2, y, Math.Max(1, Width - 4), h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var thumb = ThumbRect();

            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragOffset = e.Y - thumb.Y;
            }
            else
            {
                /* 点空白处：翻一页 */
                int delta = e.Y < thumb.Y ? -_viewport : _viewport;
                Value = _value + delta;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var thumb = ThumbRect();
            bool overThumb = thumb.Contains(e.Location);
            bool overTrack = !overThumb && ClientRectangle.Contains(e.Location);

            if (overThumb != _hoverThumb || overTrack != _hoverTrack)
            {
                _hoverThumb = overThumb;
                _hoverTrack = overTrack;
                Invalidate();
            }

            if (_dragging)
            {
                int track = Height;
                int h = thumb.Height;
                int travel = track - h;
                if (travel > 0)
                {
                    int y = Clamp(e.Y - _dragOffset, 0, travel);
                    int v = (int)Math.Round((double)y / travel * _maximum);
                    Value = v;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _dragging = false;
            _hoverThumb = false;
            _hoverTrack = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BgVoid);

            /* 轨道 */
            using (var b = new SolidBrush(_hoverTrack ? Theme.BgPanelAlt : Theme.BgPanel))
                g.FillRectangle(b, ClientRectangle);

            var thumb = ThumbRect();
            if (thumb.Height <= 0) return;

            Color fill = _hoverThumb || _dragging ? Theme.LineStrong : Theme.Line;
            using (var path = Card.Rounded(thumb, 4))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, path);
        }
    }

    /* ================================================================== */
    /* 滚动容器                                                            */
    /* ================================================================== */

    /// <summary>
    /// 带深色滚动条的容器。
    ///
    /// 用法：把原来 <c>AutoScroll = true</c> 的 Panel 换成这个，
    /// 内容照样加到 <c>Content</c> 里（Dock/坐标都不用改）。
    ///
    /// 内部不用 AutoScroll —— 改为自己测量内容高度、自己移动 Content 的 Top，
    /// 这样滚动条完全由我们绘制，不会再有系统配色的白滑块。
    /// </summary>
    internal sealed class DarkScrollHost : Control
    {
        /// <summary>内容容器，把子控件加到这里</summary>
        public readonly Panel Content;

        private readonly DarkScrollBar _bar;
        private int _scroll;

        public DarkScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgBase;

            Content = new Panel
            {
                Location = new Point(0, 0),
                BackColor = Theme.BgBase,
            };
            Controls.Add(Content);

            _bar = new DarkScrollBar();
            _bar.ValueChanged += (s, e) => ApplyScroll(_bar.Value);
            Controls.Add(_bar);

            Content.ControlAdded += (s, e) => Relayout();
            Content.ControlRemoved += (s, e) => Relayout();
            Content.Resize += (s, e) => Relayout();
            Resize += (s, e) => Relayout();
        }

        /// <summary>
        /// 内容区留白。名字故意和 Panel.Padding 一致并转发给 Content，
        /// 这样原来用 <c>new Panel { AutoScroll = true, Padding = ... }</c> 的地方
        /// 只需换掉构造，其余代码一行都不用改。
        /// </summary>
        public new Padding Padding
        {
            get { return Content.Padding; }
            set
            {
                Content.Padding = value;
                Relayout();
            }
        }

        private int ViewportWidth
        {
            get { return Math.Max(0, ClientSize.Width - (_bar.Visible ? _bar.Width : 0)); }
        }

        public void Relayout()
        {
            SuspendLayout();

            int vw = ViewportWidth;

            /* 先把内容宽度定下来，子控件的换行/测量才准 */
            Content.Width = vw;

            /* 内容高 = 所有子控件的最低点 + 上下内边距 */
            int bottom = 0;
            foreach (Control c in Content.Controls)
            {
                int b = c.Bottom;
                if (b > bottom) bottom = b;
            }
            int contentH = bottom + Content.Padding.Bottom;
            if (contentH < ClientSize.Height) contentH = ClientSize.Height;

            Content.Height = contentH;

            _bar.Bounds = new Rectangle(vw, 0, _bar.Width, ClientSize.Height);
            _bar.BringToFront();

            _bar.Update(contentH, ClientSize.Height);

            /* 隐藏滚动条后视口变宽，要再算一次宽度 */
            if (Content.Width != ViewportWidth) Content.Width = ViewportWidth;

            ResumeLayout();

            _scroll = _bar.Value;
            Content.Top = -_scroll;
        }

        private void ApplyScroll(int v)
        {
            _scroll = v;
            if (Content.Top != -v) Content.Top = -v;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_bar.Visible)
            {
                int step = SystemInformation.MouseWheelScrollLines;
                if (step <= 0) step = 3;
                _bar.Value = _bar.Value - Math.Sign(e.Delta) * step * 24;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Focus();
            base.OnMouseEnter(e);
        }
    }

    /* ================================================================== */
    /* 让滚轮在任何子控件上都能滚动父容器                                     */
    /* ================================================================== */

    /// <summary>
    /// 全局消息过滤：鼠标停在子控件（列表、文本框）上时，
    /// 滚轮默认发给那个子控件，父滚动容器收不到。
    /// 这里把没被消耗的滚轮消息转给最近的 DarkScrollHost 祖先。
    /// </summary>
    internal sealed class WheelRouter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;

            var c = Control.FromHandle(m.HWnd);
            if (c == null) return false;

            /* 子控件自己能滚就让它自己处理（列表、多行文本框） */
            if (ScrollsItself(c)) return false;

            var host = FindHost(c);
            if (host == null) return false;

            int delta = (short)((long)m.WParam >> 16);
            int step = SystemInformation.MouseWheelScrollLines;
            if (step <= 0) step = 3;

            var bar = host.Controls[host.Controls.Count - 1] as DarkScrollBar;
            if (bar == null || !bar.Visible) return false;

            bar.Value = bar.Value - Math.Sign(delta) * step * 24;
            return true;
        }

        private static bool ScrollsItself(Control c)
        {
            for (var p = c; p != null; p = p.Parent)
            {
                if (p is TextBoxBase) return true;
                if (p is ListView) return true;
                if (p is TreeView) return true;
                if (p is DataGridView) return true;
                if (p is DarkScrollHost) return false;
            }
            return false;
        }

        private static DarkScrollHost FindHost(Control c)
        {
            for (var p = c; p != null; p = p.Parent)
            {
                var h = p as DarkScrollHost;
                if (h != null) return h;
            }
            return null;
        }
    }
}
