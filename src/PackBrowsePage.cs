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
    /* 资源包 · 内容浏览                                                    */
    /* ================================================================== */

    internal sealed class PackBrowsePage : IModulePage
    {
        public string Title { get { return "内容浏览"; } }
        public string Subtitle { get { return "文件树与按类型浏览，图片、JSON、音频信息都能直接看"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;
        private PackAnalysis _analysis;

        private TextBox _pathBox, _filterBox, _textPreview;
        private TreeView _tree;
        private ListView _typeList;
        private PictureBox _imagePreview;
        private Label _info, _previewTitle, _audioInfo;
        private ComboBox _typeFilter;
        private SplitContainer _split;

        /// <summary>文件节点 → 相对路径</summary>
        private readonly Dictionary<TreeNode, string> _nodePath = new Dictionary<TreeNode, string>();
        /// <summary>目录节点是否已展开加载过</summary>
        private readonly HashSet<TreeNode> _loaded = new HashSet<TreeNode>();

        private List<string> _allFiles = new List<string>();

        public void OnEnter() { }
        public void OnLeave() { if (_analysis != null) { _analysis.Cleanup(); _analysis = null; } }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildSourceCard());
            stack.Controls.Add(BuildBrowseCard());

            _view = host;
            return host;
        }

        /* ------------------------------------------------------------ */

        private Card BuildSourceCard()
        {
            var card = new Card { Title = "① 打开资源包", Hint = "文件夹或 ZIP", Height = 168, Margin = new Padding(0, 0, 0, 12) };

            _pathBox = Ui.ReadOnlyBox("");
            _pathBox.Font = Theme.M(8.5f);
            FlatBtn browse, zipBtn;
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
            bar.Controls.Add(zipBtn);

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

        private Card BuildBrowseCard()
        {
            var card = new Card { Title = "② 浏览与预览", Hint = "左侧是文件树，右上按类型筛选，右下是预览", Height = 470, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _filterBox = new TextBox
            {
                Width = 220,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
                Margin = new Padding(0, 0, 8, 0),
            };
            _filterBox.TextChanged += (s, e) => RebuildTree();

            _typeFilter = new ComboBox { Width = 240, Margin = new Padding(0, 2, 8, 0) };
            Ui.StyleCombo(_typeFilter);
            _typeFilter.Items.Add("全部类型");
            _typeFilter.Items.Add("纹理 (textures)");
            _typeFilter.Items.Add("模型 (models)");
            _typeFilter.Items.Add("方块状态 (blockstates)");
            _typeFilter.Items.Add("物品定义 (items)");
            _typeFilter.Items.Add("语言 (lang)");
            _typeFilter.Items.Add("音效 (sounds)");
            _typeFilter.Items.Add("JSON / mcmeta");
            _typeFilter.Items.Add("其它");
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += (s, e) => FillTypeList();

            var expand = new FlatBtn("全部展开", BtnKind.Ghost, 86) { Margin = new Padding(0, 0, 6, 0) };
            var collapse = new FlatBtn("全部折叠", BtnKind.Ghost, 86);
            expand.Click += (s, e) => { _tree.ExpandAll(); Status("已全部展开"); };
            collapse.Click += (s, e) => { _tree.CollapseAll(); if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand(); Status("已全部折叠"); };

            bar.Controls.Add(_filterBox);
            bar.Controls.Add(_typeFilter);
            bar.Controls.Add(expand);
            bar.Controls.Add(collapse);

            /* 文件树 */
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.F(8.8f),
                HideSelection = false,
                ShowLines = false,
                ItemHeight = 20,
            };
            _tree.AfterSelect += (s, e) => OnTreeSelect();
            _tree.BeforeExpand += (s, e) => LazyLoad(e.Node);

            /* 类型列表 */
            _typeList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.F(8.5f),
                HideSelection = false,
            };
            _typeList.Columns.Add("文件", 300);
            _typeList.Columns.Add("大小", 90);
            _typeList.SelectedIndexChanged += (s, e) => OnTypeListSelect();

            /* 预览区 */
            _previewTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Theme.Fg,
                Font = Theme.F(9f, FontStyle.Bold),
                BackColor = Color.Transparent,
                Padding = new Padding(2, 3, 0, 0),
            };

            _imagePreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgInput,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };

            _textPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.M(8.5f),
            };

            _audioInfo = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(9f),
                BackColor = Theme.BgInput,
                Padding = new Padding(14, 14, 0, 0),
                Text = "（选中左侧文件以预览）",
            };

            var previewHolder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgInput };
            previewHolder.Controls.Add(_audioInfo);
            previewHolder.Controls.Add(_textPreview);
            previewHolder.Controls.Add(_imagePreview);
            previewHolder.Controls.Add(_previewTitle);

            /* 布局：左树 | 右（上列表 / 下预览） */
            var right = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 150,
                BackColor = Theme.Line,
                Panel1MinSize = 80,
                Panel2MinSize = 120,
            };
            right.Panel1.Controls.Add(_typeList);
            right.Panel2.Controls.Add(previewHolder);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 360,
                BackColor = Theme.Line,
                Panel1MinSize = 200,
                Panel2MinSize = 240,
            };
            _split.Panel1.Controls.Add(_tree);
            _split.Panel2.Controls.Add(right);

            card.Body.Controls.Add(_split);
            card.Body.Controls.Add(PageKit.Spacer(8));
            card.Body.Controls.Add(bar);
            return card;
        }

        /* ------------------------------------------------------------ */

        private void DoLoad()
        {
            string path = _pathBox.Text;
            if (string.IsNullOrWhiteSpace(path)) { PageKit.Error(_view, "先选择资源包。"); return; }

            if (_analysis != null) { _analysis.Cleanup(); _analysis = null; }

            var a = PackAnalyzer.Analyze(path);
            _analysis = a;

            if (!a.Ok)
            {
                _info.Text = "读取失败：" + a.Error;
                _tree.Nodes.Clear();
                _allFiles.Clear();
                _typeList.Items.Clear();
                PageKit.Error(_view, a.Error);
                Status("读取失败：" + a.Error);
                return;
            }

            _allFiles = Directory.GetFiles(a.RootDir, "*", SearchOption.AllDirectories)
                                 .Select(f => ZipIo.RelPath(a.RootDir, f))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            var c = a.Counts;
            _info.Text = a.DisplayName + "　·　" + (a.Format != null ? a.Format.DescribeCurrent() : "版本未知")
                         + "　·　共 " + c.Total + " 个文件 / " + Ui.FormatBytes(c.TotalBytes)
                         + "\n纹理 " + c.Textures + "　模型 " + c.Models + "　方块状态 " + c.BlockStates
                         + "　物品 " + c.ItemModels + "　语言 " + c.Lang + "　音效 " + c.Sounds
                         + "　JSON " + c.Json + "　其它 " + c.Other
                         + (a.Namespaces.Count > 0 ? "　·　命名空间 " + string.Join(", ", a.Namespaces.ToArray()) : "");

            RebuildTree();
            FillTypeList();
            Status("已打开：" + a.DisplayName + "（" + _allFiles.Count + " 个文件）");
        }

        private void RebuildTree()
        {
            if (_analysis == null) return;

            string filter = (_filterBox.Text ?? "").Trim();
            var matches = string.IsNullOrEmpty(filter)
                ? _allFiles
                : _allFiles.Where(f => f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            _nodePath.Clear();
            _loaded.Clear();

            var root = new TreeNode(_analysis.DisplayName + "   (" + matches.Count + " 个文件)");
            root.Tag = "/";
            _tree.Nodes.Add(root);
            BuildChildren(root, "", matches);

            root.Expand();
            _tree.EndUpdate();

            if (string.IsNullOrEmpty(filter) && _allFiles.Count > 400)
                Status("已建立文件树（" + matches.Count + " 个匹配），目录按需加载");
        }

        /// <summary>把一个目录下的直接子项挂到节点上（一级目录先建空节点用于懒加载）</summary>
        private void BuildChildren(TreeNode parent, string prefix, List<string> files)
        {
            string p = prefix.Length == 0 ? "" : prefix + "/";

            var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var direct = new List<string>();

            foreach (string f in files)
            {
                if (p.Length > 0 && !f.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;

                string rest = p.Length > 0 ? f.Substring(p.Length) : f;
                if (rest.Length == 0) continue;

                int slash = rest.IndexOf('/');
                if (slash < 0) direct.Add(f);
                else dirs.Add(rest.Substring(0, slash));
            }

            foreach (string d in dirs)
            {
                string dirFull = p + d;
                var node = new TreeNode("📁 " + d);
                node.Tag = dirFull + "/";
                node.Nodes.Add(new TreeNode("…"));   // 占位，展开时替换
                parent.Nodes.Add(node);
                _nodePath[node] = dirFull;
            }

            foreach (string f in direct)
            {
                string leaf = f.Substring(f.LastIndexOf('/') + 1);
                var node = new TreeNode(leaf);
                node.Tag = f;
                node.ForeColor = ColorForFile(f);
                parent.Nodes.Add(node);
                _nodePath[node] = f;
            }
        }

        /// <summary>展开时才真正加载该目录的子项</summary>
        private void LazyLoad(TreeNode node)
        {
            if (node == null || _loaded.Contains(node)) return;
            string dir = node.Tag as string;
            if (dir == null || !dir.EndsWith("/")) return;

            _loaded.Add(node);

            string filter = (_filterBox.Text ?? "").Trim();
            var matches = string.IsNullOrEmpty(filter)
                ? _allFiles
                : _allFiles.Where(f => f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _tree.BeginUpdate();
            node.Nodes.Clear();
            BuildChildren(node, dir.TrimEnd('/'), matches);
            _tree.EndUpdate();
        }

        private static Color ColorForFile(string rel)
        {
            string lower = rel.ToLowerInvariant();
            if (lower.EndsWith(".png")) return Color.FromArgb(140, 220, 255);
            if (lower.EndsWith(".json") || lower.EndsWith(".mcmeta")) return Color.FromArgb(255, 215, 140);
            if (lower.EndsWith(".ogg")) return Color.FromArgb(190, 255, 180);
            if (lower.EndsWith(".lang")) return Color.FromArgb(255, 180, 220);
            return Color.FromArgb(200, 205, 215);
        }

        private static string TypeOf(string rel)
        {
            string lower = rel.ToLowerInvariant();
            string ext = Path.GetExtension(lower);

            if (lower.Contains("/textures/") || lower.StartsWith("textures/")) return "纹理 (textures)";
            if (lower.Contains("/models/") || lower.StartsWith("models/")) return "模型 (models)";
            if (lower.Contains("/blockstates/") || lower.StartsWith("blockstates/")) return "方块状态 (blockstates)";
            if (lower.Contains("/items/") || lower.StartsWith("items/")) return "物品定义 (items)";
            if (lower.Contains("/lang/") || lower.StartsWith("lang/")) return "语言 (lang)";
            if (lower.Contains("/sounds/") || lower.StartsWith("sounds/") || ext == ".ogg") return "音效 (sounds)";
            if (ext == ".json" || ext == ".mcmeta") return "JSON / mcmeta";
            return "其它";
        }

        private void FillTypeList()
        {
            if (_analysis == null) return;

            string want = _typeFilter.SelectedItem as string;
            _typeList.BeginUpdate();
            _typeList.Items.Clear();

            int n = 0;
            foreach (string f in _allFiles)
            {
                string t = TypeOf(f);
                if (want != null && want != "全部类型" && t != want) continue;

                long size = 0;
                try
                {
                    string full = Path.Combine(_analysis.RootDir, f.Replace('/', Path.DirectorySeparatorChar));
                    size = new FileInfo(full).Length;
                }
                catch { }

                var item = new ListViewItem(f);
                item.SubItems.Add(Ui.FormatBytes(size));
                item.Tag = f;
                _typeList.Items.Add(item);
                n++;
            }
            _typeList.EndUpdate();
            Status("按类型筛选：" + n + " 个文件");
        }

        /* ------------------------------------------------------------ */

        private void OnTreeSelect()
        {
            if (_tree.SelectedNode == null) return;
            string tag = _tree.SelectedNode.Tag as string;
            if (tag == null || tag.EndsWith("/")) return;
            Preview(tag);
        }

        private void OnTypeListSelect()
        {
            if (_typeList.SelectedItems.Count == 0) return;
            string rel = _typeList.SelectedItems[0].Tag as string;
            if (rel != null) Preview(rel);
        }

        private void Preview(string rel)
        {
            if (_analysis == null) return;

            string full = Path.Combine(_analysis.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                _previewTitle.Text = rel + "　（文件不存在）";
                ShowOnly(_audioInfo);
                return;
            }

            long size = new FileInfo(full).Length;
            _previewTitle.Text = rel + "　·　" + Ui.FormatBytes(size);

            string lower = rel.ToLowerInvariant();
            string ext = Path.GetExtension(lower);

            try
            {
                if (ext == ".png")
                {
                    using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var img = Image.FromStream(fs))
                    {
                        var old = _imagePreview.Image;
                        _imagePreview.Image = new Bitmap(img);
                        if (old != null) old.Dispose();

                        _previewTitle.Text = rel + "　·　" + img.Width + "×" + img.Height
                                             + "　·　" + Ui.FormatBytes(size);
                    }
                    ShowOnly(_imagePreview);
                    Status("预览图片：" + rel);
                    return;
                }

                if (ext == ".ogg" || ext == ".wav" || ext == ".mp3")
                {
                    _audioInfo.Text = "音频文件（本工具不播放）\n\n"
                                      + "文件：" + rel + "\n"
                                      + "大小：" + Ui.FormatBytes(size) + "\n\n"
                                      + OggInfo(full, ext);
                    ShowOnly(_audioInfo);
                    Status("查看音频信息：" + rel);
                    return;
                }

                if (ext == ".json" || ext == ".mcmeta" || ext == ".lang" || ext == ".txt" || ext == ".properties")
                {
                    string text = File.ReadAllText(full, Encoding.UTF8);

                    string note = "";
                    if (ext == ".json" || ext == ".mcmeta")
                    {
                        JsonValue v; string err;
                        if (Json.TryParse(text, out v, out err))
                        {
                            text = Json.Write(v, true);
                            note = "　·　JSON 合法";
                        }
                        else
                        {
                            note = "　·　JSON 有问题：" + err;
                        }
                    }
                    _textPreview.Text = text;
                    ShowOnly(_textPreview);
                    _previewTitle.Text = rel + "　·　" + Ui.FormatBytes(size) + note;
                    Status("预览文本：" + rel + note);
                    return;
                }

                /* 其它二进制：给个十六进制头 */
                byte[] head = new byte[Math.Min(256, (int)size)];
                using (var fs = File.OpenRead(full)) fs.Read(head, 0, head.Length);

                var sb = new StringBuilder();
                sb.AppendLine("二进制文件：" + rel);
                sb.AppendLine("大小：" + Ui.FormatBytes(size));
                sb.AppendLine();
                sb.AppendLine("前 " + head.Length + " 字节：");
                for (int i = 0; i < head.Length; i += 16)
                {
                    sb.Append(i.ToString("X4")).Append("  ");
                    for (int j = 0; j < 16 && i + j < head.Length; j++)
                        sb.Append(head[i + j].ToString("X2")).Append(' ');
                    sb.AppendLine();
                }
                _textPreview.Text = sb.ToString();
                ShowOnly(_textPreview);
                Status("查看二进制头：" + rel);
            }
            catch (Exception e)
            {
                _audioInfo.Text = "无法预览：" + e.Message;
                ShowOnly(_audioInfo);
            }
        }

        /// <summary>读 Ogg 的采样率 / 声道（Vorbis 标识头），读不到就说明是别的容器</summary>
        private static string OggInfo(string path, string ext)
        {
            try
            {
                if (ext != ".ogg") return "（非 Ogg 容器，未解析）";

                byte[] b = new byte[256];
                int n;
                using (var fs = File.OpenRead(path)) n = fs.Read(b, 0, b.Length);

                /* 找 \x01vorbis 标识 */
                for (int i = 0; i < n - 16; i++)
                {
                    if (b[i] == 0x01 && b[i + 1] == 'v' && b[i + 2] == 'o' && b[i + 3] == 'r' &&
                        b[i + 4] == 'b' && b[i + 5] == 'i' && b[i + 6] == 's')
                    {
                        int channels = b[i + 11];
                        int rate = b[i + 12] | (b[i + 13] << 8) | (b[i + 14] << 16) | (b[i + 15] << 24);
                        return "Ogg Vorbis\n声道数：" + channels + "\n采样率：" + rate + " Hz";
                    }
                }
                return "Ogg 容器（没找到 Vorbis 标识头，可能是 Opus）";
            }
            catch (Exception e)
            {
                return "解析失败：" + e.Message;
            }
        }

        private void ShowOnly(Control c)
        {
            _imagePreview.Visible = c == _imagePreview;
            _textPreview.Visible = c == _textPreview;
            _audioInfo.Visible = c == _audioInfo;
        }
    }
}
