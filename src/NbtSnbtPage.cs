using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* NBT · SNBT 与 JSON 转换                                             */
    /* ================================================================== */

    /// <summary>
    /// NBT ↔ 文本。对应 NBTExplorer 里 NBTUtil 的 --print / --print-json 能力，
    /// 但做成来回都能走：文本改完可以再存回 .dat。
    /// </summary>
    internal sealed class NbtSnbtPage : IModulePage
    {
        public string Title { get { return "SNBT 与 JSON"; } }
        public string Subtitle { get { return "把 .dat 变成可读文本，或把文本写回 NBT 文件（无损，JSON 导出会丢长整数精度）"; } }

        public event EventHandler<string> StatusChanged;

        private Control _view;
        private TextBox _pathBox;
        private RichTextBox _text;
        private Label _info, _state;
        private NbtCompound _parsedRoot;
        private string _parsedRootName = "";

        public void OnEnter() { }
        public void OnLeave() { }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildLoadCard());
            stack.Controls.Add(BuildTextCard());
            stack.Controls.Add(BuildSaveCard());

            _view = host;
            return host;
        }

        private Card BuildLoadCard()
        {
            var card = new Card { Title = "① 打开 NBT 文件", Hint = "读出来直接变成 SNBT 文本", Height = 132, Margin = new Padding(0, 0, 0, 12) };

            _pathBox = Ui.ReadOnlyBox("");
            _pathBox.Font = Theme.M(8.5f);

            FlatBtn browse;
            var row = PageKit.PathRow(_pathBox, "选文件…", () =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "打开 NBT 文件";
                    dlg.Filter = "NBT 数据 (*.dat;*.nbt)|*.dat;*.nbt|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    LoadToText(dlg.FileName);
                }
            }, out browse);

            _info = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(8.8f),
                BackColor = Color.Transparent,
                Text = "还没有载入文件。",
            };

            card.Body.Controls.Add(_info);
            card.Body.Controls.Add(PageKit.Spacer(2));
            card.Body.Controls.Add(row);
            card.Body.Controls.Add(PageKit.Spacer(2));
            card.Body.Controls.Add(PageKit.MakeLabel("文件位置"));
            return card;
        }

        private Card BuildTextCard()
        {
            var card = new Card { Title = "② SNBT 文本", Hint = "可以在这里改，改完去下面存回文件", Height = 330, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };

            var format = new FlatBtn("格式化", BtnKind.Outline, 84) { Margin = new Padding(0, 0, 6, 0) };
            var compact = new FlatBtn("压成一行", BtnKind.Ghost, 92) { Margin = new Padding(0, 0, 6, 0) };
            var check = new FlatBtn("校验", BtnKind.Ghost, 70) { Margin = new Padding(0, 0, 6, 0) };
            var clear = new FlatBtn("清空", BtnKind.Ghost, 70) { Margin = new Padding(0, 0, 6, 0) };

            format.Click += (s, e) => Reformat(true);
            compact.Click += (s, e) => Reformat(false);
            check.Click += (s, e) => Validate(true);
            clear.Click += (s, e) => { _text.Text = ""; _parsedRoot = null; _state.Text = ""; };

            bar.Controls.Add(format);
            bar.Controls.Add(compact);
            bar.Controls.Add(check);
            bar.Controls.Add(clear);

            /* RichTextBox + 关掉系统滚动条：多行 TextBox 的滚动条在深色主题下是白的 */
            _text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ScrollBars = RichTextBoxScrollBars.None,
                WordWrap = false,
                DetectUrls = false,
                ShortcutsEnabled = true,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                /* 边框关掉：FixedSingle 的框线是系统画的纯白线，深色主题下压不住 */
                BorderStyle = BorderStyle.None,
                Font = Theme.M(9f),
            };
            _text.TextChanged += (s, e) => { _parsedRoot = null; _state.Text = "文本有改动，还没校验。"; _state.ForeColor = Theme.FgFaint; };
            _text.HandleCreated += (s, e) => DarkMode.ApplyTo(_text.Handle);

            card.Body.Controls.Add(_text);
            card.Body.Controls.Add(bar);
            return card;
        }

        private Card BuildSaveCard()
        {
            var card = new Card { Title = "③ 存回去", Hint = "校验通过才能写文件", Height = 150, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };

            var saveNbt = new FlatBtn("存为 NBT 文件…", BtnKind.Primary, 140) { Margin = new Padding(0, 0, 8, 0) };
            var saveSnbt = new FlatBtn("存为 .snbt 文本…", BtnKind.Outline, 140) { Margin = new Padding(0, 0, 8, 0) };
            var exportJson = new FlatBtn("导出 JSON…", BtnKind.Outline, 110) { Margin = new Padding(0, 0, 8, 0) };
            var copy = new FlatBtn("复制文本", BtnKind.Ghost, 92);

            saveNbt.Click += (s, e) => SaveAsNbt();
            saveSnbt.Click += (s, e) => SaveAsText();
            exportJson.Click += (s, e) => ExportJson();
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(_text.Text); Status("SNBT 文本已复制到剪贴板"); }
                catch { }
            };

            bar.Controls.Add(saveNbt);
            bar.Controls.Add(saveSnbt);
            bar.Controls.Add(exportJson);
            bar.Controls.Add(copy);

            _state = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.8f),
                BackColor = Color.Transparent,
                Text = "写成 NBT 时可以选压缩方式和大小端：Java 版用 gzip + 大端，基岩版 level.dat 用不压缩 + 小端 + 8 字节头。",
            };

            card.Body.Controls.Add(_state);
            card.Body.Controls.Add(bar);
            return card;
        }

        /* ------------------------------------------------------------ */

        private void LoadToText(string path)
        {
            NbtDocument doc;
            try
            {
                doc = NbtIo.Load(path);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "打开失败：\n\n" + e.Message);
                return;
            }

            _pathBox.Text = path;
            _text.Text = NbtSnbt.FormatPretty(doc.Root);
            _parsedRoot = doc.Root;
            _parsedRootName = doc.RootName;

            int nodes = NbtEditOps.CountNodes(doc.Root);
            _info.Text = Path.GetFileName(path) + "　·　" + doc.SourceFormat() + "　·　" + nodes + " 个标签";
            _state.Text = "已载入，可以直接改文本，再点「存为 NBT 文件…」。";
            _state.ForeColor = Theme.FgFaint;
            Status("已把 " + Path.GetFileName(path) + " 转成 SNBT 文本");
        }

        private void Reformat(bool pretty)
        {
            string error = Validate(false);
            if (error != null)
            {
                PageKit.Error(_view, error);
                return;
            }
            if (_parsedRoot == null) return;

            _text.Text = pretty ? NbtSnbt.FormatPretty(_parsedRoot) : NbtSnbt.Format(_parsedRoot);
            Status(pretty ? "已格式化" : "已压成一行");
        }

        /// <summary>校验文本。showOk=true 时把成功信息也写到状态条上</summary>
        private string Validate(bool showOk)
        {
            NbtCompound root;
            string rootName;
            string error;
            if (!NbtSnbt.TryParseRoot(_text.Text, out root, out rootName, out error))
            {
                _state.Text = "文本解析失败：" + error;
                _state.ForeColor = Theme.Danger;
                _parsedRoot = null;
                return "SNBT 解析失败：\n\n" + error;
            }

            _parsedRoot = root;
            _parsedRootName = rootName;
            int nodes = NbtEditOps.CountNodes(root);
            _state.Text = "解析通过：根标签「" + (rootName.Length == 0 ? "(无名)" : rootName) + "」，共 " + nodes + " 个标签。";
            _state.ForeColor = Theme.Ok;
            if (showOk) Status("SNBT 校验通过（" + nodes + " 个标签）");
            return null;
        }

        private void SaveAsNbt()
        {
            string error = Validate(false);
            if (error != null)
            {
                PageKit.Error(_view, error);
                return;
            }

            var doc = new NbtDocument
            {
                Path = null,
                Root = _parsedRoot,
                RootName = _parsedRootName,
                Compression = NbtCompression.GZip,
                LittleEndian = false,
                HasBedrockHeader = false,
                BedrockVersion = 9,
            };

            string suggest = string.IsNullOrEmpty(_pathBox.Text)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "untitled.dat")
                : Path.ChangeExtension(_pathBox.Text, ".dat");

            string path;
            NbtCompression compression;
            bool little, header;
            using (var dlg = new NbtSaveAsForm(doc, suggest))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                path = dlg.PickedPath;
                compression = dlg.PickedCompression;
                little = dlg.PickedLittleEndian;
                header = dlg.PickedBedrockHeader;
            }

            try
            {
                NbtIo.Save(doc, path, compression, little, header);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "保存失败：\n\n" + e.Message);
                return;
            }

            _pathBox.Text = path;
            _state.Text = "已写入 " + path + "（" + NbtIo.DescribeCompression(compression) + (little ? " · 小端" : " · 大端") + "）";
            _state.ForeColor = Theme.Ok;
            Status("SNBT 已写成 NBT 文件：" + path);
        }

        private void SaveAsText()
        {
            string path = PageKit.PickSave(_view, "保存 SNBT 文本", "SNBT 文本 (*.snbt)|*.snbt|文本文件 (*.txt)|*.txt", "data.snbt");
            if (path == null) return;

            try
            {
                File.WriteAllText(path, _text.Text, new UTF8Encoding(false));
                Status("SNBT 文本已保存到 " + path);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "写入失败：" + e.Message);
            }
        }

        private void ExportJson()
        {
            string error = Validate(false);
            if (error != null)
            {
                PageKit.Error(_view, error);
                return;
            }

            string json;
            try { json = NbtJson.Write(_parsedRoot, true); }
            catch (Exception e) { PageKit.Error(_view, "导出失败：" + e.Message); return; }

            using (var dlg = new NbtTextForm("导出 JSON",
                "对应 NBTExplorer 的 NBTUtil --print-json。超过 2^53 的长整数在 JSON 里会丢精度，需要无损请用 SNBT。",
                json, true, null))
            {
                dlg.ShowDialog(_view);
            }
        }

        private void Status(string text)
        {
            var h = StatusChanged;
            if (h != null) h(this, text);

            /* 页面本身不是控件，从自己创建的视图上找宿主窗体 */
            var shell = _view == null ? null : _view.FindForm() as ShellForm;
            if (shell != null) shell.Status(text);
        }
    }
}
