using System;
using System.Drawing;

namespace McPackStudio
{
    /// <summary>统一配色与字体，改一处全界面生效</summary>
    internal static class Theme
    {
        /* 背景层次 */
        public static readonly Color BgVoid = Color.FromArgb(11, 14, 20);
        public static readonly Color BgBase = Color.FromArgb(17, 20, 28);
        public static readonly Color BgPanel = Color.FromArgb(22, 26, 36);
        public static readonly Color BgPanelAlt = Color.FromArgb(26, 31, 43);
        public static readonly Color BgInput = Color.FromArgb(13, 16, 23);
        public static readonly Color BgHover = Color.FromArgb(33, 39, 54);
        public static readonly Color BgActive = Color.FromArgb(40, 48, 68);

        /* 描边 */
        public static readonly Color Line = Color.FromArgb(40, 47, 64);
        public static readonly Color LineSoft = Color.FromArgb(30, 36, 49);
        public static readonly Color LineStrong = Color.FromArgb(58, 68, 90);

        /* 文字 */
        public static readonly Color Fg = Color.FromArgb(234, 238, 248);
        public static readonly Color FgMuted = Color.FromArgb(158, 170, 194);
        public static readonly Color FgDim = Color.FromArgb(124, 136, 162);
        public static readonly Color FgFaint = Color.FromArgb(94, 105, 128);

        /* 强调色 */
        public static readonly Color Accent = Color.FromArgb(88, 132, 255);
        public static readonly Color AccentHover = Color.FromArgb(112, 152, 255);
        public static readonly Color AccentSoft = Color.FromArgb(32, 44, 74);
        public static readonly Color Accent2 = Color.FromArgb(158, 108, 255);

        /* 语义色 */
        public static readonly Color Ok = Color.FromArgb(88, 214, 154);
        public static readonly Color Warn = Color.FromArgb(255, 186, 96);
        public static readonly Color Danger = Color.FromArgb(255, 118, 132);
        public static readonly Color Info = Color.FromArgb(108, 196, 255);

        /* 动作色（预览表格用） */
        public static readonly Color ActCopy = Color.FromArgb(140, 152, 176);
        public static readonly Color ActRename = Color.FromArgb(255, 196, 108);
        public static readonly Color ActRewrite = Color.FromArgb(122, 196, 255);
        public static readonly Color ActExclude = Color.FromArgb(255, 130, 145);

        /* 字体 */
        public static string UiFont = "Microsoft YaHei UI";
        public static string MonoFont = "Consolas";

        public static Font F(float size) { return new Font(UiFont, size); }
        public static Font F(float size, FontStyle style) { return new Font(UiFont, size, style); }
        public static Font M(float size) { return new Font(MonoFont, size); }

        /// <summary>按背景亮度选可读的前景色</summary>
        public static Color Readable(Color bg)
        {
            double lum = (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000.0;
            return lum > 150 ? Color.FromArgb(32, 36, 46) : Color.FromArgb(235, 240, 250);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
