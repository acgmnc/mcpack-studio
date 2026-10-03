using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* 精简 PNG 编码器（不依赖 System.Drawing 的 Save，避免 GDI+ 的坑）      */
    /* ================================================================== */

    /// <summary>
    /// 手写 PNG 编码：8 位 RGBA、无隔行、单 IDAT。
    /// 用 .NET 的 DeflateStream 生成 zlib 流（PNG 的 IDAT 就是 zlib 格式）。
    /// </summary>
    internal static class PngWriter
    {
        /// <summary>把 RGBA 像素写成 PNG 字节</summary>
        public static byte[] Encode(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("尺寸必须为正");
            if (rgba == null || rgba.Length < width * height * 4)
                throw new ArgumentException("像素数据长度不足");

            byte[] raw = new byte[height * (1 + width * 4)];
            int p = 0;
            for (int y = 0; y < height; y++)
            {
                raw[p++] = 0;                       // 每行前缀：filter type 0 (None)
                Buffer.BlockCopy(rgba, y * width * 4, raw, p, width * 4);
                p += width * 4;
            }

            byte[] idat = ZlibCompress(raw);

            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                WriteChunk(ms, "IHDR", Ihdr(width, height));
                WriteChunk(ms, "IDAT", idat);
                WriteChunk(ms, "IEND", new byte[0]);

                return ms.ToArray();
            }
        }

        private static byte[] Ihdr(int w, int h)
        {
            var b = new byte[13];
            WriteBE(b, 0, w);
            WriteBE(b, 4, h);
            b[8] = 8;    // bit depth
            b[9] = 6;    // color type: RGBA
            b[10] = 0;   // compression
            b[11] = 0;   // filter
            b[12] = 0;   // interlace
            return b;
        }

        private static void WriteBE(byte[] b, int offset, int value)
        {
            b[offset] = (byte)((value >> 24) & 0xFF);
            b[offset + 1] = (byte)((value >> 16) & 0xFF);
            b[offset + 2] = (byte)((value >> 8) & 0xFF);
            b[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBE(len, 0, data.Length);
            s.Write(len, 0, 4);

            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);

            uint crc = Crc32(typeBytes, data);
            var crcBytes = new byte[4];
            WriteBE(crcBytes, 0, unchecked((int)crc));
            s.Write(crcBytes, 0, 4);
        }

        /* ---- zlib（2 字节头 + raw deflate + adler32）---- */

        private static byte[] ZlibCompress(byte[] data)
        {
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var ds = new System.IO.Compression.DeflateStream(
                           ms, System.IO.Compression.CompressionMode.Compress, true))
                {
                    ds.Write(data, 0, data.Length);
                }
                deflated = ms.ToArray();
            }

            using (var outMs = new MemoryStream())
            {
                outMs.WriteByte(0x78);   // CMF: deflate, 32K window
                outMs.WriteByte(0x9C);   // FLG: 默认压缩级别
                outMs.Write(deflated, 0, deflated.Length);

                uint adler = Adler32(data);
                outMs.WriteByte((byte)((adler >> 24) & 0xFF));
                outMs.WriteByte((byte)((adler >> 16) & 0xFF));
                outMs.WriteByte((byte)((adler >> 8) & 0xFF));
                outMs.WriteByte((byte)(adler & 0xFF));

                return outMs.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            const uint MOD = 65521;
            uint a = 1, b = 0;
            foreach (byte t in data)
            {
                a = (a + t) % MOD;
                b = (b + a) % MOD;
            }
            return (b << 16) | a;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc32(byte[] a, byte[] b)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte t in a) c = CrcTable[(c ^ t) & 0xFF] ^ (c >> 8);
            foreach (byte t in b) c = CrcTable[(c ^ t) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }
    }

    /* ================================================================== */
    /* 图像生成                                                            */
    /* ================================================================== */

    internal enum IconPattern
    {
        Solid,          // 纯色
        Transparent,    // 全透明
        Grid,           // 网格（占位方块贴图）
        Checker,        // 棋盘格
        Cross,          // 十字（常见的纹理占位）
        PackIcon,       // 资源包图标：圆角底 + 字形
    }

    internal static class IconMaker
    {
        /// <summary>生成一张图片的 PNG 字节</summary>
        public static byte[] Make(int size, IconPattern pattern, Color color, Color color2,
                                  int gridStep, string text)
        {
            if (size < 1) size = 1;
            if (size > 4096) size = 4096;

            var px = new byte[size * size * 4];

            switch (pattern)
            {
                case IconPattern.Transparent:
                    break;   // 全 0 就是全透明

                case IconPattern.Solid:
                    Fill(px, size, color, 255);
                    break;

                case IconPattern.Checker:
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            bool alt = (((x / Math.Max(1, gridStep)) + (y / Math.Max(1, gridStep))) % 2) == 0;
                            Set(px, size, x, y, alt ? color : color2, 255);
                        }
                    break;

                case IconPattern.Grid:
                    Fill(px, size, color, 255);
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            bool onLine = (x % Math.Max(1, gridStep) == 0) || (y % Math.Max(1, gridStep) == 0);
                            if (onLine) Set(px, size, x, y, color2, 255);
                        }
                    break;

                case IconPattern.Cross:
                    Fill(px, size, color, 255);
                    {
                        int t = Math.Max(1, size / 8);
                        int mid = size / 2;
                        for (int y = 0; y < size; y++)
                            for (int x = 0; x < size; x++)
                            {
                                bool v = Math.Abs(x - mid) < t;
                                bool h = Math.Abs(y - mid) < t;
                                if (v || h) Set(px, size, x, y, color2, 255);
                            }
                    }
                    break;

                case IconPattern.PackIcon:
                    {
                        // 圆角矩形底 + 一条横带，模拟 Minecraft 资源包图标
                        int radius = Math.Max(2, size / 8);
                        Fill(px, size, color2, 255);
                        for (int y = 0; y < size; y++)
                            for (int x = 0; x < size; x++)
                            {
                                if (InsideRounded(x, y, size, radius))
                                    Set(px, size, x, y, color, 255);
                            }

                        int bandY = size / 3;
                        int bandH = Math.Max(1, size / 6);
                        for (int y = bandY; y < bandY + bandH && y < size; y++)
                            for (int x = radius; x < size - radius; x++)
                                Set(px, size, x, y, color2, 255);
                        break;
                    }
            }

            return PngWriter.Encode(size, size, px);
        }

        private static bool InsideRounded(int x, int y, int size, int r)
        {
            int cx = x < r ? r : (x >= size - r ? size - r - 1 : x);
            int cy = y < r ? r : (y >= size - r ? size - r - 1 : y);
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        private static void Fill(byte[] px, int size, Color c, byte a)
        {
            for (int i = 0; i < size * size; i++)
            {
                px[i * 4] = c.R; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.B; px[i * 4 + 3] = a;
            }
        }

        private static void Set(byte[] px, int size, int x, int y, Color c, byte a)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            int i = (y * size + x) * 4;
            px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = a;
        }

        /// <summary>把任意 PNG/JPEG 缩放到指定尺寸并转成 PNG 字节（用于导入现有图片当图标）</summary>
        public static byte[] ResizeToPng(string imagePath, int size, out string error)
        {
            error = null;
            try
            {
                using (var src = Image.FromFile(imagePath))
                using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(src, 0, 0, size, size);
                    }

                    var px = new byte[size * size * 4];
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            var c = bmp.GetPixel(x, y);
                            int i = (y * size + x) * 4;
                            px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = c.A;
                        }
                    return PngWriter.Encode(size, size, px);
                }
            }
            catch (Exception e)
            {
                error = "读取图片失败：" + e.Message;
                return null;
            }
        }

        /// <summary>读取图片基本信息（给界面显示用）</summary>
        public static string Describe(string imagePath)
        {
            try
            {
                using (var src = Image.FromFile(imagePath))
                    return src.Width + "×" + src.Height + "  " + src.PixelFormat;
            }
            catch (Exception e)
            {
                return "无法读取：" + e.Message;
            }
        }
    }

    /* ================================================================== */
    /* 新建资源包                                                          */
    /* ================================================================== */

    internal sealed class ScaffoldOptions
    {
        public string OutputDir;
        public int PackFormat = 15;
        public string Description = "My Resource Pack";
        public string Namespace = "minecraft";

        /// <summary>要预置哪些子目录（相对 assets/&lt;ns&gt;/ 或根）</summary>
        public bool WithTextures = true;
        public bool WithBlockTextures = true;
        public bool WithItemTextures = true;
        public bool WithModels = true;
        public bool WithLang = true;
        public bool WithSounds = true;
        /// <summary>1.21.4+ 的物品定义目录（items/）</summary>
        public bool WithItems;

        public bool WithPackIcon = true;
        public Color IconColor = Color.FromArgb(88, 132, 255);

        /// <summary>示例文件，方便用户直接看到目录用途</summary>
        public bool WithSamples = true;
    }

    internal static class PackScaffold
    {
        /// <summary>pack_format 达到这个值起，物品定义从 models/item 迁到 items/</summary>
        public const int ItemsDirBoundary = 46;

        /// <summary>1.13「扁平化」边界：低于它用 textures/blocks，达到它用 textures/block</summary>
        public const int FlattenBoundary = 4;

        public static bool UsesItemsDir(int packFormat)
        {
            return packFormat >= ItemsDirBoundary;
        }

        public static string BlockTextureDir(int packFormat)
        {
            return packFormat >= FlattenBoundary ? "block" : "blocks";
        }

        public static string ItemTextureDir(int packFormat)
        {
            return packFormat >= FlattenBoundary ? "item" : "items";
        }

        /// <summary>创建资源包骨架，返回创建的文件数</summary>
        public static WriteReport Create(ScaffoldOptions o, Action<string> note)
        {
            return StagedWriter.Write(
                o.OutputDir,
                StageOutput.Folder,
                staging =>
                {
                    string ns = string.IsNullOrWhiteSpace(o.Namespace) ? "minecraft" : o.Namespace.Trim();
                    string assets = Path.Combine(staging, "assets", ns);

                    /* ---- pack.mcmeta ---- */
                    var root = JsonValue.Obj();
                    var pack = JsonValue.Obj();
                    var order = new List<KeyValuePair<string, JsonValue>>();

                    if (o.PackFormat >= ItemsDirBoundary)
                    {
                        /* 新版：同时写 pack_format 与 supported_formats 范围，
                           让同一个包在多个版本上都能用 */
                        pack.Set("pack_format", JsonValue.Num(o.PackFormat));
                        pack.Set("description", JsonValue.Str(o.Description));
                    }
                    else
                    {
                        pack.Set("pack_format", JsonValue.Num(o.PackFormat));
                        pack.Set("description", JsonValue.Str(o.Description));
                    }
                    root.Set("pack", pack);

                    StagedWriter.WriteText(staging, "pack.mcmeta", Json.Write(root, true) + "\n");

                    /* ---- 目录骨架 ---- */
                    var dirs = new List<string>();

                    if (o.WithTextures)
                    {
                        if (o.WithBlockTextures)
                            dirs.Add("assets/" + ns + "/textures/" + BlockTextureDir(o.PackFormat));
                        if (o.WithItemTextures)
                            dirs.Add("assets/" + ns + "/textures/" + ItemTextureDir(o.PackFormat));
                        dirs.Add("assets/" + ns + "/textures/gui");
                        dirs.Add("assets/" + ns + "/textures/entity");
                    }
                    if (o.WithModels)
                    {
                        dirs.Add("assets/" + ns + "/models/block");
                        dirs.Add("assets/" + ns + "/models/item");
                    }
                    if (o.WithItems || UsesItemsDir(o.PackFormat))
                        dirs.Add("assets/" + ns + "/items");
                    if (o.WithLang)
                        dirs.Add("assets/" + ns + "/lang");
                    if (o.WithSounds)
                        dirs.Add("assets/" + ns + "/sounds");

                    /* 目录本身写一个 .gitkeep，否则空目录提交不进版本库，
                       也让用户在文件管理器里能立刻看到结构 */
                    foreach (string d in dirs)
                    {
                        StagedWriter.WriteText(staging, d + "/.gitkeep", "");
                        if (note != null) note("建立目录 " + d);
                    }

                    /* ---- 默认图标 pack.png ---- */
                    if (o.WithPackIcon)
                    {
                        byte[] png = IconMaker.Make(64, IconPattern.PackIcon,
                            o.IconColor, Color.FromArgb(24, 28, 38), 16, null);
                        StagedWriter.WriteBytes(staging, "pack.png", png);
                        if (note != null) note("生成 pack.png（64×64）");
                    }

                    /* ---- 示例文件 ---- */
                    if (o.WithSamples)
                    {
                        if (o.WithLang)
                        {
                            StagedWriter.WriteText(staging, "assets/" + ns + "/lang/en_us.json",
                                "{\n  \"pack.name\": \"" + Escape(o.Description) + "\"\n}\n");
                            StagedWriter.WriteText(staging, "assets/" + ns + "/lang/zh_cn.json",
                                "{\n  \"pack.name\": \"" + Escape(o.Description) + "\"\n}\n");
                        }
                        if (o.WithSounds)
                        {
                            StagedWriter.WriteText(staging, "assets/" + ns + "/sounds.json",
                                "{\n}\n");
                        }
                        if (o.WithBlockTextures)
                        {
                            byte[] tex = IconMaker.Make(16, IconPattern.Grid,
                                Color.FromArgb(150, 150, 150), Color.FromArgb(110, 110, 110), 16, null);
                            StagedWriter.WriteBytes(staging,
                                "assets/" + ns + "/textures/" + BlockTextureDir(o.PackFormat) + "/example.png", tex);
                        }
                    }
                },
                note);
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
