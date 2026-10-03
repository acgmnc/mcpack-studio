using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 暂存写入：先完整生成到临时目录，最后一步原子替换                        */
    /* ================================================================== */

    /// <summary>输出形态</summary>
    internal enum StageOutput { Folder, Zip }

    /// <summary>写入结果</summary>
    internal sealed class WriteReport
    {
        public bool Ok;
        public string Error;
        public string OutputPath;
        public int FileCount;
        public long TotalBytes;
        public string BackupPath;          // 被替换掉的原内容（成功后删除）
        public List<string> Notes = new List<string>();
        public string ElapsedText;

        public string Describe()
        {
            if (!Ok) return "失败：" + Error;
            return FileCount + " 个文件，" + Ui.FormatBytes(TotalBytes) + "，" + ElapsedText;
        }
    }

    /// <summary>
    /// 统一的安全写入流程：
    ///   1. 建暂存目录（尽量和目标同卷，最后一步能瞬间 Move）
    ///   2. 调用方往暂存目录里写内容
    ///   3. 把已存在的目标改名成备份
    ///   4. 暂存目录 → 目标（同卷 Move，跨卷复制）
    ///   5. 成功则删备份；失败则把备份改回来
    ///
    /// 「绝不原地覆盖」的约束也在这里统一检查。
    /// </summary>
    internal static class StagedWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// 执行一次写入。<paramref name="fill"/> 收到暂存目录路径，负责把全部内容写进去。
        /// </summary>
        public static WriteReport Write(string outputPath, StageOutput mode, Action<string> fill, Action<string> note)
        {
            var report = new WriteReport();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string staging = null;
            string backup = null;
            bool committed = false;

            try
            {
                if (string.IsNullOrWhiteSpace(outputPath))
                    throw new InvalidOperationException("没有指定输出路径");

                string outFull = Path.GetFullPath(outputPath);

                if (mode == StageOutput.Zip && !outFull.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    outFull = outFull + ".zip";

                report.OutputPath = outFull;

                staging = MakeStagingDir(outFull);
                if (note != null) note("暂存目录：" + staging);

                fill(staging);

                /* ---- 统计暂存内容 ---- */
                var files = Directory.Exists(staging)
                    ? Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                    : new string[0];

                if (files.Length == 0)
                    throw new InvalidOperationException("没有任何文件可写，已取消");

                report.FileCount = files.Length;
                long bytes = 0;
                foreach (string f in files)
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
                report.TotalBytes = bytes;

                /* ---- 目标若已存在，先改名成备份 ---- */
                if (File.Exists(outFull) || Directory.Exists(outFull))
                {
                    backup = outFull + ".mcpack_backup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    MoveOrCopy(outFull, backup);
                    report.BackupPath = backup;
                    if (note != null) note("原有内容已备份到：" + backup);
                }

                /* ---- 暂存 → 目标 ---- */
                if (mode == StageOutput.Zip)
                {
                    ZipIo.CreateFromDirectory(staging, outFull, null);
                    ZipIo.DeleteDirectory(staging);
                    staging = null;
                }
                else
                {
                    MoveOrCopy(staging, outFull);
                    staging = null;
                }

                committed = true;

                /* ---- 成功：删掉备份 ---- */
                if (backup != null)
                {
                    ZipIo.DeleteDirectory(backup);
                    if (File.Exists(backup)) { try { File.Delete(backup); } catch { } }
                    backup = null;
                }

                report.Ok = true;
            }
            catch (Exception e)
            {
                report.Ok = false;
                report.Error = e.Message;

                /* ---- 失败：把备份改回来 ---- */
                if (!committed && backup != null)
                {
                    try
                    {
                        if (!File.Exists(report.OutputPath) && !Directory.Exists(report.OutputPath))
                        {
                            MoveOrCopy(backup, report.OutputPath);
                            backup = null;
                            if (note != null) note("已从备份恢复原有内容");
                        }
                    }
                    catch
                    {
                        // 恢复失败也要保证备份还在，用户能自己找回
                    }
                }
            }
            finally
            {
                if (staging != null) ZipIo.DeleteDirectory(staging);
                if (backup != null && report.Ok) ZipIo.DeleteDirectory(backup);
            }

            sw.Stop();
            report.ElapsedText = sw.ElapsedMilliseconds + " 毫秒";
            return report;
        }

        /// <summary>暂存目录：优先放在目标所在卷，拿不到就退回系统临时目录</summary>
        public static string MakeStagingDir(string outputPath)
        {
            string dir;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    dir = Path.Combine(root, ".mcpack_build_" + Guid.NewGuid().ToString("N").Substring(0, 12));
                else
                    dir = Path.Combine(Path.GetTempPath(), "mcpack_build_" + Guid.NewGuid().ToString("N"));
            }
            catch
            {
                dir = Path.Combine(Path.GetTempPath(), "mcpack_build_" + Guid.NewGuid().ToString("N"));
            }
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>同卷用 Move，跨卷退回「复制 + 删除」</summary>
        public static void MoveOrCopy(string from, string to)
        {
            try
            {
                if (File.Exists(from))
                {
                    string dir = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Move(from, to);
                }
                else
                {
                    Directory.Move(from, to);
                }
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (File.Exists(from))
            {
                string dir = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(from, to, true);
                try { File.Delete(from); } catch { }
            }
            else
            {
                CopyDirectory(from, to);
                ZipIo.DeleteDirectory(from);
            }
        }

        public static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, ZipIo.RelPath(from, d).Replace('/', Path.DirectorySeparatorChar)));
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = ZipIo.RelPath(from, f).Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(to, rel);
                string parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(f, target, true);
            }
        }

        /// <summary>把一个目录里的内容整理进暂存目录（递归复制，跳过指定相对路径）</summary>
        public static int CopyInto(string sourceRoot, string stagingRoot, Func<string, bool> skip)
        {
            int n = 0;
            string srcFull = Path.GetFullPath(sourceRoot);
            foreach (string f in Directory.GetFiles(srcFull, "*", SearchOption.AllDirectories))
            {
                string rel = ZipIo.RelPath(srcFull, f);
                if (skip != null && skip(rel)) continue;
                WriteBytes(stagingRoot, rel, File.ReadAllBytes(f));
                n++;
            }
            return n;
        }

        /// <summary>往暂存目录写一个文件（自动建目录，拒绝路径穿越）</summary>
        public static void WriteBytes(string stagingRoot, string relPath, byte[] content)
        {
            string rel = ZipIo.Normalize(relPath);
            string full = Path.GetFullPath(Path.Combine(stagingRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!ZipIo.IsInside(Path.GetFullPath(stagingRoot), full))
                throw new InvalidOperationException("非法路径：" + relPath);

            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, content);
        }

        public static void WriteText(string stagingRoot, string relPath, string text)
        {
            WriteBytes(stagingRoot, relPath, Utf8NoBom.GetBytes(text));
        }

        /// <summary>输出路径合法性：不能等于输入，也不能在输入目录内部</summary>
        public static string CheckOutput(string inputPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) return "没有指定输出路径";

            string inFull = null;
            try { inFull = Path.GetFullPath(inputPath); } catch { }

            string outFull;
            try { outFull = Path.GetFullPath(outputPath); } catch { return "输出路径不合法"; }

            if (inFull != null)
            {
                if (string.Equals(inFull, outFull, StringComparison.OrdinalIgnoreCase))
                    return "输出路径与输入相同，请换一个位置（工具不会原地覆盖）";

                if (Directory.Exists(inFull) && ZipIo.IsInside(inFull, outFull))
                    return "输出路径在输入目录内部，会递归复制自己，请换一个位置";
            }
            return null;
        }
    }

    /* ================================================================== */
    /* 页面搭建辅助                                                        */
    /* ================================================================== */

    /// <summary>下拉框里的一个版本条目</summary>
    internal sealed class ComboItem
    {
        public int Format;
        public string Text;
        public override string ToString() { return Text; }
    }

    /// <summary>
    /// 各功能页共用的排版骨架。统一成「host + 纵向 stack + Card」，
    /// 避免每个页面各写一遍 Padding / 宽度同步 / 标签样式。
    /// </summary>
    internal static class PageKit
    {
        /// <summary>一列纵向排列的卡片容器，宽度随窗口自适应</summary>
        public static FlowLayoutPanel MakeStack(Panel host)
        {
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.BgBase,
            };
            host.Controls.Add(stack);
            host.Resize += (s, e) =>
            {
                int w = host.ClientSize.Width - host.Padding.Horizontal - 4;
                if (w < 400) w = 400;
                foreach (Control c in stack.Controls) c.Width = w;
            };
            return stack;
        }

        public static Panel MakeHost()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgBase,
                Padding = new Padding(18, 16, 18, 14),
                AutoScroll = true,
            };
        }

        /// <summary>字段标签</summary>
        public static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft,
            };
        }

        public static Panel Spacer(int h)
        {
            return new Panel { Dock = DockStyle.Top, Height = h, BackColor = Color.Transparent };
        }

        /// <summary>按可用宽度预量高度的自动换行标签（写死高度会裁掉半行）</summary>
        public static Label WrapLabel(string text, Font font, Color color, int width, int padX, int y)
        {
            int w = Math.Max(60, width - padX * 2);
            var probe = new Label { Text = text, Font = font, AutoSize = false, Width = w };
            int h = probe.GetPreferredSize(new Size(w, 0)).Height;
            probe.Dispose();

            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = false,
                Width = w,
                Height = h + 2,
                Location = new Point(padX, y),
                BackColor = Color.Transparent,
            };
        }

        /// <summary>一行：左侧标签 + 右侧浏览按钮 + 中间只读输入框</summary>
        public static Panel PathRow(TextBox box, string buttonText, Action onBrowse, out FlatBtn button)
        {
            var btn = new FlatBtn(buttonText, BtnKind.Outline, 86) { Dock = DockStyle.Right };
            btn.Click += (s, e) => onBrowse();
            box.Dock = DockStyle.Fill;

            var row = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row.Controls.Add(box);
            row.Controls.Add(btn);
            button = btn;
            return row;
        }

        /// <summary>底部按钮条</summary>
        public static Panel ButtonBar(params Control[] items)
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.Transparent };
            for (int i = items.Length - 1; i >= 0; i--)
            {
                items[i].Dock = DockStyle.Left;
                bar.Controls.Add(items[i]);
            }
            return bar;
        }

        /// <summary>选择资源包（文件夹或 ZIP）</summary>
        public static string PickPack(IWin32Window owner, string title)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = title + " —— 选资源包文件夹里的任意文件或 ZIP";
                dlg.Filter = "Minecraft 资源包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = false;
                dlg.FileName = "选择文件夹则点「打开」前先取消";
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg.FileName;
            }
            return null;
        }

        /// <summary>选文件夹</summary>
        public static string PickFolder(IWin32Window owner, string description)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = description;
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg.SelectedPath;
            }
            return null;
        }

        /// <summary>选保存路径</summary>
        public static string PickSave(IWin32Window owner, string title, string filter, string defaultName)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = title;
                dlg.Filter = filter;
                dlg.FileName = defaultName;
                dlg.OverwritePrompt = true;
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg.FileName;
            }
            return null;
        }

        /// <summary>统一的错误提示</summary>
        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>统一的操作确认</summary>
        public static bool Confirm(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, text, "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                   == DialogResult.OK;
        }
    }
}
