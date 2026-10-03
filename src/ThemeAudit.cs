using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// 亮块自检：扫描界面截图里「明显比深色主题亮」的横条。
    ///
    /// 深色主题最怕漏掉原生控件的白底 —— TabControl 的页眉、
    /// ListView 的数据区、各种滚动条默认都是浅灰的，一漏就是一大条白杠。
    ///
    /// 判定条件要同时满足，否则会把浅色文字也算进来：
    ///   1. 够亮（亮度 &gt; 200）
    ///   2. 够「灰」（RGB 三通道接近，纯文字有抗锯齿彩边，浅灰底不会）
    ///   3. 横向连续成段（至少 40px）—— 文字笔画不会有这么长的连续段
    /// </summary>
    internal static class ThemeAudit
    {
        private const double LumaThreshold = 200.0;
        private const int NeutralSpread = 24;      // RGB 最大最小差
        private const int MinRun = 40;             // 连续亮灰像素长度
        private const int MinRowPixels = 60;       // 整行连续段总长
        private const int MinBandHeight = 4;

        public static string Scan(string dir)
        {
            var sb = new StringBuilder();
            if (!Directory.Exists(dir))
            {
                sb.AppendLine("目录不存在: " + dir);
                return sb.ToString();
            }

            var files = new List<string>(Directory.GetFiles(dir, "*.png"));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            int bad = 0;
            foreach (string f in files)
            {
                int total;
                var bands = ScanFile(f, out total);
                string name = Path.GetFileName(f);
                if (total == 0)
                {
                    sb.AppendLine("  OK   " + name + "   无白色控件区域");
                    continue;
                }

                bad++;
                sb.AppendLine("  !!   " + name + "   白像素 " + total);
                foreach (var b in bands)
                {
                    sb.AppendLine("         y=" + b.Y + ".." + (b.Y + b.Height - 1) +
                                  "  高=" + b.Height + "  白像素=" + b.Pixels);
                }
            }

            sb.AppendLine();
            sb.AppendLine(bad == 0
                ? "全部页面都没有漏白的原生控件。"
                : "有 " + bad + " 张截图存在漏白区域（上面标 !! 的）。");
            return sb.ToString();
        }

        private struct Band
        {
            public int Y;
            public int Height;
            public int Pixels;
        }

        private static List<Band> ScanFile(string path, out int total)
        {
            var bands = new List<Band>();
            total = 0;

            using (var src = new Bitmap(path))
            using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);

                int w = bmp.Width, h = bmp.Height;
                var rect = new Rectangle(0, 0, w, h);
                BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                try
                {
                    int stride = data.Stride;
                    var buf = new byte[stride * h];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                    var rows = new int[h];
                    for (int y = 0; y < h; y++)
                    {
                        int line = y * stride;
                        int run = 0;
                        int sum = 0;

                        for (int x = 0; x < w; x++)
                        {
                            int i = line + x * 4;
                            int b = buf[i], gg = buf[i + 1], r = buf[i + 2];

                            int mx = Math.Max(r, Math.Max(gg, b));
                            int mn = Math.Min(r, Math.Min(gg, b));
                            double lum = 0.114 * b + 0.587 * gg + 0.299 * r;

                            bool hit = lum > LumaThreshold && (mx - mn) <= NeutralSpread;
                            if (hit) run++;
                            else
                            {
                                if (run >= MinRun) sum += run;
                                run = 0;
                            }
                        }
                        if (run >= MinRun) sum += run;

                        /* 只统计「成段」的像素：文字的笔画不会有 40px 连续同色，
                           所以浅色文字不会被算进来，只有真正的白色控件区域会。 */
                        rows[y] = sum;
                        total += sum;
                    }

                    int start = -1;
                    for (int y = 0; y < h; y++)
                    {
                        if (rows[y] >= MinRowPixels)
                        {
                            if (start < 0) start = y;
                        }
                        else
                        {
                            if (start >= 0 && y - start >= MinBandHeight)
                            {
                                int sum = 0;
                                for (int k = start; k < y; k++) sum += rows[k];
                                bands.Add(new Band { Y = start, Height = y - start, Pixels = sum });
                            }
                            start = -1;
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }

            return bands;
        }
    }
}
