using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 资源包 · 新建资源包                                                  */
    /* ================================================================== */

    internal sealed class PackNewPage : IModulePage
    {
        public string Title { get { return "新建资源包"; } }
        public string Subtitle { get { return "从零生成符合所选版本规范的目录结构与 pack.mcmeta"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;
        private TextBox _dirBox, _nameBox, _descBox;
        private TextBox _nsBox;
        private FlatSelect _verCombo;
        private ToolCheck _cTex, _cBlock, _cItem, _cModel, _cLang, _cSound, _cItems, _cIcon, _cSamples;
        private RichTextBox _preview;
        private FlatBtn _createBtn;
        private Label _hint;
        private int _iconColor = Color.FromArgb(88, 132, 255).ToArgb();

        public void OnEnter() { }
        public void OnLeave() { }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildBasicCard());
            stack.Controls.Add(BuildContentCard());
            stack.Controls.Add(BuildPreviewCard());

            _view = host;
            RefreshPreview();
            return host;
        }

        private Card BuildBasicCard()
        {
            var card = new Card { Title = "① 基本信息", Hint = "输出到一个新目录；目录已存在时先备份再替换", Height = 250, Margin = new Padding(0, 0, 0, 12) };

            _dirBox = Ui.ReadOnlyBox("");
            _dirBox.Font = Theme.M(8.5f);
            FlatBtn browse;
            var row1 = PageKit.PathRow(_dirBox, "浏览…", () =>
            {
                string dir = PageKit.PickFolder(_view, "选择要创建资源包的位置");
                if (dir != null)
                {
                    _dirBox.Text = Path.Combine(dir, SafeName(_nameBox.Text));
                    RefreshPreview();
                }
            }, out browse);

            _nameBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
                Text = "MyResourcePack",
            };
            _nameBox.TextChanged += (s, e) =>
            {
                /* 名字变了，输出目录跟着变（只改最后一段） */
                if (string.IsNullOrWhiteSpace(_dirBox.Text)) return;
                try
                {
                    string parent = Path.GetDirectoryName(_dirBox.Text.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(parent))
                        _dirBox.Text = Path.Combine(parent, SafeName(_nameBox.Text));
                }
                catch { }
                RefreshPreview();
            };
            var row2 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row2.Controls.Add(_nameBox);

            /* 命名空间允许自由输入（不只是下拉里那几个），所以用 TextBox */
            _nsBox = new TextBox
            {
                Dock = DockStyle.Left,
                Width = 220,
                Text = "minecraft",
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9.5f),
            };
            _nsBox.TextChanged += (s, e) => RefreshPreview();
            var row3 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row3.Controls.Add(_nsBox);

            _descBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
                Text = "My Resource Pack",
            };
            _descBox.TextChanged += (s, e) => RefreshPreview();
            var row4 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row4.Controls.Add(_descBox);

            _verCombo = new FlatSelect { Dock = DockStyle.Left, Width = 400 };
            Ui.StyleSelect(_verCombo);
            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format))
                _verCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            /* 默认选一个较新的正式版 */
            int defIdx = 0;
            for (int i = 0; i < _verCombo.Items.Count; i++)
            {
                var ci = _verCombo.Items[i] as ComboItem;
                if (ci != null && ci.Format == 46) { defIdx = i; break; }
            }
            _verCombo.SelectedIndex = defIdx;
            _verCombo.SelectedIndexChanged += (s, e) => RefreshPreview();
            var row5 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row5.Controls.Add(_verCombo);

            _hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.Accent,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };

            card.Body.Controls.Add(_hint);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(row5);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("目标版本"));
            card.Body.Controls.Add(row4);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("描述（显示在游戏里，支持 § 颜色码）"));
            card.Body.Controls.Add(row3);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("命名空间（assets/ 下的第一级目录）"));
            card.Body.Controls.Add(row2);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("资源包名称"));
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("输出目录"));
            return card;
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "NewPack";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim()) sb.Append(bad.Contains(c) ? '_' : c);
            return sb.Length == 0 ? "NewPack" : sb.ToString();
        }

        private Card BuildContentCard()
        {
            var card = new Card { Title = "② 预置内容", Hint = "按需勾选；生成的都是空目录或最小示例，可直接替换", Height = 210, Margin = new Padding(0, 0, 0, 12) };

            _cTex = new ToolCheck("textures/", true, 110) { Dock = DockStyle.Left };
            _cBlock = new ToolCheck("方块纹理", true, 100) { Dock = DockStyle.Left, Padding = new Padding(16, 3, 0, 0) };
            _cItem = new ToolCheck("物品纹理", true, 100) { Dock = DockStyle.Left, Padding = new Padding(16, 3, 0, 0) };
            var row1 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row1.Controls.Add(_cItem); row1.Controls.Add(_cBlock); row1.Controls.Add(_cTex);

            _cModel = new ToolCheck("models/", true, 110) { Dock = DockStyle.Left };
            _cItems = new ToolCheck("items/（1.21.4+ 物品定义）", true, 240) { Dock = DockStyle.Left, Padding = new Padding(16, 3, 0, 0) };
            var row2 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row2.Controls.Add(_cItems); row2.Controls.Add(_cModel);

            _cLang = new ToolCheck("lang/", true, 110) { Dock = DockStyle.Left };
            _cSound = new ToolCheck("sounds/", true, 110) { Dock = DockStyle.Left, Padding = new Padding(16, 3, 0, 0) };
            var row3 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row3.Controls.Add(_cSound); row3.Controls.Add(_cLang);

            _cIcon = new ToolCheck("生成 pack.png 图标", true, 180) { Dock = DockStyle.Left };
            _cSamples = new ToolCheck("写入示例文件（语言、sounds.json、示例纹理）", true, 340) { Dock = DockStyle.Left, Padding = new Padding(16, 3, 0, 0) };
            var row4 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row4.Controls.Add(_cSamples); row4.Controls.Add(_cIcon);

            var colorBtn = new FlatBtn("图标颜色…", BtnKind.Outline, 100) { Dock = DockStyle.Left, Margin = new Padding(0, 0, 8, 0) };
            colorBtn.Click += (s, e) => PickColor();
            var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            bar.Controls.Add(colorBtn);

            foreach (Control c in new Control[] { _cTex, _cBlock, _cItem, _cModel, _cLang, _cSound, _cItems, _cIcon, _cSamples })
                c.Click += (a, b) => RefreshPreview();

            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(row4);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(row3);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(row2);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(row1);
            return card;
        }

        private void PickColor()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.Color = Color.FromArgb(_iconColor);
                dlg.FullOpen = true;
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                _iconColor = dlg.Color.ToArgb();
                Status("图标颜色已设为 " + dlg.Color.Name);
            }
        }

        private Card BuildPreviewCard()
        {
            var card = new Card { Title = "③ 预览与创建", Hint = "下面是即将生成的目录结构", Height = 300, Margin = new Padding(0, 0, 0, 12) };

            _preview = Ui.LogBox();
            _preview.Font = Theme.M(8.5f);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _createBtn = new FlatBtn("创建资源包", BtnKind.Primary, 120) { Margin = new Padding(0, 0, 8, 0) };
            var refresh = new FlatBtn("刷新预览", BtnKind.Ghost, 88);
            _createBtn.Click += (s, e) => DoCreate();
            refresh.Click += (s, e) => RefreshPreview();
            bar.Controls.Add(_createBtn);
            bar.Controls.Add(refresh);

            card.Body.Controls.Add(_preview);
            card.Body.Controls.Add(PageKit.Spacer(8));
            card.Body.Controls.Add(bar);
            return card;
        }

        private ScaffoldOptions CurrentOptions()
        {
            var ci = _verCombo.SelectedItem as ComboItem;
            int fmt = ci != null ? ci.Format : 15;

            return new ScaffoldOptions
            {
                OutputDir = _dirBox.Text,
                PackFormat = fmt,
                Description = _descBox.Text,
                Namespace = string.IsNullOrWhiteSpace(_nsBox.Text) ? "minecraft" : _nsBox.Text.Trim(),
                WithTextures = _cTex.Checked,
                WithBlockTextures = _cTex.Checked && _cBlock.Checked,
                WithItemTextures = _cTex.Checked && _cItem.Checked,
                WithModels = _cModel.Checked,
                WithLang = _cLang.Checked,
                WithSounds = _cSound.Checked,
                WithItems = _cItems.Checked,
                WithPackIcon = _cIcon.Checked,
                WithSamples = _cSamples.Checked,
                IconColor = Color.FromArgb(_iconColor),
            };
        }

        private void RefreshPreview()
        {
            if (_preview == null) return;
            var o = CurrentOptions();

            string blockDir = PackScaffold.BlockTextureDir(o.PackFormat);
            string itemDir = PackScaffold.ItemTextureDir(o.PackFormat);
            bool itemsDir = o.WithItems || PackScaffold.UsesItemsDir(o.PackFormat);

            if (_hint != null)
                _hint.Text = PackScaffold.UsesItemsDir(o.PackFormat)
                    ? "pack_format " + o.PackFormat + " 属于 1.21.4+，物品定义放在 items/，" + "方块纹理用 textures/" + blockDir
                    : "pack_format " + o.PackFormat + " < 46，物品定义仍用 models/item，" + "方块纹理用 textures/" + blockDir;

            var sb = new System.Text.StringBuilder();
            string root = string.IsNullOrWhiteSpace(o.OutputDir) ? "(未选择输出目录)" : o.OutputDir;
            sb.AppendLine(root + Path.DirectorySeparatorChar);
            if (o.WithPackIcon) sb.AppendLine("  pack.png");
            sb.AppendLine("  pack.mcmeta");

            string ns = o.Namespace;
            sb.AppendLine("  assets" + Path.DirectorySeparatorChar + ns + Path.DirectorySeparatorChar);

            if (o.WithTextures)
            {
                sb.AppendLine("    textures" + Path.DirectorySeparatorChar);
                if (o.WithBlockTextures) sb.AppendLine("      " + blockDir + Path.DirectorySeparatorChar);
                if (o.WithItemTextures) sb.AppendLine("      " + itemDir + Path.DirectorySeparatorChar);
                sb.AppendLine("      gui" + Path.DirectorySeparatorChar);
                sb.AppendLine("      entity" + Path.DirectorySeparatorChar);
            }
            if (o.WithModels)
            {
                sb.AppendLine("    models" + Path.DirectorySeparatorChar);
                sb.AppendLine("      block" + Path.DirectorySeparatorChar);
                sb.AppendLine("      item" + Path.DirectorySeparatorChar);
            }
            if (itemsDir)
                sb.AppendLine("    items" + Path.DirectorySeparatorChar);
            if (o.WithLang) sb.AppendLine("    lang" + Path.DirectorySeparatorChar);
            if (o.WithSounds) sb.AppendLine("    sounds" + Path.DirectorySeparatorChar);

            sb.AppendLine();
            sb.AppendLine("pack.mcmeta 内容：");
            var rootJ = JsonValue.Obj();
            var pack = JsonValue.Obj();
            pack.Set("pack_format", JsonValue.Num(o.PackFormat));
            pack.Set("description", JsonValue.Str(o.Description));
            rootJ.Set("pack", pack);
            sb.AppendLine(Json.Write(rootJ, true));
            sb.AppendLine();
            var ver = Settings.FindByFormat(o.PackFormat);
            sb.AppendLine("对应版本：" + (ver != null ? Settings.Describe(ver) : "未知"));

            _preview.Text = sb.ToString();
        }

        private void DoCreate()
        {
            var o = CurrentOptions();

            if (string.IsNullOrWhiteSpace(o.OutputDir))
            {
                PageKit.Error(_view, "先选择输出目录。");
                return;
            }
            if (string.IsNullOrWhiteSpace(o.Namespace) ||
                o.Namespace.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                PageKit.Error(_view, "命名空间不合法，只能包含字母、数字、下划线和短横线。");
                return;
            }

            if (Directory.Exists(o.OutputDir) &&
                Directory.GetFileSystemEntries(o.OutputDir).Length > 0)
            {
                if (!PageKit.Confirm(_view,
                    "目录已存在且不为空：\n\n" + o.OutputDir +
                    "\n\n其中的内容会先被备份、被新骨架替换，成功后删除备份。继续吗？")) return;
            }

            var notes = new List<string>();
            _createBtn.Enabled = false;
            if (_view != null) _view.UseWaitCursor = true;

            try
            {
                var report = PackScaffold.Create(o, t => notes.Add(t));

                _preview.Text = string.Join(Environment.NewLine, notes.ToArray())
                                + Environment.NewLine + Environment.NewLine
                                + (report.Ok ? "创建完成：" + report.Describe() : "失败：" + report.Error);

                if (report.Ok)
                {
                    Status("资源包已创建：" + report.FileCount + " 个文件");
                    MessageBox.Show(_view,
                        "创建完成。\n\n" + report.FileCount + " 个文件，" + Ui.FormatBytes(report.TotalBytes)
                        + "\n\n" + report.OutputPath,
                        "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Status("创建失败：" + report.Error);
                    PageKit.Error(_view, "创建失败：\n\n" + report.Error);
                }
            }
            catch (Exception ex)
            {
                PageKit.Error(_view, ex.Message);
                Status("创建失败：" + ex.Message);
            }
            finally
            {
                _createBtn.Enabled = true;
                if (_view != null) _view.UseWaitCursor = false;
            }
        }
    }
}
