using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 工具箱 · 图标与占位图                                                */
    /* ================================================================== */

    internal sealed class IconPage : IModulePage
    {
        public string Title { get { return "图标与占位图"; } }
        public string Subtitle { get { return "生成 pack.png 资源包图标与纯色 / 网格 / 透明占位贴图"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;

        private FlatSelect _pattern, _size;
        private NumericUpDown _gridStep, _bulkCount;
        private Panel _swatch, _swatch2;
        private TextBox _outBox, _importBox;
        private RichTextBox _log;
        private PictureBox _preview;
        private FlatBtn _genBtn, _bulkBtn, _importBtn;
        private Label _sizeInfo;

        private Color _c1 = Color.FromArgb(88, 132, 255);
        private Color _c2 = Color.FromArgb(24, 28, 38);
        private readonly List<string> _bulkSizes = new List<string>();

        public void OnEnter() { }
        public void OnLeave() { }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildDesignCard());
            stack.Controls.Add(BuildBulkCard());
            stack.Controls.Add(BuildImportCard());

            _view = host;
            RefreshPreview();
            return host;
        }

        /* ------------------------------------------------------------ */

        private Card BuildDesignCard()
        {
            var card = new Card { Title = "① 生成图片", Hint = "生成的是标准 PNG，可直接丢进资源包", Height = 400, Margin = new Padding(0, 0, 0, 12) };

            /* 预览 */
            _preview = new PictureBox
            {
                Dock = DockStyle.Right,
                Width = 220,
                BackColor = Theme.BgInput,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };

            /* 左列参数 */
            var left = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            _pattern = new FlatSelect { Dock = DockStyle.Left, Width = 220 };
            Ui.StyleSelect(_pattern);
            _pattern.Items.Add("资源包图标（圆角 + 横带）");
            _pattern.Items.Add("纯色");
            _pattern.Items.Add("透明（全空）");
            _pattern.Items.Add("网格（占位方块）");
            _pattern.Items.Add("棋盘格");
            _pattern.Items.Add("十字（纹理占位）");
            _pattern.SelectedIndex = 0;
            _pattern.SelectedIndexChanged += (s, e) => RefreshPreview();
            var rowP = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowP.Controls.Add(_pattern);

            _size = new FlatSelect { Dock = DockStyle.Left, Width = 140 };
            Ui.StyleSelect(_size);
            foreach (int s in new[] { 16, 32, 64, 128, 256, 512 })
                _size.Items.Add(s + " × " + s);
            _size.SelectedIndex = 2;   // 64
            _size.SelectedIndexChanged += (s, e) => RefreshPreview();
            _sizeInfo = new Label
            {
                Dock = DockStyle.Left,
                Width = 300,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                Padding = new Padding(12, 7, 0, 0),
                BackColor = Color.Transparent,
            };
            var rowS = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowS.Controls.Add(_sizeInfo);
            rowS.Controls.Add(_size);

            _gridStep = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 70,
                Minimum = 1,
                Maximum = 512,
                Value = 16,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
            _gridStep.ValueChanged += (s, e) => RefreshPreview();
            var rowG = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowG.Controls.Add(_gridStep);

            _swatch = MakeSwatch(_c1, c => { _c1 = c; RefreshPreview(); });
            _swatch2 = MakeSwatch(_c2, c => { _c2 = c; RefreshPreview(); });
            var rowC = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.Transparent };
            rowC.Controls.Add(_swatch2);
            rowC.Controls.Add(_swatch);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _genBtn = new FlatBtn("生成并保存…", BtnKind.Primary, 120) { Margin = new Padding(0, 0, 8, 0) };
            var saveAs = new FlatBtn("另存为 pack.png", BtnKind.Outline, 130) { Margin = new Padding(0, 0, 8, 0) };
            _genBtn.Click += (s, e) => DoGenerate();
            saveAs.Click += (s, e) => DoSaveAsPackIcon();
            bar.Controls.Add(_genBtn);
            bar.Controls.Add(saveAs);

            left.Controls.Add(rowC);
            left.Controls.Add(PageKit.Spacer(6));
            left.Controls.Add(PageKit.MakeLabel("颜色（底色 / 图案色）—— 点色块可改"));
            left.Controls.Add(rowG);
            left.Controls.Add(PageKit.Spacer(6));
            left.Controls.Add(PageKit.MakeLabel("网格间距（像素，仅网格 / 棋盘格用）"));
            left.Controls.Add(rowS);
            left.Controls.Add(PageKit.Spacer(6));
            left.Controls.Add(PageKit.MakeLabel("尺寸"));
            left.Controls.Add(rowP);
            left.Controls.Add(PageKit.Spacer(6));
            left.Controls.Add(PageKit.MakeLabel("图案"));
            left.Controls.Add(bar);
            left.Controls.Add(PageKit.Spacer(8));
            left.Controls.Add(PageKit.MakeLabel("生成后的操作"));

            card.Body.Controls.Add(left);
            card.Body.Controls.Add(_preview);
            return card;
        }

        private Panel MakeSwatch(Color initial, Action<Color> onPick)
        {
            var p = new Panel
            {
                Dock = DockStyle.Left,
                Width = 110,
                Height = 34,
                BackColor = initial,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
            };
            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.LineStrong, 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            p.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog())
                {
                    dlg.Color = p.BackColor;
                    dlg.FullOpen = true;
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    p.BackColor = dlg.Color;
                    onPick(dlg.Color);
                }
            };
            return p;
        }

        private int CurrentSize()
        {
            switch (_size.SelectedIndex)
            {
                case 0: return 16;
                case 1: return 32;
                case 2: return 64;
                case 3: return 128;
                case 4: return 256;
                case 5: return 512;
                default: return 64;
            }
        }

        private IconPattern CurrentPattern()
        {
            switch (_pattern.SelectedIndex)
            {
                case 1: return IconPattern.Solid;
                case 2: return IconPattern.Transparent;
                case 3: return IconPattern.Grid;
                case 4: return IconPattern.Checker;
                case 5: return IconPattern.Cross;
                default: return IconPattern.PackIcon;
            }
        }

        private byte[] Render(out int size)
        {
            size = CurrentSize();
            return IconMaker.Make(size, CurrentPattern(), _c1, _c2, (int)_gridStep.Value, null);
        }

        private byte[] _lastPng;

        private void RefreshPreview()
        {
            try
            {
                int size;
                _lastPng = Render(out size);

                if (_sizeInfo != null)
                    _sizeInfo.Text = size + "×" + size + " RGBA　·　" + Ui.FormatBytes(_lastPng.Length);

                if (_preview != null)
                {
                    using (var ms = new MemoryStream(_lastPng))
                    {
                        var old = _preview.Image;
                        _preview.Image = Image.FromStream(ms);
                        if (old != null) old.Dispose();
                    }
                }
            }
            catch (Exception e)
            {
                Status("预览失败：" + e.Message);
            }
        }

        private void DoGenerate()
        {
            string name = "icon_" + CurrentSize() + "x" + CurrentSize() + ".png";
            if (CurrentPattern() == IconPattern.PackIcon) name = "pack.png";

            string path = PageKit.PickSave(_view, "保存图片", "PNG 图片 (*.png)|*.png", name);
            if (path == null) return;

            try
            {
                File.WriteAllBytes(path, _lastPng);
                Status("已保存：" + path);
                MessageBox.Show(_view, "已保存：\n\n" + path, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "保存失败：" + e.Message);
            }
        }

        /// <summary>直接写进某个资源包的根目录，覆盖它的 pack.png</summary>
        private void DoSaveAsPackIcon()
        {
            string dir = PageKit.PickFolder(_view, "选择要写入图标的目标资源包文件夹");
            if (dir == null) return;

            if (!File.Exists(Path.Combine(dir, "pack.mcmeta")))
            {
                if (!PageKit.Confirm(_view,
                    "这个文件夹里没有 pack.mcmeta，看起来不是资源包。\n\n仍然要把 pack.png 写进去吗？")) return;
            }

            string target = Path.Combine(dir, "pack.png");
            try
            {
                if (File.Exists(target))
                {
                    string bak = target + ".bak_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Copy(target, bak, true);
                    Status("原有 pack.png 已备份为 " + Path.GetFileName(bak));
                }

                byte[] png = IconMaker.Make(64, IconPattern.PackIcon, _c1, _c2, (int)_gridStep.Value, null);
                File.WriteAllBytes(target, png);
                Status("已写入 pack.png（64×64）：" + target);
                MessageBox.Show(_view, "已写入：\n\n" + target, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "写入失败：" + e.Message);
            }
        }

        /* ------------------------------------------------------------ */

        private Card BuildBulkCard()
        {
            var card = new Card { Title = "② 批量导出多个尺寸", Hint = "一次生成同一图案的多种尺寸，常用于测试不同分辨率下的效果", Height = 226, Margin = new Padding(0, 0, 0, 12) };

            _outBox = Ui.ReadOnlyBox("");
            _outBox.Font = Theme.M(8.5f);
            FlatBtn browse;
            var row1 = PageKit.PathRow(_outBox, "浏览…", () =>
            {
                string dir = PageKit.PickFolder(_view, "选择导出目录");
                if (dir != null) _outBox.Text = dir;
            }, out browse);

            _bulkCount = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 70,
                Minimum = 1,
                Maximum = 13,
                Value = 6,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
            var hint = new Label
            {
                Dock = DockStyle.Left,
                Width = 420,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                Padding = new Padding(12, 7, 0, 0),
                Text = "从 16×16 起，每级翻倍（16, 32, 64, 128, 256, 512, 1024…）",
                BackColor = Color.Transparent,
            };
            var row2 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row2.Controls.Add(hint);
            row2.Controls.Add(_bulkCount);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _bulkBtn = new FlatBtn("批量导出", BtnKind.Primary, 110);
            _bulkBtn.Click += (s, e) => DoBulk();
            bar.Controls.Add(_bulkBtn);

            _log = Ui.LogBox();
            _log.Height = 60;

            card.Body.Controls.Add(_log);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(row2);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("导出几个尺寸"));
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("导出目录"));
            return card;
        }

        private void DoBulk()
        {
            if (string.IsNullOrWhiteSpace(_outBox.Text))
            {
                PageKit.Error(_view, "先选择导出目录。");
                return;
            }

            int count = (int)_bulkCount.Value;
            var sizes = new List<int>();
            int s = 16;
            for (int i = 0; i < count && s <= 4096; i++) { sizes.Add(s); s *= 2; }

            _log.Clear();
            _bulkBtn.Enabled = false;

            try
            {
                Directory.CreateDirectory(_outBox.Text);
                int ok = 0;
                foreach (int sz in sizes)
                {
                    byte[] png = IconMaker.Make(sz, CurrentPattern(), _c1, _c2, (int)_gridStep.Value, null);
                    string name = "icon_" + sz + "x" + sz + ".png";
                    string path = Path.Combine(_outBox.Text, name);
                    File.WriteAllBytes(path, png);
                    Ui.AppendLog(_log, "✓ " + name + "   " + Ui.FormatBytes(png.Length));
                    ok++;
                }
                Status("批量导出完成：" + ok + " 个文件 → " + _outBox.Text);
            }
            catch (Exception e)
            {
                Ui.AppendLog(_log, "失败：" + e.Message);
                PageKit.Error(_view, "导出失败：" + e.Message);
            }
            finally
            {
                _bulkBtn.Enabled = true;
            }
        }

        /* ------------------------------------------------------------ */

        private Card BuildImportCard()
        {
            var card = new Card { Title = "③ 从现有图片生成图标", Hint = "把任意图片缩放成 64×64 的 pack.png", Height = 190, Margin = new Padding(0, 0, 0, 12) };

            _importBox = Ui.ReadOnlyBox("");
            _importBox.Font = Theme.M(8.5f);
            FlatBtn browse;
            var row1 = PageKit.PathRow(_importBox, "选择图片…", () =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择图片";
                    dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(_view) == DialogResult.OK) _importBox.Text = dlg.FileName;
                }
            }, out browse);

            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            _importBox.TextChanged += (s, e) =>
            {
                info.Text = string.IsNullOrWhiteSpace(_importBox.Text) ? "" : IconMaker.Describe(_importBox.Text);
            };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _importBtn = new FlatBtn("缩放并保存 pack.png…", BtnKind.Primary, 170) { Margin = new Padding(0, 0, 8, 0) };
            var toPreview = new FlatBtn("载入到预览", BtnKind.Outline, 110);
            _importBtn.Click += (s, e) => DoImport();
            toPreview.Click += (s, e) => LoadIntoPreview();
            bar.Controls.Add(_importBtn);
            bar.Controls.Add(toPreview);

            card.Body.Controls.Add(info);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("源图片"));
            return card;
        }

        private void LoadIntoPreview()
        {
            string src = _importBox.Text;
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
            {
                PageKit.Error(_view, "先选一张存在的图片。");
                return;
            }
            string err;
            byte[] png = IconMaker.ResizeToPng(src, CurrentSize(), out err);
            if (png == null) { PageKit.Error(_view, err); return; }

            using (var ms = new MemoryStream(png))
            {
                var old = _preview.Image;
                _preview.Image = Image.FromStream(ms);
                if (old != null) old.Dispose();
            }
            Status("已载入到预览（" + CurrentSize() + "×" + CurrentSize() + "）");
        }

        private void DoImport()
        {
            string src = _importBox.Text;
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
            {
                PageKit.Error(_view, "先选一张存在的图片。");
                return;
            }

            string path = PageKit.PickSave(_view, "保存为 pack.png", "PNG 图片 (*.png)|*.png", "pack.png");
            if (path == null) return;

            string err;
            byte[] png = IconMaker.ResizeToPng(src, 64, out err);
            if (png == null) { PageKit.Error(_view, err); return; }

            try
            {
                File.WriteAllBytes(path, png);
                Status("已保存：" + path + "（64×64）");
                MessageBox.Show(_view, "已保存为 64×64 的 PNG：\n\n" + path, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "保存失败：" + e.Message);
            }
        }
    }
}
