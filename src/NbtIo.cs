using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace McPackStudio
{
    /* ================================================================== */
    /* 压缩方式 / 文档 / 异常                                              */
    /* ================================================================== */

    /// <summary>
    /// NBT 文件用到的压缩方式。Zlib 是「2 字节头 + 裸 deflate + 4 字节 Adler-32」，
    /// Deflate 是完全没有头的裸 deflate——它没有任何特征字节可以认，读的时候只能反过来试。
    /// </summary>
    internal enum NbtCompression
    {
        None,
        GZip,
        Zlib,
        Deflate,
    }

    /// <summary>
    /// 一个已载入（或准备保存）的 NBT 文件。字段全部公开，界面直接读写。
    /// </summary>
    internal sealed class NbtDocument
    {
        public string Path = "";
        public NbtCompound Root;
        public string RootName = "";
        public NbtCompression Compression = NbtCompression.None;
        public bool LittleEndian;
        public bool HasBedrockHeader;
        public int BedrockVersion = 9;
        public long FileBytes;
        public long PayloadBytes;

        /// <summary>
        /// 一行说明这份数据是怎么装的：gzip · 大端 / 无压缩 · 小端 · 基岩版头。
        /// 版本号不并进来，界面上另有一栏显示 BedrockVersion。
        /// </summary>
        public string SourceFormat()
        {
            var sb = new StringBuilder();
            sb.Append(NbtIo.DescribeCompression(Compression));
            sb.Append(LittleEndian ? " · 小端" : " · 大端");
            if (HasBedrockHeader) sb.Append(" · 基岩版头");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 解析时碰到的结构性错误（数据不够、长度越界、类型不认识……）。
    /// 继承 IOException，调用方只 catch IOException 就能同时接住压缩错误和格式错误。
    /// </summary>
    internal sealed class NbtFormatException : IOException
    {
        public NbtFormatException(string message) : base(message) { }
    }

    /* ================================================================== */
    /* NBT 二进制读写                                                      */
    /* 格式与容错策略参照 NBTExplorer / Substrate（MIT 许可）：             */
    /* 官方 NBT 是大端，基岩版改成了小端并加了 8 字节头，两种都得认。        */
    /* ================================================================== */

    internal static class NbtIo
    {
        /* ---------------- 常量 ---------------- */

        /// <summary>「读干净」允许的尾巴：有些工具会在末尾补几个字节</summary>
        private const int TrailingSlack = 4;

        /// <summary>基岩版 level.dat 的 8 字节头：[版本:4][负载长度:4]，都是小端</summary>
        private const int BedrockHeaderSize = 8;

        /// <summary>头上版本号的合理范围，超出的不当头看</summary>
        private const int MaxBedrockVersion = 20;

        /// <summary>版本号不在合理范围时退回这个值（基岩版现行版本）</summary>
        private const int DefaultBedrockVersion = 9;

        private const uint AdlerMod = 65521;

        /* ================================================================== */
        /* 对外接口                                                            */
        /* ================================================================== */

        /// <summary>给界面显示用的压缩方式名字</summary>
        public static string DescribeCompression(NbtCompression c)
        {
            switch (c)
            {
                case NbtCompression.None: return "无压缩";
                case NbtCompression.GZip: return "gzip";
                case NbtCompression.Zlib: return "zlib";
                case NbtCompression.Deflate: return "deflate";
            }
            return "未知压缩(" + ((int)c) + ")";
        }

        /// <summary>
        /// 读一个 NBT 文件：先按文件头认压缩，再在「大端 / 小端 / 小端+8 字节头」里挑一个
        /// 能整段读干净的解析方式。失败一律抛 IOException，消息里说清是压缩坏了还是格式不对。
        /// </summary>
        public static NbtDocument Load(string path)
        {
            if (path == null) throw new ArgumentNullException("path");

            byte[] raw = ReadFileBytes(path);
            if (raw.Length == 0)
                throw new IOException("文件是空的（0 字节），不可能是 NBT：" + path);

            NbtCompression compression = DetectCompression(raw);
            byte[] payload;
            Exception decompressError = null;

            if (compression == NbtCompression.GZip)
            {
                try
                {
                    payload = Decompress(raw, 0, raw.Length, NbtCompression.GZip);
                }
                catch (Exception e)
                {
                    throw new IOException("解压失败：文件头是 gzip（1F 8B），但数据是坏的（" + e.Message + "）。", e);
                }
            }
            else if (compression == NbtCompression.Zlib)
            {
                try
                {
                    payload = Decompress(raw, 0, raw.Length, NbtCompression.Zlib);
                }
                catch (Exception e)
                {
                    /* zlib 只能靠 2 字节头认，31 分之 1 的概率会误判成 zlib；
                       解不开就退回「没压缩」再走一遍，最后仍失败再报这个压缩错。 */
                    decompressError = e;
                    compression = NbtCompression.None;
                    payload = raw;
                }
            }
            else
            {
                payload = raw;
            }

            if (payload.Length == 0)
            {
                /* 认得出压缩头却一个字节都没解出来：多半是文件被截断了 */
                if (compression != NbtCompression.None)
                    throw new IOException("解压失败（" + DescribeCompression(compression) +
                                          "）：解出来是空的，文件被截断或者损坏：" + path);
                throw new IOException("文件里没有数据：" + path);
            }

            int headerVersion;
            bool header = TryReadBedrockHeader(payload, out headerVersion);

            ParseAttempt chosen;
            Exception parseError;
            if (!TryPickParse(payload, header, headerVersion, out chosen, out parseError))
            {
                /* 裸 deflate 没有文件头，前面按「不压缩」处理了；解析不出来时再赌一把：
                   解压得出来、且解出来的东西像 NBT，才认它是 deflate。 */
                if (compression == NbtCompression.None)
                {
                    byte[] inflated;
                    if (TryInflateRaw(raw, out inflated) && inflated.Length > 0)
                    {
                        int v2;
                        bool h2 = TryReadBedrockHeader(inflated, out v2);
                        ParseAttempt retry;
                        Exception retryError;
                        if (TryPickParse(inflated, h2, v2, out retry, out retryError))
                        {
                            compression = NbtCompression.Deflate;
                            payload = inflated;
                            header = h2;
                            headerVersion = v2;
                            chosen = retry;
                            parseError = null;
                        }
                    }
                }

                if (parseError != null)
                {
                    if (decompressError != null)
                        throw new IOException("解压失败：文件头看起来是 zlib 压缩，但数据是坏的（" +
                                              decompressError.Message + "）。", decompressError);
                    throw new IOException("这个文件不是有效的 NBT：" + parseError.Message, parseError);
                }
            }

            var doc = new NbtDocument();
            doc.Path = path;
            doc.Root = chosen.Root;
            doc.RootName = chosen.RootName;
            doc.Compression = compression;
            doc.LittleEndian = chosen.LittleEndian;
            doc.HasBedrockHeader = chosen.HasHeader;
            doc.BedrockVersion = chosen.HasHeader ? chosen.HeaderVersion : DefaultBedrockVersion;
            doc.FileBytes = raw.Length;
            doc.PayloadBytes = payload.Length;
            return doc;
        }

        /// <summary>
        /// 存一个 NBT 文件：先序列化出正文，需要时补基岩版 8 字节头，再压缩，
        /// 最后先写同目录临时文件、成功了才替换目标——中途失败不会留下半个文件。
        /// </summary>
        public static void Save(NbtDocument doc, string path, NbtCompression compression, bool littleEndian, bool bedrockHeader)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("保存路径不能为空。", "path");
            if (doc.Root == null) throw new ArgumentException("这个文档没有根标签，存不了。", "doc");

            switch (compression)
            {
                case NbtCompression.None:
                case NbtCompression.GZip:
                case NbtCompression.Zlib:
                case NbtCompression.Deflate:
                    break;
                default:
                    throw new ArgumentException("未知的压缩方式：" + ((int)compression), "compression");
            }

            byte[] payload = Serialize(doc.Root, doc.RootName, littleEndian);

            int version = doc.BedrockVersion;
            if (bedrockHeader)
            {
                if (version < 0 || version > MaxBedrockVersion) version = DefaultBedrockVersion;
                var withHeader = new byte[BedrockHeaderSize + payload.Length];
                WriteInt32Le(withHeader, 0, version);
                WriteInt32Le(withHeader, 4, payload.Length);
                Buffer.BlockCopy(payload, 0, withHeader, BedrockHeaderSize, payload.Length);
                payload = withHeader;
            }

            byte[] fileBytes;
            try
            {
                fileBytes = Compress(payload, compression);
            }
            catch (Exception e)
            {
                throw new IOException("压缩失败（" + DescribeCompression(compression) + "）：" + e.Message, e);
            }

            try
            {
                WriteFileAtomic(path, fileBytes);
            }
            catch (IOException e)
            {
                throw new IOException("写入文件失败：" + e.Message, e);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new IOException("没有权限写入这个文件：" + e.Message, e);
            }

            /* 存完之后文档的来源信息跟着变，界面上的「来源」一栏才对得上 */
            doc.Path = path;
            doc.Compression = compression;
            doc.LittleEndian = littleEndian;
            doc.HasBedrockHeader = bedrockHeader;
            if (bedrockHeader) doc.BedrockVersion = version;
            doc.FileBytes = fileBytes.Length;
            doc.PayloadBytes = payload.Length;
        }

        /// <summary>把一棵树序列化成 NBT 字节。根标签名以 rootName 为准（null 时用 root.Name）</summary>
        public static byte[] Serialize(NbtCompound root, string rootName, bool littleEndian)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (rootName == null) rootName = root.Name == null ? "" : root.Name;

            var writer = new NbtWriter(littleEndian);
            WriteTag(writer, root, rootName);
            return writer.ToArray();
        }

        /// <summary>
        /// 从字节里解析出一棵树。根不是复合标签时会包一层空复合标签（现实里极少，
        /// 但不能因为这样就崩掉），rootName 仍然是文件里写的那个根名。
        /// 尾部多余字节不报错，交给 Load 去挑「读得最干净」的那种解析方式。
        /// </summary>
        public static NbtCompound Deserialize(byte[] data, int offset, int count, bool littleEndian, out string rootName)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (long)offset + count > data.Length) throw new ArgumentOutOfRangeException("count");
            if (count == 0) throw new NbtFormatException("没有可解析的数据（长度 0）。");

            var reader = new NbtReader(data, offset, count, littleEndian);
            NbtTag raw = reader.ReadRoot(out rootName);
            return WrapRoot(raw);
        }

        /* ================================================================== */
        /* 压缩识别与解压                                                      */
        /* ================================================================== */

        /// <summary>按文件头认压缩：1F 8B 是 gzip；首字节 78 且前两字节凑得成 31 的倍数是 zlib</summary>
        private static NbtCompression DetectCompression(byte[] raw)
        {
            if (raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B) return NbtCompression.GZip;
            if (raw.Length >= 2 && raw[0] == 0x78 && ((raw[0] * 256 + raw[1]) % 31) == 0) return NbtCompression.Zlib;
            return NbtCompression.None;
        }

        private static byte[] Decompress(byte[] data, int offset, int count, NbtCompression compression)
        {
            switch (compression)
            {
                case NbtCompression.GZip:
                    using (var ms = new MemoryStream(data, offset, count, false))
                    using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                        return ReadAll(gz);

                case NbtCompression.Zlib:
                    /* .NET 的 DeflateStream 是裸 deflate，不认 zlib 头，所以自己跳过 2 字节；
                       结尾 4 字节 Adler-32 不用管，DeflateStream 读到最后一个块就停了。 */
                    if (count < 6) throw new NbtFormatException("zlib 数据太短（" + count + " 字节）。");
                    return DecompressDeflate(data, offset + 2, count - 2);

                case NbtCompression.Deflate:
                    return DecompressDeflate(data, offset, count);
            }

            var copy = new byte[count];
            Buffer.BlockCopy(data, offset, copy, 0, count);
            return copy;
        }

        private static byte[] DecompressDeflate(byte[] data, int offset, int count)
        {
            using (var ms = new MemoryStream(data, offset, count, false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                return ReadAll(ds);
        }

        /// <summary>试解一次裸 deflate，失败就当不是（用来认没有文件头的 deflate）</summary>
        private static bool TryInflateRaw(byte[] data, out byte[] inflated)
        {
            inflated = null;
            try
            {
                inflated = DecompressDeflate(data, 0, data.Length);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] Compress(byte[] payload, NbtCompression compression)
        {
            switch (compression)
            {
                case NbtCompression.None:
                    return payload;

                case NbtCompression.GZip:
                    using (var ms = new MemoryStream())
                    {
                        using (var gz = new GZipStream(ms, CompressionMode.Compress))
                            gz.Write(payload, 0, payload.Length);
                        return ms.ToArray();   // MemoryStream 关掉了也能 ToArray
                    }

                case NbtCompression.Zlib:
                    {
                        byte[] body = DeflateCompress(payload);
                        var outMs = new MemoryStream(body.Length + 6);
                        outMs.WriteByte(0x78);   // CMF：deflate + 32K 窗口
                        outMs.WriteByte(0x9C);   // FLG：默认压缩级别，凑成 31 的倍数
                        outMs.Write(body, 0, body.Length);
                        uint adler = Adler32(payload);
                        outMs.WriteByte((byte)(adler >> 24));
                        outMs.WriteByte((byte)(adler >> 16));
                        outMs.WriteByte((byte)(adler >> 8));
                        outMs.WriteByte((byte)adler);
                        return outMs.ToArray();
                    }

                case NbtCompression.Deflate:
                    return DeflateCompress(payload);
            }
            throw new ArgumentException("未知的压缩方式：" + ((int)compression), "compression");
        }

        private static byte[] DeflateCompress(byte[] payload)
        {
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress))
                    ds.Write(payload, 0, payload.Length);
                return ms.ToArray();
            }
        }

        /// <summary>zlib 的 Adler-32 校验和（自己算，.NET 不给现成的）</summary>
        private static uint Adler32(byte[] data)
        {
            uint a = 1;
            uint b = 0;
            int i = 0;
            /* 5552 是经典的分块长度：一块之内 a、b 都不会溢出 uint */
            while (i < data.Length)
            {
                int chunk = data.Length - i;
                if (chunk > 5552) chunk = 5552;
                for (int j = 0; j < chunk; j++)
                {
                    a += data[i + j];
                    b += a;
                }
                a %= AdlerMod;
                b %= AdlerMod;
                i += chunk;
            }
            return (b << 16) | a;
        }

        private static byte[] ReadAll(Stream input)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, n);
                return output.ToArray();
            }
        }

        /* ================================================================== */
        /* 大小端判定                                                          */
        /* ================================================================== */

        /// <summary>一次「从某个偏移、按某种大小端」的试解析</summary>
        private sealed class ParseAttempt
        {
            public byte[] Payload;
            public int Offset;
            public bool LittleEndian;
            public bool HasHeader;
            public int HeaderVersion;
            public NbtCompound Root;
            public string RootName = "";
            public int Consumed;
            public Exception Error;

            /// <summary>解析没报错</summary>
            public bool Parsed { get { return Error == null && Root != null; } }

            /// <summary>整个缓冲区都读干净了（允许几个字节的尾巴）</summary>
            public bool Clean { get { return Parsed && Consumed >= Payload.Length - Offset - TrailingSlack; } }

            public int Remaining { get { return Payload.Length - Offset - Consumed; } }
        }

        private static ParseAttempt TryLoad(byte[] payload, int offset, bool littleEndian, bool hasHeader, int headerVersion)
        {
            var attempt = new ParseAttempt();
            attempt.Payload = payload;
            attempt.Offset = offset;
            attempt.LittleEndian = littleEndian;
            attempt.HasHeader = hasHeader;
            attempt.HeaderVersion = headerVersion;
            try
            {
                var reader = new NbtReader(payload, offset, payload.Length - offset, littleEndian);
                string name;
                NbtTag raw = reader.ReadRoot(out name);
                attempt.RootName = name;
                attempt.Root = WrapRoot(raw);
                attempt.Consumed = reader.Position - offset;
            }
            catch (Exception e)
            {
                attempt.Error = e;
            }
            return attempt;
        }

        /// <summary>
        /// 依次试「大端」「小端」「跳 8 字节头 + 小端」，谁把整段读干净就用谁。
        /// 小端文件按大端解析通常会直接抛异常，所以顺序本身就能分开绝大多数文件；
        /// 都不干净时不硬猜——宁可报错，也不要猜错大小端后把用户的存档改写坏。
        /// </summary>
        private static bool TryPickParse(byte[] payload, bool headerLooksValid, int headerVersion,
                                         out ParseAttempt chosen, out Exception error)
        {
            var attempts = new List<ParseAttempt>();
            attempts.Add(TryLoad(payload, 0, false, false, 0));
            attempts.Add(TryLoad(payload, 0, true, false, 0));
            if (headerLooksValid && payload.Length > BedrockHeaderSize)
                attempts.Add(TryLoad(payload, BedrockHeaderSize, true, true, headerVersion));

            chosen = null;
            foreach (var attempt in attempts)
            {
                if (attempt.Clean) { chosen = attempt; break; }
            }

            if (chosen != null)
            {
                error = null;
                return true;
            }

            /* 报第一次尝试（大端）的真实问题，便于排查 */
            ParseAttempt first = attempts[0];
            if (first.Error != null) error = first.Error;
            else error = new NbtFormatException("根标签只读掉了 " + first.Consumed + " 字节，还剩 " +
                                                first.Remaining + " 字节没读懂（文件损坏或大小端不对）。");
            return false;
        }

        /// <summary>
        /// 认基岩版 8 字节头：[版本:4 字节小端][负载长度:4 字节小端]，版本在 0..20 之间、
        /// 长度正好等于剩下的字节数才算数，免得把普通 NBT 的头几个字节误当成版本号。
        /// </summary>
        private static bool TryReadBedrockHeader(byte[] data, out int version)
        {
            version = 0;
            if (data.Length <= BedrockHeaderSize) return false;

            int v = ReadInt32Le(data, 0);
            int len = ReadInt32Le(data, 4);
            if (v < 0 || v > MaxBedrockVersion) return false;
            if (len != data.Length - BedrockHeaderSize) return false;

            version = v;
            return true;
        }

        /// <summary>根不是复合标签时包一层空复合标签，让界面永远拿到一个 Compound 根</summary>
        private static NbtCompound WrapRoot(NbtTag raw)
        {
            var compound = raw as NbtCompound;
            if (compound != null) return compound;

            var wrapper = new NbtCompound("");
            wrapper.Add(raw);
            return wrapper;
        }

        /* ================================================================== */
        /* 小端辅助                                                            */
        /* ================================================================== */

        private static int ReadInt32Le(byte[] data, int at)
        {
            return data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
        }

        private static void WriteInt32Le(byte[] data, int at, int value)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
            data[at + 2] = (byte)(value >> 16);
            data[at + 3] = (byte)(value >> 24);
        }

        /* ================================================================== */
        /* 写                                                                  */
        /* ================================================================== */

        private static void WriteTag(NbtWriter writer, NbtTag tag, string name)
        {
            if (tag == null) throw new NbtFormatException("要写入的标签是空的。");
            writer.WriteByte((int)tag.Type);
            writer.WriteString(name == null ? "" : name);
            WritePayload(writer, tag);
        }

        private static void WritePayload(NbtWriter writer, NbtTag tag)
        {
            if (tag == null) throw new NbtFormatException("要写入的标签是空的。");

            switch (tag.Type)
            {
                case NbtType.Byte: writer.WriteByte(((NbtByte)tag).Value); return;
                case NbtType.Short: writer.WriteInt16(((NbtShort)tag).Value); return;
                case NbtType.Int: writer.WriteInt32(((NbtInt)tag).Value); return;
                case NbtType.Long: writer.WriteInt64(((NbtLong)tag).Value); return;
                case NbtType.Float: writer.WriteSingle(((NbtFloat)tag).Value); return;
                case NbtType.Double: writer.WriteDouble(((NbtDouble)tag).Value); return;
                case NbtType.String: writer.WriteString(((NbtString)tag).Value); return;
                case NbtType.ByteArray: WriteByteArray(writer, ((NbtByteArray)tag).Value); return;
                case NbtType.ShortArray: WriteShortArray(writer, ((NbtShortArray)tag).Value); return;
                case NbtType.IntArray: WriteIntArray(writer, ((NbtIntArray)tag).Value); return;
                case NbtType.LongArray: WriteLongArray(writer, ((NbtLongArray)tag).Value); return;
                case NbtType.List: WriteList(writer, (NbtList)tag); return;
                case NbtType.Compound: WriteCompound(writer, (NbtCompound)tag); return;
            }
            throw new NbtFormatException("不能写入类型 " + NbtTypeInfo.TagName(tag.Type) + "。");
        }

        private static void WriteCompound(NbtWriter writer, NbtCompound tag)
        {
            foreach (var child in tag.Children)
            {
                if (child == null) throw new NbtFormatException("复合标签「" + tag.Name + "」里有空的子标签。");
                WriteTag(writer, child, child.Name);
            }
            writer.WriteByte(0);   // TAG_End 收尾
        }

        private static void WriteList(NbtWriter writer, NbtList tag)
        {
            int count = tag.Items.Count;
            NbtType elementType = NbtType.End;

            if (count > 0)
            {
                /* 以第一个元素的类型为准，而不是 ElementType：两边不一致时按内容写，
                   免得写出「声明 Int 却塞着 String」的坏文件 */
                for (int i = 0; i < count; i++)
                {
                    if (tag.Items[i] == null)
                        throw new NbtFormatException("列表「" + tag.Name + "」里有空的元素。");
                    if (i == 0) elementType = tag.Items[i].Type;
                    else if (tag.Items[i].Type != elementType)
                        throw new NbtFormatException("列表「" + tag.Name + "」里的元素类型不一致（第 " + (i + 1) +
                                                     " 项是 " + NbtTypeInfo.TagName(tag.Items[i].Type) + "）。");
                }
            }

            writer.WriteByte((int)elementType);
            writer.WriteInt32(count);
            for (int i = 0; i < count; i++)
                WritePayload(writer, tag.Items[i]);
        }

        private static void WriteByteArray(NbtWriter writer, byte[] data)
        {
            if (data == null) data = new byte[0];
            writer.WriteInt32(data.Length);
            writer.WriteRaw(data, 0, data.Length);
        }

        private static void WriteShortArray(NbtWriter writer, short[] data)
        {
            if (data == null) data = new short[0];
            writer.WriteInt32(data.Length);
            for (int i = 0; i < data.Length; i++) writer.WriteInt16(data[i]);
        }

        private static void WriteIntArray(NbtWriter writer, int[] data)
        {
            if (data == null) data = new int[0];
            writer.WriteInt32(data.Length);
            for (int i = 0; i < data.Length; i++) writer.WriteInt32(data[i]);
        }

        private static void WriteLongArray(NbtWriter writer, long[] data)
        {
            if (data == null) data = new long[0];
            writer.WriteInt32(data.Length);
            for (int i = 0; i < data.Length; i++) writer.WriteInt64(data[i]);
        }

        /* ================================================================== */
        /* 读                                                                  */
        /* ================================================================== */

        private static byte[] ReadFileBytes(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException e)
            {
                throw new IOException("无法读取文件：" + e.Message, e);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new IOException("没有权限读取这个文件：" + e.Message, e);
            }
        }

        /* ================================================================== */
        /* 原子写文件                                                          */
        /* ================================================================== */

        /// <summary>
        /// 先写同目录的临时文件，写成功了再顶替目标文件：写到一半断电时，
        /// 目标文件要么是旧的完整内容，要么是新的完整内容，不会变成半个。
        /// </summary>
        private static void WriteFileAtomic(string path, byte[] data)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);

            string temp = Path.Combine(dir, Path.GetFileName(full) + ".tmp" + Path.GetRandomFileName());

            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush();
                }

                if (File.Exists(full))
                {
                    /* File.Replace 要求目标和临时文件在同一个卷上，同目录正好满足；
                       它还会保留目标原有的权限设置（临时文件是新建的，权限是默认值） */
                    File.Replace(temp, full, null, true);
                }
                else
                {
                    File.Move(temp, full);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception)
                {
                    /* 清临时文件失败不能再盖掉真正的错误 */
                }
                throw;
            }
        }
    }

    /* ================================================================== */
    /* 二进制读取器                                                        */
    /* ================================================================== */

    /// <summary>
    /// 在 byte[] 的一段上顺序读取 NBT。所有多字节数值都按构造时给的大小端读写，
    /// 越界、长度不对、类型不认识一律抛 NbtFormatException（IOException）。
    /// </summary>
    internal sealed class NbtReader
    {
        /// <summary>嵌套层数上限：坏文件可以写出十万层复合标签，不拦着会把栈打爆</summary>
        private const int MaxDepth = 512;

        private readonly byte[] _data;
        private readonly bool _littleEndian;
        private readonly int _start;
        private readonly int _end;
        private int _pos;
        private int _depth;

        public NbtReader(byte[] data, int offset, int count, bool littleEndian)
        {
            _data = data;
            _start = offset;
            _end = offset + count;
            _littleEndian = littleEndian;
            _pos = offset;
        }

        public int Position { get { return _pos; } }
        public bool AtEnd { get { return _pos >= _end; } }

        /* ---------------- 基础读取 ---------------- */

        private void Need(int n, string what)
        {
            if (n < 0 || _end - _pos < n)
                throw new NbtFormatException(what + "需要 " + n + " 字节，只剩 " + (_end - _pos) +
                                             " 字节（偏移 " + _pos + "），文件在这里就断了。");
        }

        private int ReadUInt8(string what)
        {
            Need(1, what);
            return _data[_pos++];
        }

        private int ReadInt16Raw(string what)
        {
            Need(2, what);
            int v;
            if (_littleEndian) v = _data[_pos] | (_data[_pos + 1] << 8);
            else v = (_data[_pos] << 8) | _data[_pos + 1];
            _pos += 2;
            return v;
        }

        private int ReadInt32Raw(string what)
        {
            Need(4, what);
            int v;
            if (_littleEndian)
                v = _data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24);
            else
                v = (_data[_pos] << 24) | (_data[_pos + 1] << 16) | (_data[_pos + 2] << 8) | _data[_pos + 3];
            _pos += 4;
            return v;
        }

        private long ReadInt64Raw(string what)
        {
            Need(8, what);
            long hi;
            long lo;
            if (_littleEndian)
            {
                lo = (uint)(_data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24));
                hi = (uint)(_data[_pos + 4] | (_data[_pos + 5] << 8) | (_data[_pos + 6] << 16) | (_data[_pos + 7] << 24));
            }
            else
            {
                hi = (uint)((_data[_pos] << 24) | (_data[_pos + 1] << 16) | (_data[_pos + 2] << 8) | _data[_pos + 3]);
                lo = (uint)((_data[_pos + 4] << 24) | (_data[_pos + 5] << 16) | (_data[_pos + 6] << 8) | _data[_pos + 7]);
            }
            _pos += 8;
            return (hi << 32) | lo;
        }

        private float ReadSingle(string what)
        {
            Need(4, what);
            var bytes = new byte[4];
            Buffer.BlockCopy(_data, _pos, bytes, 0, 4);
            if (!_littleEndian) Array.Reverse(bytes);
            _pos += 4;
            return BitConverter.ToSingle(bytes, 0);
        }

        private double ReadDouble(string what)
        {
            Need(8, what);
            var bytes = new byte[8];
            Buffer.BlockCopy(_data, _pos, bytes, 0, 8);
            if (!_littleEndian) Array.Reverse(bytes);
            _pos += 8;
            return BitConverter.ToDouble(bytes, 0);
        }

        /// <summary>
        /// 读一个字符串：[长度:2 字节][UTF-8 字节]。长度在前，所以先把「声明长度」和
        /// 「实际剩多少」比一比——不这么干，一个坏长度就能让我们去申请几个 G 的内存。
        /// </summary>
        private string ReadString(string what)
        {
            int at = _pos;
            int len = ReadInt16Raw(what + "的长度");
            if (len < 0) len += 65536;               // 按 2 字节无符号读
            if (_end - _pos < len)
                throw new NbtFormatException(what + "声明有 " + len + " 字节（UTF-8），实际只剩 " +
                                             (_end - _pos) + " 字节（偏移 " + at + "）。");
            string s = Encoding.UTF8.GetString(_data, _pos, len);   // NBT 规定 UTF-8、不带 BOM
            _pos += len;
            return s;
        }

        private NbtType CheckType(int typeId, int at)
        {
            if (typeId < 1 || typeId > 13)
                throw new NbtFormatException("不认识的标签类型 " + typeId + "（偏移 " + at + "），" +
                                             "这个文件多半不是 NBT。");
            return (NbtType)typeId;
        }

        private void EnterContainer(int at, string what)
        {
            _depth++;
            if (_depth > MaxDepth)
                throw new NbtFormatException(what + "嵌套超过 " + MaxDepth + " 层（偏移 " + at +
                                             "），文件多半是坏的；不拦着会把程序的栈撑爆。");
        }

        /* ---------------- 标签 ---------------- */

        /// <summary>读根标签：类型 + 名字 + 内容</summary>
        public NbtTag ReadRoot(out string rootName)
        {
            int at = _pos;
            int typeId = ReadUInt8("根标签类型");
            if (typeId == 0)
                throw new NbtFormatException("根标签是 TAG_End（偏移 " + at + "），这不是有效的 NBT 文件。");

            NbtType type = CheckType(typeId, at);
            rootName = ReadString("根标签名");
            return ReadPayload(type, rootName, at);
        }

        private NbtTag ReadPayload(NbtType type, string name, int at)
        {
            switch (type)
            {
                case NbtType.Byte: return new NbtByte(name, (sbyte)ReadUInt8("TAG_Byte「" + name + "」的值"));
                case NbtType.Short: return new NbtShort(name, (short)ReadInt16Raw("TAG_Short「" + name + "」的值"));
                case NbtType.Int: return new NbtInt(name, ReadInt32Raw("TAG_Int「" + name + "」的值"));
                case NbtType.Long: return new NbtLong(name, ReadInt64Raw("TAG_Long「" + name + "」的值"));
                case NbtType.Float: return new NbtFloat(name, ReadSingle("TAG_Float「" + name + "」的值"));
                case NbtType.Double: return new NbtDouble(name, ReadDouble("TAG_Double「" + name + "」的值"));
                case NbtType.String: return new NbtString(name, ReadString("TAG_String「" + name + "」的值"));
                case NbtType.ByteArray: return new NbtByteArray(name, ReadByteArray(name));
                case NbtType.ShortArray: return new NbtShortArray(name, ReadShortArray(name));
                case NbtType.IntArray: return new NbtIntArray(name, ReadIntArray(name));
                case NbtType.LongArray: return new NbtLongArray(name, ReadLongArray(name));
                case NbtType.List: return ReadList(name);
                case NbtType.Compound: return ReadCompound(name);
            }
            throw new NbtFormatException("不支持的标签类型 " + ((int)type) + "（偏移 " + at + "）。");
        }

        private NbtCompound ReadCompound(string name)
        {
            int at = _pos;
            EnterContainer(at, "复合标签「" + name + "」");
            var compound = new NbtCompound(name);

            while (true)
            {
                int childAt = _pos;
                int typeId = ReadUInt8("子标签类型");
                if (typeId == 0) break;                       // TAG_End 收尾
                NbtType type = CheckType(typeId, childAt);
                string childName = ReadString("子标签名");
                compound.Add(ReadPayload(type, childName, childAt));
            }

            _depth--;
            return compound;
        }

        private NbtList ReadList(string name)
        {
            int at = _pos;
            int typeId = ReadUInt8("列表的元素类型");
            NbtType elementType = typeId == 0 ? NbtType.End : CheckType(typeId, at);
            int count = ReadInt32Raw("列表「" + name + "」的元素个数");

            if (count < 0)
                throw new NbtFormatException("列表「" + name + "」的元素个数是负数（" + count +
                                             "，偏移 " + at + "），文件损坏。");
            if (count > 0 && elementType == NbtType.End)
                throw new NbtFormatException("列表「" + name + "」声明有 " + count +
                                             " 个元素，元素类型却是 TAG_End（偏移 " + at + "），文件损坏。");

            EnterContainer(at, "列表「" + name + "」");
            /* 元素类型按文件里写的保留：空列表也可能声明成 TAG_String 之类，界面要照原样显示 */
            var list = new NbtList(name, elementType);
            for (int i = 0; i < count; i++)
            {
                int itemAt = _pos;
                if (AtEnd)
                    throw new NbtFormatException("列表「" + name + "」的第 " + (i + 1) + " 个元素没数据了（偏移 " +
                                                 itemAt + "）。");
                list.Add(ReadPayload(elementType, "", itemAt));
            }
            _depth--;

            return list;
        }

        /* ---------------- 数组 ---------------- */

        /// <summary>
        /// 读数组元素个数。负数直接报错；再按「元素个数 × 元素大小」跟剩余字节比一次，
        /// 免得一个坏长度让我们去 new 一个几 G 的数组。
        /// </summary>
        private int ReadArrayCount(string name, int elementSize)
        {
            int at = _pos;
            int count = ReadInt32Raw("数组「" + name + "」的元素个数");
            if (count < 0)
                throw new NbtFormatException("数组「" + name + "」的元素个数是负数（" + count +
                                             "，偏移 " + at + "），文件损坏。");
            if ((long)count * elementSize > _end - _pos)
                throw new NbtFormatException("数组「" + name + "」声明有 " + count + " 个元素（需要 " +
                                             ((long)count * elementSize) + " 字节），实际只剩 " +
                                             (_end - _pos) + " 字节（偏移 " + at + "）。");
            return count;
        }

        private byte[] ReadByteArray(string name)
        {
            int count = ReadArrayCount(name, 1);
            var result = new byte[count];
            if (count > 0) Buffer.BlockCopy(_data, _pos, result, 0, count);
            _pos += count;
            return result;
        }

        private short[] ReadShortArray(string name)
        {
            int count = ReadArrayCount(name, 2);
            var result = new short[count];
            for (int i = 0; i < count; i++) result[i] = (short)ReadInt16Raw("数组「" + name + "」的元素");
            return result;
        }

        private int[] ReadIntArray(string name)
        {
            int count = ReadArrayCount(name, 4);
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = ReadInt32Raw("数组「" + name + "」的元素");
            return result;
        }

        private long[] ReadLongArray(string name)
        {
            int count = ReadArrayCount(name, 8);
            var result = new long[count];
            for (int i = 0; i < count; i++) result[i] = ReadInt64Raw("数组「" + name + "」的元素");
            return result;
        }
    }

    /* ================================================================== */
    /* 二进制写入器                                                        */
    /* ================================================================== */

    /// <summary>
    /// 往自带的可增长缓冲区里按指定大小端写 NBT。手写而不是用 BinaryWriter，
    /// 因为 BinaryWriter 只会小端，大端还得自己翻字节，不如一次写对。
    /// </summary>
    internal sealed class NbtWriter
    {
        private byte[] _buf = new byte[1024];
        private int _len;
        private readonly bool _littleEndian;

        public NbtWriter(bool littleEndian)
        {
            _littleEndian = littleEndian;
        }

        public int Length { get { return _len; } }

        public byte[] ToArray()
        {
            var result = new byte[_len];
            Buffer.BlockCopy(_buf, 0, result, 0, _len);
            return result;
        }

        private void Ensure(int extra)
        {
            int need = _len + extra;
            if (need <= _buf.Length) return;

            int cap = _buf.Length;
            while (cap < need) cap = Math.Max(cap * 2, need);   // Math.Max 防溢出死循环

            var bigger = new byte[cap];
            Buffer.BlockCopy(_buf, 0, bigger, 0, _len);
            _buf = bigger;
        }

        public void WriteByte(int value)
        {
            Ensure(1);
            _buf[_len++] = (byte)value;
        }

        public void WriteRaw(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            Ensure(count);
            Buffer.BlockCopy(data, offset, _buf, _len, count);
            _len += count;
        }

        public void WriteInt16(short value)
        {
            Ensure(2);
            if (_littleEndian)
            {
                _buf[_len] = (byte)value;
                _buf[_len + 1] = (byte)(value >> 8);
            }
            else
            {
                _buf[_len] = (byte)(value >> 8);
                _buf[_len + 1] = (byte)value;
            }
            _len += 2;
        }

        public void WriteInt32(int value)
        {
            Ensure(4);
            if (_littleEndian)
            {
                _buf[_len] = (byte)value;
                _buf[_len + 1] = (byte)(value >> 8);
                _buf[_len + 2] = (byte)(value >> 16);
                _buf[_len + 3] = (byte)(value >> 24);
            }
            else
            {
                _buf[_len] = (byte)(value >> 24);
                _buf[_len + 1] = (byte)(value >> 16);
                _buf[_len + 2] = (byte)(value >> 8);
                _buf[_len + 3] = (byte)value;
            }
            _len += 4;
        }

        public void WriteInt64(long value)
        {
            Ensure(8);
            for (int i = 0; i < 8; i++)
            {
                int shift = _littleEndian ? (i * 8) : ((7 - i) * 8);
                _buf[_len + i] = (byte)(value >> shift);
            }
            _len += 8;
        }

        public void WriteSingle(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);   // 本机是小端，正好是 IEEE754 小端序
            if (!_littleEndian) Array.Reverse(bytes);
            WriteRaw(bytes, 0, 4);
        }

        public void WriteDouble(double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!_littleEndian) Array.Reverse(bytes);
            WriteRaw(bytes, 0, 8);
        }

        /// <summary>有没有落单的代理项（比如粘进来的半个 emoji）</summary>
        private static void CheckSurrogates(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
                }
                else if (!char.IsLowSurrogate(c))
                {
                    continue;
                }
                throw new NbtFormatException("字符串里有落单的代理项（第 " + (i + 1) +
                                             " 个字符，多半是粘进来的半个 emoji）。写成 UTF-8 会变成「?」再也还原不回来，先把它改掉。");
            }
        }

        /// <summary>
        /// 写字符串：[长度:2 字节][UTF-8 字节]。长度字段只有 2 字节，超长的字符串
        /// 在这里就报错，绝不做「悄悄截断」这种事——那会把用户的存档写坏；
        /// 落单的代理项同理，宁可报错也不让它悄悄变成「?」。
        /// </summary>
        public void WriteString(string value)
        {
            if (value == null) value = "";
            CheckSurrogates(value);
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > 65535)
                throw new NbtFormatException("字符串太长：UTF-8 之后 " + bytes.Length +
                                             " 字节，NBT 的长度字段只有 2 字节（最多 65535）。");
            WriteInt16((short)bytes.Length);
            WriteRaw(bytes, 0, bytes.Length);
        }
    }
}
