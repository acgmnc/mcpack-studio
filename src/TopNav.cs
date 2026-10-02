using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /// <summary>
    /// 顶部导航条：把主模块横向排开（替代原来的侧边栏），
    /// 左侧额外给一个「主页」入口。右侧显示当前位置。
    /// </summary>
    internal sealed class TopNav : Control
    {
        private const int PillH = 30;
        private const int PadX = 18;
        private const int Gap = 6;

        private List<MainModule> _modules = new List<MainModule>();
        private readonly List<Hit> _hits = new List<Hit>();
        private int _hover = -1;

        public string CurrentModuleId { get; private set; }
        public bool HomeSelected { get; private set; }

        public event EventHandler<string> ModuleClicked;   // null 表示主页
        public event EventHandler<string> SubClicked;      // 在模块上再点一次时回到落地页

        private sealed class Hit
        {
            public string ModuleId;      // null = 主页
            public Rectangle Rect;
        }

        public TopNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgPanel;
            Height = 50;
            Font = Theme.F(9f);
            HomeSelected = true;
        }

        public void SetModules(List<MainModule> modules)
        {
            _modules = modules ?? new List<MainModule>();
            Rebuild();
            Invalidate();
        }

        public void SelectModule(string moduleId)
        {
            HomeSelected = false;
            CurrentModuleId = moduleId;
            Invalidate();
        }

        public void SelectHome()
        {
            HomeSelected = true;
            Invalidate();
        }

        /* ------------------------------------------------------------ */

        private void Rebuild()
        {
            _hits.Clear();
            int x = PadX;
            int y = (Height - PillH) / 2;

            // 主页
            int w = WidthOf("主页") + 34;
            _hits.Add(new Hit { ModuleId = null, Rect = new Rectangle(x, y, w, PillH) });
            x += w + 14;   // 后面留出分隔线的位置

            foreach (var m in _modules)
            {
                int mw = WidthOf(m.Title) + 46;   // 图标 + 文字
                _hits.Add(new Hit { ModuleId = m.Id, Rect = new Rectangle(x, y, mw, PillH) });
                x += mw + Gap;
            }
        }

        private static int WidthOf(string text)
        {
            return TextRenderer.MeasureText(text, Theme.F(9.5f)).Width;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rebuild();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].Rect.Contains(e.Location)) { idx = i; break; }
            }
            if (idx != _hover) { _hover = idx; Invalidate(); }
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
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
            for (int i = 0; i < _hits.Count; i++)
            {
                if (!_hits[i].Rect.Contains(e.Location)) continue;

                var hit = _hits[i];
                if (hit.ModuleId == null)
                {
                    HomeSelected = true;
                    Invalidate();
                    var h0 = ModuleClicked;
                    if (h0 != null) h0(this, null);
                }
                else
                {
                    bool alreadyHere = !HomeSelected && hit.ModuleId == CurrentModuleId;
                    HomeSelected = false;
                    CurrentModuleId = hit.ModuleId;
                    Invalidate();

                    var h = ModuleClicked;
                    if (h != null) h(this, hit.ModuleId);

                    // 已经在这个模块里时再点一次 → 回到该模块的落地页
                    if (alreadyHere)
                    {
                        var h2 = SubClicked;
                        if (h2 != null) h2(this, hit.ModuleId);
                    }
                }
                break;
            }
            base.OnMouseClick(e);
        }

        /* ------------------------------------------------------------ */

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.BgPanel);

            // 底部分隔线
            using (var pen = new Pen(Theme.Line, 1))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                var hit = _hits[i];
                bool isHome = hit.ModuleId == null;
                bool active = isHome ? HomeSelected : (!HomeSelected && hit.ModuleId == CurrentModuleId);
                bool hot = i == _hover;

                Color fill, border, fg;
                if (active)
                {
                    fill = Theme.AccentSoft;
                    border = Theme.Accent;
                    fg = Theme.Fg;
                }
                else if (hot)
                {
                    fill = Theme.BgHover;
                    border = Theme.LineStrong;
                    fg = Theme.Fg;
                }
                else
                {
                    fill = Theme.BgPanelAlt;
                    border = Theme.Line;
                    fg = Theme.FgMuted;
                }

                using (var path = Card.Rounded(hit.Rect, 8))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var pen = new Pen(border, active ? 1.4f : 1f)) g.DrawPath(pen, path);
                }

                if (isHome)
                {
                    TextRenderer.DrawText(g, "⌂  主页", Theme.F(9.5f), hit.Rect, fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                else
                {
                    var m = _modules.FirstOrDefault(x => x.Id == hit.ModuleId);
                    if (m == null) continue;

                    // 图标块
                    var iconBox = new Rectangle(hit.Rect.X + 8, hit.Rect.Y + (PillH - 20) / 2, 20, 20);
                    using (var path = Card.Rounded(iconBox, 6))
                    {
                        using (var b = new SolidBrush(active ? Theme.AccentSoft : Theme.BgPanel))
                            g.FillPath(b, path);
                    }
                    ModuleGlyphs.Draw(g, GlyphFor(m.Id), iconBox, active ? Theme.Accent : Theme.FgDim);

                    TextRenderer.DrawText(g, m.Title, Theme.F(9.5f),
                        new Rectangle(iconBox.Right + 7, hit.Rect.Y, hit.Rect.Width - 34, PillH), fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                        TextFormatFlags.EndEllipsis);
                }

                // 「主页」和模块之间画一条竖分隔线
                if (isHome)
                {
                    int lx = hit.Rect.Right + 7;
                    using (var pen = new Pen(Theme.LineStrong, 1))
                    {
                        g.DrawLine(pen, lx, hit.Rect.Top + 4, lx, hit.Rect.Bottom - 4);
                    }
                }
            }
        }

        private static string GlyphFor(string moduleId)
        {
            if (moduleId == "pack") return "pack";
            if (moduleId == "tools") return "tools";
            if (moduleId == "settings") return "settings";
            return "default";
        }
    }
}
