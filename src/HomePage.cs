using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 可点击的卡片基类                                                     */
    /* ================================================================== */

    internal class ClickCard : Panel
    {
        protected bool Hover;

        /// <summary>
        /// 画布可视范围（父面板坐标）。卡片整体重绘会连自身边界外的部分一起画出来，
        /// 如果卡片落在这个范围外就必须自己裁掉，否则会盖到状态栏上。
        /// </summary>
        public Rectangle ClipRectangle = Rectangle.Empty;

        public event EventHandler Activated;

        public ClickCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        /// <summary>按可视范围裁剪，再交给子类绘制</summary>
        protected Graphics Clipped(Graphics g)
        {
            if (!ClipRectangle.IsEmpty) g.SetClip(ClipRectangle);
            return g;
        }

        protected bool FullyClipped
        {
            get
            {
                return !ClipRectangle.IsEmpty &&
                       !ClipRectangle.IntersectsWith(new Rectangle(0, 0, Width, Height));
            }
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var h = Activated;
            if (h != null) h(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }

        protected void DrawSurface(Graphics g, int radius, bool accent)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Hover ? Theme.BgPanelAlt : Theme.BgPanel;
            Color border = Hover ? Theme.Accent : (accent ? Theme.LineStrong : Theme.Line);

            using (var path = Card.Rounded(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(border, Hover ? 1.5f : 1f)) g.DrawPath(pen, path);
            }
        }
    }

    /* ================================================================== */
    /* 主模块卡片                                                          */
    /* ================================================================== */

    internal sealed class ModuleCard : ClickCard
    {
        public string Glyph = "▣";
        public string ModuleTitle = "";
        public string ModuleDesc = "";
        public string ModuleMeta = "";
        public string EnterText = "进入";
        /// <summary>子模块标题，卡片下方以小标签形式展示</summary>
        public string[] SubNames = new string[0];

        private Rectangle _enterRect;
        private bool _enterHot;
        private int _shownChips = -1;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool inside = _enterRect.Contains(e.Location);
            if (inside != _enterHot)
            {
                _enterHot = inside;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (FullyClipped) { g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase); return; }
            Clipped(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);

            DrawSurface(g, 12, true);

            /* 图标块固定在左上角 */
            const int padX = 24;
            const int padY = 22;
            const int iconSize = 56;

            var iconRect = new Rectangle(padX, padY, iconSize, iconSize);
            using (var path = Card.Rounded(iconRect, 14))
            {
                using (var b = new SolidBrush(Hover ? Theme.AccentSoft : Theme.BgPanelAlt))
                    g.FillPath(b, path);
                using (var pen = new Pen(Hover ? Theme.Accent : Theme.LineStrong, 1f))
                    g.DrawPath(pen, path);
            }
            ModuleGlyphs.Draw(g, Glyph, iconRect, Hover ? Theme.Accent : Theme.Info);

            using (var fTitle = Theme.F(13f, FontStyle.Bold))
            using (var fDesc = Theme.F(9f))
            using (var fMeta = Theme.F(8.5f))
            using (var fChip = Theme.F(8.2f))
            {
                /* 「进入」按钮先摆好，正文的可用宽度要看它脸色 */
                int bw = TextRenderer.MeasureText(EnterText, fDesc).Width + 34;
                if (bw < 76) bw = 76;
                _enterRect = new Rectangle(Width - bw - padX, padY + 2, bw, 32);

                using (var path = Card.Rounded(_enterRect, 8))
                {
                    Color fill = Hover ? Theme.Accent : Theme.BgInput;
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var pen = new Pen(Hover ? Theme.Accent : Theme.LineStrong, 1f))
                        g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, EnterText, fDesc, _enterRect,
                    Hover ? Color.White : Theme.FgMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);

                /* 正文区：从图标右边开始，宽度让开按钮。
                   所有纵向位置都用 MeasureText 现算，写死坐标就会出现
                   文字互相盖住、或者顶出卡片被裁掉的情况。 */
                int textLeft = iconRect.Right + 18;
                int textW = _enterRect.Left - 16 - textLeft;
                if (textW < 120)
                {
                    /* 卡片太窄：按钮挪到底部，正文占满整行 */
                    textW = Width - textLeft - padX;
                    _enterRect = new Rectangle(Width - 96 - padX, Height - 42, 96, 30);
                }
                if (textW < 60) textW = 60;

                int y = padY;

                var szTitle = TextRenderer.MeasureText(g, ModuleTitle, fTitle,
                    new Size(textW, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int titleH = Math.Min(szTitle.Height, 48);
                TextRenderer.DrawText(g, ModuleTitle, fTitle,
                    new Rectangle(textLeft, y, textW, titleH), Theme.Fg,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                y += titleH + 8;

                /* 说明：可用高度按剩下的空间算，末尾留给标签区和元信息 */
                int metaH = TextRenderer.MeasureText(g, "国", fMeta).Height + 2;
                int chipRoom = SubNames.Length > 0 ? 28 : 0;
                int descMax = Height - y - metaH - chipRoom - 12;

                if (descMax > 14)
                {
                    var szDesc = TextRenderer.MeasureText(g, ModuleDesc, fDesc,
                        new Size(textW, descMax), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    int descH = Math.Min(szDesc.Height, descMax);
                    TextRenderer.DrawText(g, ModuleDesc, fDesc,
                        new Rectangle(textLeft, y, textW, descH), Theme.FgMuted,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += descH + 8;
                }

                /* 子模块标签：卡片高的时候这里能把余下的空间填满，
                   也顺便让「包含什么」一眼可见，不用读一长串顿号。 */
                if (SubNames.Length > 0 && y + 24 <= Height - metaH - 6)
                {
                    int chipH = TextRenderer.MeasureText(g, "国", fChip).Height + 8;
                    if (chipH < 22) chipH = 22;
                    _shownChips = DrawChips(g, fChip, textLeft, y, textW, chipH);
                    y += chipH + 6;
                }
                else
                {
                    _shownChips = -1;
                }

                if (y + metaH <= Height - 8)
                {
                    TextRenderer.DrawText(g, ModuleMeta, fMeta,
                        new Rectangle(textLeft, y, textW, metaH), Theme.FgFaint,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix |
                        TextFormatFlags.EndEllipsis);
                }
            }
        }

        /// <summary>
        /// 画子模块标签，放不下就在末尾换成「+N」。
        /// 返回实际显示出标题的个数（-1 表示整块没画）。
        /// </summary>
        private int DrawChips(Graphics g, Font f, int left, int top, int width, int height)
        {
            const int gapX = 6;
            int x = left;
            int shown = 0;

            for (int i = 0; i < SubNames.Length; i++)
            {
                int tw = TextRenderer.MeasureText(g, SubNames[i], f).Width + 18;
                if (tw > width) tw = width;

                /* 留出「+N」的位置 */
                int more = SubNames.Length - i - 1;
                int reserve = more > 0 ? TextRenderer.MeasureText(g, "+" + more, f).Width + 18 + gapX : 0;

                if (x + tw + reserve > left + width && shown > 0) break;

                var r = new Rectangle(x, top, tw, height);
                using (var path = Card.Rounded(r, height / 2))
                {
                    using (var b = new SolidBrush(Theme.BgPanelAlt)) g.FillPath(b, path);
                    using (var pen = new Pen(Theme.LineSoft, 1f)) g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, SubNames[i], f, r, Theme.FgDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                x += tw + gapX;
                shown++;

                if (x >= left + width) break;
            }

            int hidden = SubNames.Length - shown;
            if (hidden > 0 && shown > 0)
            {
                string more = "+" + hidden;
                int tw = TextRenderer.MeasureText(g, more, f).Width + 18;
                if (x + tw <= left + width)
                {
                    var r = new Rectangle(x, top, tw, height);
                    using (var path = Card.Rounded(r, height / 2))
                    using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, more, f, r, Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix);
                }
            }

            return shown;
        }
    }

    /* ================================================================== */
    /* 空位卡片（以后加主模块的位置）                                        */
    /* ================================================================== */

    internal sealed class EmptyCard : Panel
    {
        /// <summary>卡片文字。用 new 明确覆盖 Control.Text，避免编译警告</summary>
        public new string Text = "空位";
        public Rectangle ClipRectangle = Rectangle.Empty;

        public EmptyCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);

            if (!ClipRectangle.IsEmpty)
            {
                g.SetClip(ClipRectangle);
                if (!ClipRectangle.IntersectsWith(new Rectangle(0, 0, Width, Height))) return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Card.Rounded(r, 12))
            {
                using (var pen = new Pen(Theme.LineSoft, 1f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 5f, 5f };
                    g.DrawPath(pen, path);
                }
            }

            using (var f = Theme.F(9f))
            using (var b = new SolidBrush(Theme.FgFaint))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(Text, f, b, new Rectangle(0, 0, Width, Height), sf);
            }
        }
    }

    /* ================================================================== */
    /* 资源包内的操作卡片（进入资源包后显示）                                 */
    /* ================================================================== */

    internal sealed class PackActionCard : ClickCard
    {
        public string Glyph = "▣";
        public string CardTitle = "";
        public string CardDesc = "";
        public string SubActions;          // 以 | 分隔

        private readonly List<Rectangle> _subRects = new List<Rectangle>();
        private readonly List<string> _subList = new List<string>();
        private int _hotSub = -1;

        public event EventHandler<string> SubActivated;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hot = -1;
            for (int i = 0; i < _subRects.Count; i++)
            {
                if (_subRects[i].Contains(e.Location)) { hot = i; break; }
            }
            if (hot != _hotSub) { _hotSub = hot; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hotSub = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < _subRects.Count; i++)
            {
                if (_subRects[i].Contains(e.Location))
                {
                    var h = SubActivated;
                    if (h != null) h(this, _subList[i]);
                    base.OnMouseClick(e);
                    return;
                }
            }
            base.OnMouseClick(e);
        }

        private void RebuildSubs()
        {
            _subList.Clear();
            _subRects.Clear();
            if (string.IsNullOrEmpty(SubActions)) return;

            foreach (string part in SubActions.Split('|'))
            {
                string t = part.Trim();
                if (t.Length > 0) _subList.Add(t);
            }
            if (_subList.Count == 0) return;

            int y = Height - 42;
            int x = Width - 20;
            var rects = new List<Rectangle>();
            for (int i = _subList.Count - 1; i >= 0; i--)
            {
                int w = TextRenderer.MeasureText(_subList[i], Theme.F(8.5f)).Width + 22;
                x -= w;
                if (x < 16) break;
                rects.Insert(0, new Rectangle(x, y, w, 24));
                x -= 8;
            }
            int start = _subList.Count - rects.Count;
            if (start > 0) _subList.RemoveRange(0, start);
            _subRects.AddRange(rects);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildSubs();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (FullyClipped) { g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase); return; }
            Clipped(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.BgBase);

            if (_subRects.Count == 0 && !string.IsNullOrEmpty(SubActions)) RebuildSubs();

            DrawSurface(g, 10, false);

            bool hasSubs = _subRects.Count > 0;
            int bottomReserve = hasSubs ? 50 : 18;

            /* 内容块整体居中：卡片变高时不会全挤在顶上，变矮时也不会顶出下边界 */
            int iconSize = 46;
            int titleGap = 12;
            int titleH = TextRenderer.MeasureText(g, "国", Theme.F(11f, FontStyle.Bold)).Height + 6;
            int descGap = 8;

            int blockH = iconSize + titleGap + titleH + descGap;
            int top = (Height - bottomReserve - blockH) / 2;
            if (top < 16) top = 16;

            /* 图标 */
            var iconRect = new Rectangle(Width / 2 - iconSize / 2, top, iconSize, iconSize);
            using (var path = Card.Rounded(iconRect, 12))
            {
                using (var b = new SolidBrush(Hover ? Theme.AccentSoft : Theme.BgPanelAlt))
                    g.FillPath(b, path);
                using (var pen = new Pen(Hover ? Theme.Accent : Theme.LineStrong, 1f))
                    g.DrawPath(pen, path);
            }
            ModuleGlyphs.Draw(g, Glyph, iconRect, Hover ? Theme.Accent : Theme.Info);

            /* 标题 */
            var titleRect = new Rectangle(12, iconRect.Bottom + titleGap, Width - 24, titleH);
            TextRenderer.DrawText(g, CardTitle, Theme.F(11f, FontStyle.Bold), titleRect, Theme.Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            /* 说明 */
            int descTop = titleRect.Bottom + descGap;
            int descH = Height - descTop - bottomReserve;
            if (descH >= 16)
            {
                TextRenderer.DrawText(g, CardDesc, Theme.F(8.6f),
                    new Rectangle(16, descTop, Width - 32, descH), Theme.FgDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            /* 次级按钮 */
            for (int i = 0; i < _subRects.Count; i++)
            {
                bool hot = i == _hotSub;
                using (var path = Card.Rounded(_subRects[i], 5))
                {
                    using (var b = new SolidBrush(hot ? Theme.Accent : Theme.BgInput))
                        g.FillPath(b, path);
                    using (var pen = new Pen(hot ? Theme.Accent : Theme.LineStrong, 1f))
                        g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, _subList[i], Theme.F(8.5f), _subRects[i],
                    hot ? Color.White : Theme.FgMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /* ================================================================== */
    /* 矢量图标集                                                          */
    /* ================================================================== */

    internal static class ModuleGlyphs
    {
        public static void Draw(Graphics g, string kind, Rectangle box, Color color)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            float s = box.Width * 0.30f;

            using (var pen = new Pen(color, 2.0f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case "pack":       // 箱子 → 资源包
                        {
                            float w = s * 1.9f, h = s * 1.6f;
                            var r = new RectangleF(cx - w / 2, cy - h / 2, w, h);
                            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                            g.DrawLine(pen, r.Left, cy - h * 0.12f, r.Right, cy - h * 0.12f);
                            g.DrawLine(pen, cx, r.Top, cx, cy - h * 0.12f);
                            break;
                        }
                    case "tools":      // 扳手
                        {
                            g.DrawLine(pen, cx - s, cy + s, cx + s * 0.25f, cy - s * 0.45f);
                            g.DrawEllipse(pen, cx + s * 0.1f, cy - s * 1.05f, s * 0.95f, s * 0.95f);
                            break;
                        }
                    case "settings":   // 齿轮（简化为圆 + 辐条）
                        {
                            g.DrawEllipse(pen, cx - s * 0.75f, cy - s * 0.75f, s * 1.5f, s * 1.5f);
                            for (int i = 0; i < 6; i++)
                            {
                                double a = i * Math.PI / 3;
                                g.DrawLine(pen,
                                    cx + (float)Math.Cos(a) * s * 0.75f, cy + (float)Math.Sin(a) * s * 0.75f,
                                    cx + (float)Math.Cos(a) * s * 1.25f, cy + (float)Math.Sin(a) * s * 1.25f);
                            }
                            break;
                        }
                    case "nbt":        // 标签树：一个根节点往下分出两个子节点
                        {
                            float top = cy - s * 0.85f;
                            float bottom = cy + s * 0.85f;
                            g.DrawEllipse(pen, cx - 1.9f, top - 1.9f, 3.8f, 3.8f);
                            g.DrawLine(pen, cx, top + 1.9f, cx, cy);
                            g.DrawLine(pen, cx - s * 0.85f, cy, cx + s * 0.85f, cy);
                            g.DrawLine(pen, cx - s * 0.85f, cy, cx - s * 0.85f, bottom - 1.9f);
                            g.DrawLine(pen, cx + s * 0.85f, cy, cx + s * 0.85f, bottom - 1.9f);
                            g.DrawEllipse(pen, cx - s * 0.85f - 1.9f, bottom - 1.9f, 3.8f, 3.8f);
                            g.DrawEllipse(pen, cx + s * 0.85f - 1.9f, bottom - 1.9f, 3.8f, 3.8f);
                            break;
                        }
                    case "folder":
                        {
                            float w = s * 1.8f, h = s * 1.4f;
                            var r = new RectangleF(cx - w / 2, cy - h / 2 + 2, w, h);
                            g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
                            g.DrawLine(pen, r.Left, r.Top + 6, r.Left, r.Bottom);
                            g.DrawLine(pen, r.Right, r.Top + 6, r.Right, r.Bottom);
                            g.DrawLine(pen, r.Left, r.Top + 6, cx + 1, r.Top + 6);
                            g.DrawLine(pen, cx + 1, r.Top + 6, cx - 2, r.Top);
                            g.DrawLine(pen, cx - 2, r.Top, r.Left, r.Top);
                            g.DrawLine(pen, r.Left, r.Top, r.Left, r.Top + 6);
                            break;
                        }
                    case "download":
                        {
                            g.DrawLine(pen, cx, cy - s, cx, cy + s * 0.45f);
                            g.DrawLine(pen, cx - s * 0.55f, cy - s * 0.05f, cx, cy + s * 0.45f);
                            g.DrawLine(pen, cx + s * 0.55f, cy - s * 0.05f, cx, cy + s * 0.45f);
                            g.DrawLine(pen, cx - s, cy + s, cx + s, cy + s);
                            break;
                        }
                    case "convert":
                        {
                            g.DrawLine(pen, cx - s, cy - s * 0.4f, cx + s, cy - s * 0.4f);
                            g.DrawLine(pen, cx + s * 0.45f, cy - s * 0.9f, cx + s, cy - s * 0.4f);
                            g.DrawLine(pen, cx + s * 0.45f, cy + s * 0.1f, cx + s, cy - s * 0.4f);
                            g.DrawLine(pen, cx + s, cy + s * 0.4f, cx - s, cy + s * 0.4f);
                            g.DrawLine(pen, cx - s * 0.45f, cy - s * 0.1f, cx - s, cy + s * 0.4f);
                            g.DrawLine(pen, cx - s * 0.45f, cy + s * 0.9f, cx - s, cy + s * 0.4f);
                            break;
                        }
                    case "merge":
                        {
                            g.DrawEllipse(pen, cx - s, cy - s, s * 2, s * 2);
                            g.DrawLine(pen, cx - s * 0.5f, cy, cx + s * 0.5f, cy);
                            g.DrawLine(pen, cx, cy - s * 0.5f, cx, cy + s * 0.5f);
                            break;
                        }
                    case "browse":
                        {
                            for (int i = -1; i <= 1; i++)
                            {
                                float y = cy + i * s * 0.62f;
                                g.DrawEllipse(pen, cx - s, y - 1.2f, 2.4f, 2.4f);
                                g.DrawLine(pen, cx - s * 0.45f, y, cx + s, y);
                            }
                            break;
                        }
                    case "create":
                        {
                            g.DrawLine(pen, cx - s, cy + s, cx + s * 0.5f, cy - s * 0.5f);
                            g.DrawLine(pen, cx + s * 0.1f, cy - s * 0.9f, cx + s * 0.9f, cy - s * 0.1f);
                            break;
                        }
                    default:
                        {
                            g.DrawEllipse(pen, cx - s * 0.9f, cy - s * 0.9f, s * 1.8f, s * 1.8f);
                            break;
                        }
                }
            }

            g.SmoothingMode = old;
        }
    }

    /* ================================================================== */
    /* 主页：只列主模块                                                     */
    /* ================================================================== */

    internal sealed class HomePage
    {
        /// <summary>请求进入某个主模块（模块Id）</summary>
        public event EventHandler<string> ModuleRequested;
        public event EventHandler<string> StatusChanged;

        private Control _view;
        private Panel _canvas;
        private readonly List<ModuleCard> _moduleCards = new List<ModuleCard>();
        private readonly List<EmptyCard> _emptyCards = new List<EmptyCard>();

        private static void Status0(HomePage p, string t)
        {
            var h = p.StatusChanged;
            if (h != null) h(p, t);
        }

        public Control CreateView(List<MainModule> modules)
        {
            if (_view != null) return _view;

            /*
             * 就一个画布，卡片坐标在 Layout() 里算，从顶部开始排。
             *
             * 页面名显示在上面的子模块条里（ShellForm 负责），这里不再放标题，
             * 也就不需要 AutoScroll / Dock 嵌套 —— 之前几轮错位全是那两样引起的：
             * AutoScroll 在内容接近可视高度时会把内容滚上去，标题被滚出视野、
             * 卡片整体上移被裁掉。
             */
            _canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgBase,
            };
            _canvas.Resize += (s, e) => Layout(_canvas.ClientSize);

            BuildModuleCards(modules);

            _canvas.Paint += (s, e) =>
            {
                e.Graphics.Clear(Theme.BgBase);
                PaintHeader(e.Graphics);
            };

            _view = _canvas;
            return _canvas;
        }

        /// <summary>
        /// 按窗口宽度决定列数，只摆真正需要的「空位」卡片：
        /// 每行都填满（比如 3 个模块 + 3 个空位正好两排），
        /// 但绝不会因为有 6 个槽位就永远占着下面一整排空白。
        /// </summary>
        private void BuildModuleCards(List<MainModule> modules)
        {
            int cols = ColumnsFor(_canvas.ClientSize.Width);
            int slots = modules.Count;
            int rem = slots % cols;
            if (rem != 0) slots += cols - rem;      // 补齐到整排

            for (int i = 0; i < slots; i++)
            {
                if (i < modules.Count)
                {
                    var m = modules[i];
                    var card = new ModuleCard
                    {
                        Glyph = GlyphFor(m.Id),
                        ModuleTitle = m.Title,
                        ModuleDesc = ShortDesc(m),
                        SubNames = m.Subs.Select(s => s.Title).ToArray(),
                        ModuleMeta = m.Subs.Count + " 个子模块　·　" +
                                     m.Subs.Count(s => !(s.Page is PlannedPage)) + " 个已实现",
                        EnterText = "进入",
                    };
                    string id = m.Id;
                    card.Activated += (s, e) => OnModule(m, id);
                    _moduleCards.Add(card);
                    _canvas.Controls.Add(card);
                }
                else
                {
                    var empty = new EmptyCard
                    {
                        Text = i == modules.Count ? "空位　·　以后可以加新的主模块" : "空位",
                    };
                    _emptyCards.Add(empty);
                    _canvas.Controls.Add(empty);
                }
            }
        }

        private const int MinColW = 260;

        private static int ColumnsFor(int canvasWidth)
        {
            int w = canvasWidth - PadX * 2;
            if (w >= MinColW * 3 + Gap * 2) return 3;
            if (w >= MinColW * 2 + Gap) return 2;
            return 1;
        }

        private void OnModule(MainModule m, string id)
        {
            Status0(this, "进入「" + m.Title + "」");
            var h = ModuleRequested;
            if (h != null) h(this, id);
        }

        private static string GlyphFor(string moduleId)
        {
            if (moduleId == "pack") return "pack";
            if (moduleId == "tools") return "tools";
            if (moduleId == "settings") return "settings";
            if (moduleId == "nbt") return "nbt";
            return "default";
        }

        /// <summary>
        /// 卡片说明：只取 Hint 的第一句。
        /// 子模块清单交给下面的标签区展示，不再拼成一行顿号，读起来清爽些。
        /// </summary>
        private static string ShortDesc(MainModule m)
        {
            string d = m.Hint ?? "";
            int idx = d.IndexOf('，');
            if (idx < 0) idx = d.IndexOf('。');
            if (idx > 10) d = d.Substring(0, idx);
            return d.Trim();
        }

        /* ------------------------------------------------------------ */

        private const int Gap = 16;
        private const int PadX = 26;
        private const int PadTop = 18;
        private const int PadBottom = 18;
        private const int HeaderH = 62;          // 画布内的标题区（主页简介）

        private const int MinCardH = 132;
        private const int MaxCardH = 200;

        public void Layout(Size client)
        {
            if (_canvas == null) return;

            int w = _canvas.ClientSize.Width;
            int h = _canvas.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int cols = ColumnsFor(w);
            int padX = PadX;
            int avail = w - padX * 2;
            if (avail < 240) { padX = 12; avail = w - padX * 2; }
            if (avail < 80) avail = 80;
            int colW = (avail - Gap * (cols - 1)) / cols;

            int total = _moduleCards.Count + _emptyCards.Count;
            int rows = total == 0 ? 1 : (total + cols - 1) / cols;

            int areaTop = PadTop + HeaderH;
            int usable = h - areaTop - PadBottom;
            int cardH = (usable - Gap * (rows - 1)) / rows;
            if (cardH > MaxCardH) cardH = MaxCardH;
            if (cardH < MinCardH) cardH = MinCardH;

            /* 卡片整体垂直居中。下面原本留一大片空白，看着像内容没加载完；
               居中以后上下留白对称，窗口拉大也不会头重脚轻。
               排不下时贴着标题区往下排，多出来的部分由裁剪处理，不会溢到状态栏。 */
            int gridH = rows * cardH + Gap * (rows - 1);
            int gridTop = areaTop + (h - PadTop - PadBottom - gridH) / 2;
            if (gridTop < areaTop) gridTop = areaTop;

            /* 可视范围：卡片画到画布外面时会盖住状态栏，这里统一裁掉 */
            var clip = new Rectangle(0, 0, w, h);

            for (int i = 0; i < total; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var bounds = new Rectangle(
                    padX + col * (colW + Gap),
                    gridTop + row * (cardH + Gap),
                    colW, cardH);

                if (i < _moduleCards.Count) Place(_moduleCards[i], bounds, clip);
                else Place(_emptyCards[i - _moduleCards.Count], bounds, clip);
            }

            _cols = cols;
            _padX = padX;
            _gridTop = gridTop;
        }

        private int _cols = 3;
        private int _padX = PadX;
        private int _gridTop = PadTop + HeaderH;

        /// <summary>
        /// 画布内的标题区。以前这里什么都没有，内容区一片空白，
        /// 「主页」两个字孤零零挂在上面那条子模块条里，看着像没排版好。
        /// </summary>
        private void PaintHeader(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var fName = Theme.F(15f, FontStyle.Bold))
            using (var fSub = Theme.F(9f))
            {
                var szName = TextRenderer.MeasureText(g, Settings.AppName, fName);
                var szSub = TextRenderer.MeasureText(g, "国", fSub);

                TextRenderer.DrawText(g, Settings.AppName, fName,
                    new Rectangle(_padX, PadTop - 2, szName.Width + 6, szName.Height),
                    Theme.Fg, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);

                int subX = _padX + szName.Width + 14;
                int subY = PadTop - 2 + szName.Height - szSub.Height - 1;
                var szText = TextRenderer.MeasureText(g, Settings.AppTagline, fSub);
                if (subX + szText.Width < _canvas.ClientSize.Width - _padX)
                {
                    TextRenderer.DrawText(g, Settings.AppTagline, fSub,
                        new Rectangle(subX, subY, szText.Width + 4, szSub.Height),
                        Theme.FgDim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                }
            }

            using (var pen = new Pen(Theme.LineSoft, 1))
            {
                g.DrawLine(pen, _padX, _gridTop - 13, _canvas.ClientSize.Width - _padX, _gridTop - 13);
            }
        }

        private static void Place(ModuleCard c, Rectangle b, Rectangle clip)
        {
            c.ClipRectangle = clip;
            if (c.Bounds != b) c.Bounds = b;
            c.Invalidate();
        }

        private static void Place(EmptyCard c, Rectangle b, Rectangle clip)
        {
            c.ClipRectangle = clip;
            if (c.Bounds != b) c.Bounds = b;
            c.Invalidate();
        }
    }
}
