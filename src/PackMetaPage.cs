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
    /* 资源包 · pack.mcmeta 编辑                                            */
    /* ================================================================== */

    internal sealed class PackMetaPage : IModulePage
    {
        public string Title { get { return "pack.mcmeta"; } }
        public string Subtitle { get { return "可视化编辑描述、版本声明与图标，带 § 颜色码实时预览"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;
        private string _packPath;
        private PackAnalysis _analysis;
        private string _originalText;

        private TextBox _pathBox, _descBox, _rawBox, _log;
        private ComboBox _verCombo;
        private Panel _gamePreview;
        private Label _info;
        private FlatBtn _saveBtn, _revertBtn;
        private PictureBox _iconBox;
        private ToolCheck _keepRange;

        public void OnEnter() { }
        public void OnLeave() { if (_analysis != null) { _analysis.Cleanup(); _analysis = null; } }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildSourceCard());
            stack.Controls.Add(BuildEditCard());
            stack.Controls.Add(BuildRawCard());

            _view = host;
            return host;
        }

        /* ------------------------------------------------------------ */

        private Card BuildSourceCard()
        {
            var card = new Card { Title = "① 选择资源包", Hint = "文件夹或 ZIP 都可以", Height = 168, Margin = new Padding(0, 0, 0, 12) };

            _pathBox = Ui.ReadOnlyBox("");
            _pathBox.Font = Theme.M(8.5f);
            FlatBtn browse, zipBtn, loadBtn;
            var row1 = PageKit.PathRow(_pathBox, "选文件夹…", () =>
            {
                string dir = PageKit.PickFolder(_view, "选择资源包文件夹");
                if (dir != null) { _pathBox.Text = dir; DoLoad(); }
            }, out browse);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            zipBtn = new FlatBtn("打开 ZIP…", BtnKind.Outline, 96) { Margin = new Padding(0, 0, 8, 0) };
            loadBtn = new FlatBtn("读取", BtnKind.Primary, 74) { Margin = new Padding(0, 0, 8, 0) };
            zipBtn.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "打开资源包 ZIP";
                    dlg.Filter = "Minecraft 资源包 (*.zip)|*.zip";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    _pathBox.Text = dlg.FileName;
                    DoLoad();
                }
            };
            loadBtn.Click += (s, e) => DoLoad();
            bar.Controls.Add(zipBtn);
            bar.Controls.Add(loadBtn);

            _info = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(8.8f),
                BackColor = Color.Transparent,
            };

            card.Body.Controls.Add(_info);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("资源包位置"));
            return card;
        }

        private Card BuildEditCard()
        {
            var card = new Card { Title = "② 编辑", Hint = "保存前不会改动原文件", Height = 330, Margin = new Padding(0, 0, 0, 12) };

            /* 游戏内预览 */
            _gamePreview = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.FromArgb(16, 16, 16),
            };
            _gamePreview.Paint += (s, e) => PaintGamePreview(e.Graphics);

            /* 描述 */
            _descBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
            _descBox.TextChanged += (s, e) => { _gamePreview.Invalidate(); SyncRawFromFields(); };
            var rowD = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowD.Controls.Add(_descBox);

            /* 版本 */
            _verCombo = new ComboBox { Dock = DockStyle.Left, Width = 400 };
            Ui.StyleCombo(_verCombo);
            _verCombo.Items.Add("（不改动版本声明）");
            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format))
                _verCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            _verCombo.SelectedIndex = 0;
            _verCombo.SelectedIndexChanged += (s, e) => SyncRawFromFields();

            _keepRange = new ToolCheck("保留 min/max_format 范围写法", false, 240)
            {
                Dock = DockStyle.Left,
                Padding = new Padding(12, 3, 0, 0),
            };
            var rowV = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowV.Controls.Add(_keepRange);
            rowV.Controls.Add(_verCombo);

            /* 图标 */
            _iconBox = new PictureBox
            {
                Width = 64,
                Height = 64,
                BackColor = Theme.BgInput,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };
            var iconRow = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.Transparent };
            var pickIcon = new FlatBtn("替换 pack.png…", BtnKind.Outline, 130) { Location = new Point(76, 4) };
            var genIcon = new FlatBtn("生成图标…", BtnKind.Outline, 110) { Location = new Point(214, 4) };
            pickIcon.Click += (s, e) => ReplaceIcon();
            genIcon.Click += (s, e) => GenerateIcon();
            iconRow.Controls.Add(_iconBox);
            iconRow.Controls.Add(pickIcon);
            iconRow.Controls.Add(genIcon);
            _iconBox.Location = new Point(0, 2);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _saveBtn = new FlatBtn("保存到资源包", BtnKind.Primary, 130) { Margin = new Padding(0, 0, 8, 0), Enabled = false };
            _revertBtn = new FlatBtn("放弃改动", BtnKind.Ghost, 96) { Enabled = false };
            _saveBtn.Click += (s, e) => DoSave();
            _revertBtn.Click += (s, e) => DoLoad();
            bar.Controls.Add(_saveBtn);
            bar.Controls.Add(_revertBtn);

            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(iconRow);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("图标 pack.png"));
            card.Body.Controls.Add(rowV);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("版本声明"));
            card.Body.Controls.Add(rowD);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("描述（支持 § 颜色码，例如 §6金色 §l加粗）"));
            card.Body.Controls.Add(_gamePreview);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("游戏内预览"));
            return card;
        }

        private Card BuildRawCard()
        {
            var card = new Card { Title = "③ 原始 JSON", Hint = "直接编辑；与上面的表单双向同步", Height = 260, Margin = new Padding(0, 0, 0, 12) };

            _rawBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.M(9f),
                AcceptsTab = true,
            };
            _log = Ui.LogBox();
            _log.Height = 46;

            card.Body.Controls.Add(_rawBox);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(_log);
            return card;
        }

        /* ------------------------------------------------------------ */
        /* 游戏内预览：把 § 颜色码渲染成彩色文字                                */
        /* ------------------------------------------------------------ */

        private static readonly Dictionary<char, Color> CodeColors = new Dictionary<char, Color>
        {
            { '0', Color.FromArgb(0, 0, 0) },
            { '1', Color.FromArgb(0, 0, 170) },
            { '2', Color.FromArgb(0, 170, 0) },
            { '3', Color.FromArgb(0, 170, 170) },
            { '4', Color.FromArgb(170, 0, 0) },
            { '5', Color.FromArgb(170, 0, 170) },
            { '6', Color.FromArgb(255, 170, 0) },
            { '7', Color.FromArgb(170, 170, 170) },
            { '8', Color.FromArgb(85, 85, 85) },
            { '9', Color.FromArgb(85, 85, 255) },
            { 'a', Color.FromArgb(85, 255, 85) },
            { 'b', Color.FromArgb(85, 255, 255) },
            { 'c', Color.FromArgb(255, 85, 85) },
            { 'd', Color.FromArgb(255, 85, 255) },
            { 'e', Color.FromArgb(255, 255, 85) },
            { 'f', Color.FromArgb(255, 255, 255) },
        };

        private void PaintGamePreview(Graphics g)
        {
            g.Clear(Color.FromArgb(16, 16, 16));
            int w = _gamePreview.Width;

            using (var f = Theme.F(11f, FontStyle.Bold))
            using (var small = Theme.F(8f))
            {
                /* 顶部一行小字，说明这是模拟 */
                using (var b = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    g.DrawString("游戏内「资源包」列表里的样子（模拟）", small, b, new PointF(14, 6));

                string text = _descBox != null ? _descBox.Text : "";
                if (string.IsNullOrEmpty(text)) text = "（无描述）";

                float x = 14, y = 26;
                float lineH = f.GetHeight(g) + 2;

                Color cur = Color.FromArgb(255, 255, 255);
                bool bold = false, italic = false, underline = false, strike = false;

                var sb = new StringBuilder();
                FontStyle style = FontStyle.Regular;

                Action flush = () =>
                {
                    if (sb.Length == 0) return;
                    style = FontStyle.Regular;
                    if (bold) style |= FontStyle.Bold;
                    if (italic) style |= FontStyle.Italic;
                    if (underline) style |= FontStyle.Underline;

                    using (var ff = new Font(f.FontFamily, f.Size, style))
                    using (var bb = new SolidBrush(cur))
                    {
                        string seg = sb.ToString();
                        var size = g.MeasureString(seg, ff);
                        if (x + size.Width > w - 14) { x = 14; y += lineH; }
                        g.DrawString(seg, ff, bb, new PointF(x, y));

                        if (strike && seg.Length > 0)
                        {
                            using (var pen = new Pen(cur, 1.4f))
                                g.DrawLine(pen, x, y + ff.GetHeight(g) / 2, x + size.Width, y + ff.GetHeight(g) / 2);
                        }
                        x += size.Width;
                    }
                    sb.Length = 0;
                };

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c != '\u00A7' && c != '&') { sb.Append(c); continue; }

                    if (i + 1 >= text.Length) { sb.Append(c); continue; }
                    char code = char.ToLowerInvariant(text[i + 1]);
                    i++;

                    flush();

                    if (CodeColors.ContainsKey(code)) { cur = CodeColors[code]; bold = italic = underline = strike = false; }
                    else if (code == 'l') bold = true;
                    else if (code == 'o') italic = true;
                    else if (code == 'n') underline = true;
                    else if (code == 'm') strike = true;
                    else if (code == 'r') { cur = Color.FromArgb(255, 255, 255); bold = italic = underline = strike = false; }
                    else sb.Append(code);
                }
                flush();
            }
        }

        /* ------------------------------------------------------------ */
        /* 读写                                                                */
        /* ------------------------------------------------------------ */

        private void DoLoad()
        {
            string path = _pathBox.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                PageKit.Error(_view, "先选择资源包。");
                return;
            }

            if (_analysis != null) { _analysis.Cleanup(); _analysis = null; }

            var a = PackAnalyzer.Analyze(path);
            _analysis = a;

            if (!a.Ok)
            {
                _info.Text = "读取失败：" + a.Error;
                _saveBtn.Enabled = false;
                _revertBtn.Enabled = false;
                PageKit.Error(_view, a.Error);
                Status("读取失败：" + a.Error);
                return;
            }

            string mcmeta = Path.Combine(a.RootDir, "pack.mcmeta");
            if (!File.Exists(mcmeta))
            {
                _info.Text = "这个资源包根目录没有 pack.mcmeta。";
                _saveBtn.Enabled = false;
                _revertBtn.Enabled = false;
                return;
            }

            _originalText = File.ReadAllText(mcmeta, Encoding.UTF8);
            _rawBox.Text = _originalText;

            /* 表单字段 */
            JsonValue root; string err;
            if (Json.TryParse(_originalText, out root, out err) && root != null)
            {
                var pack = root.Get("pack");
                _descBox.Text = pack != null && pack.Get("description") != null
                    ? pack.Get("description").AsText() : "";

                /* 版本下拉定位到当前格式 */
                var f = a.Format;
                int cur = f != null && f.EffectiveFormat.HasValue ? f.EffectiveFormat.Value : -1;
                _verCombo.SelectedIndex = 0;
                for (int i = 0; i < _verCombo.Items.Count; i++)
                {
                    var ci = _verCombo.Items[i] as ComboItem;
                    if (ci != null && ci.Format == cur) { _verCombo.SelectedIndex = i; break; }
                }
                _keepRange.Checked = f != null && f.UsesNewFormat;
            }
            else
            {
                _descBox.Text = "";
                _log.Text = "pack.mcmeta 不是合法 JSON：" + err;
            }

            string iconPath = Path.Combine(a.RootDir, "pack.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    using (var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                    using (var img = Image.FromStream(fs))
                        _iconBox.Image = new Bitmap(img);
                    _iconBox.Tag = iconPath;
                }
                catch { _iconBox.Image = null; _iconBox.Tag = null; }
            }
            else
            {
                _iconBox.Image = null;
                _iconBox.Tag = null;
            }

            _info.Text = a.DisplayName + "　·　" + (a.Format != null ? a.Format.DescribeCurrent() : "版本未知")
                         + "　·　" + a.Counts.Total + " 个文件"
                         + (a.IsZip ? "　·　来自 ZIP（保存时会重新打包）" : "");

            _saveBtn.Enabled = true;
            _revertBtn.Enabled = true;
            _gamePreview.Invalidate();
            Status("已读取：" + a.DisplayName);
        }

        /// <summary>表单改动时同步到原始 JSON 文本框</summary>
        private void SyncRawFromFields()
        {
            if (_rawBox == null) return;
            string text = _rawBox.Text;
            JsonValue root; string err;
            if (!Json.TryParse(text, out root, out err) || root == null || !root.IsObject) return;

            var pack = root.Get("pack");
            if (pack == null || !pack.IsObject) { pack = JsonValue.Obj(); root.Set("pack", pack); }

            pack.Set("description", JsonValue.Str(_descBox.Text));

            var ci = _verCombo.SelectedItem as ComboItem;
            if (ci != null)
            {
                if (!_keepRange.Checked)
                {
                    pack.Remove("min_format");
                    pack.Remove("max_format");
                    pack.Remove("supported_formats");
                }
                pack.Set("pack_format", JsonValue.Num(ci.Format));
            }

            _rawBox.Text = Json.Write(root, true);
        }

        private void ReplaceIcon()
        {
            if (_analysis == null) { PageKit.Error(_view, "先读取一个资源包。"); return; }

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择图标图片";
                dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;

                string err;
                byte[] png = IconMaker.ResizeToPng(dlg.FileName, 64, out err);
                if (png == null) { PageKit.Error(_view, err); return; }

                string target = Path.Combine(_analysis.RootDir, "pack.png");
                try
                {
                    File.WriteAllBytes(target, png);
                    using (var ms = new MemoryStream(png))
                        _iconBox.Image = Image.FromStream(ms);
                    Status("图标已更新（64×64）");
                }
                catch (Exception e)
                {
                    PageKit.Error(_view, "写入图标失败：" + e.Message);
                }
            }
        }

        private void GenerateIcon()
        {
            if (_analysis == null) { PageKit.Error(_view, "先读取一个资源包。"); return; }
            using (var dlg = new ColorDialog())
            {
                dlg.Color = Color.FromArgb(88, 132, 255);
                dlg.FullOpen = true;
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;

                byte[] png = IconMaker.Make(64, IconPattern.PackIcon, dlg.Color, Color.FromArgb(24, 28, 38), 16, null);
                string target = Path.Combine(_analysis.RootDir, "pack.png");
                try
                {
                    File.WriteAllBytes(target, png);
                    using (var ms = new MemoryStream(png))
                        _iconBox.Image = Image.FromStream(ms);
                    Status("已生成新的 pack.png");
                }
                catch (Exception e)
                {
                    PageKit.Error(_view, "写入图标失败：" + e.Message);
                }
            }
        }

        private void DoSave()
        {
            if (_analysis == null) { PageKit.Error(_view, "先读取一个资源包。"); return; }

            string text = _rawBox.Text;
            JsonValue root; string err;
            if (!Json.TryParse(text, out root, out err))
            {
                PageKit.Error(_view, "pack.mcmeta 不是合法 JSON，已阻止保存：\n\n" + err);
                Status("保存被阻止：JSON 不合法");
                return;
            }

            if (!PageKit.Confirm(_view, "即将把改动写回资源包：\n\n" + _analysis.SourcePath + "\n\n继续吗？")) return;

            _saveBtn.Enabled = false;
            try
            {
                if (_analysis.IsZip)
                {
                    /* ZIP：整体重新打包（用暂存 + 原子替换） */
                    string zipPath = _analysis.SourcePath;
                    string rootDir = _analysis.RootDir;
                    File.WriteAllText(Path.Combine(rootDir, "pack.mcmeta"), Json.Write(root, true) + "\n",
                                      new UTF8Encoding(false));

                    var report = StagedWriter.Write(zipPath, StageOutput.Zip, staging =>
                    {
                        StagedWriter.CopyInto(rootDir, staging, null);
                    }, null);

                    if (report.Ok) { Status("已写回 ZIP：" + Path.GetFileName(zipPath)); }
                    else { PageKit.Error(_view, "写回 ZIP 失败：" + report.Error); }
                }
                else
                {
                    string mcmeta = Path.Combine(_analysis.RootDir, "pack.mcmeta");
                    /* 改动前留一份备份 */
                    if (File.Exists(mcmeta) && _originalText != null)
                    {
                        string bak = mcmeta + ".bak";
                        try { File.WriteAllText(bak, _originalText, new UTF8Encoding(false)); } catch { }
                    }
                    File.WriteAllText(mcmeta, Json.Write(root, true) + "\n", new UTF8Encoding(false));
                    Status("已保存 pack.mcmeta");
                }

                _originalText = File.ReadAllText(
                    Path.Combine(_analysis.RootDir, "pack.mcmeta"), Encoding.UTF8);

                /* 重新分析，刷新信息行 */
                string saved = _analysis.SourcePath;
                if (_analysis != null) _analysis.Cleanup();
                _analysis = PackAnalyzer.Analyze(saved);
                if (_analysis.Ok && _analysis.Format != null)
                    _info.Text = _analysis.DisplayName + "　·　" + _analysis.Format.DescribeCurrent()
                                 + "　·　" + _analysis.Counts.Total + " 个文件";

                MessageBox.Show(_view, "已保存。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "保存失败：" + e.Message);
                Status("保存失败：" + e.Message);
            }
            finally
            {
                _saveBtn.Enabled = true;
            }
        }
    }
}
