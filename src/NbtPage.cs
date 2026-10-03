using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* NBT · 编辑器                                                        */
    /* ================================================================== */

    /// <summary>
    /// NBT 数据编辑器。功能对照 NBTExplorer（MIT，Justin Aquadro）的 Windows 版：
    /// 打开/刷新/另存、树形浏览、增删改标签、剪切复制粘贴、上下移动、查找替换、
    /// SNBT 与 JSON 导入导出、撤销重做。
    /// </summary>
    internal sealed class NbtPage : IModulePage
    {
        public string Title { get { return "NBT 编辑器"; } }
        public string Subtitle { get { return "打开 .dat / .nbt，树形浏览、增删改标签、查找替换与另存（能力移植自 NBTExplorer）"; } }

        public event EventHandler<string> StatusChanged;

        private Control _view;
        private NbtDocument _doc;
        private bool _dirty;
        private int _nodeCount;
        private readonly NbtHistory _history = new NbtHistory();

        /// <summary>复制粘贴用的剪贴板。做成静态的：切到别的页面再回来，复制的东西还在</summary>
        private static NbtTag _clipboard;
        private static string _clipboardPath = "";

        /* ---------------------------- 控件 ---------------------------- */

        private TextBox _pathBox, _nameBox, _valueBox;
        private Label _info, _detailTitle, _detailHint, _pathLabel;
        private TreeView _tree;
        private FlatBtn _saveBtn, _undoBtn, _redoBtn;
        private FlatBtn _addBtn, _renameBtn, _valueBtn, _deleteBtn;
        private FlatBtn _cutBtn, _copyBtn, _pasteBtn, _upBtn, _downBtn;

        /* 展开状态用路径记住，重建树时不至于全部塌回去 */
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly HashSet<TreeNode> _filled = new HashSet<TreeNode>();

        public void OnEnter()
        {
            if (_doc == null) Status("NBT 编辑器 —— 先打开一个 .dat / .nbt 文件");
            else Status("NBT 编辑器 —— " + DescribeDoc());
        }

        public void OnLeave() { }

        /* ============================================================ */
        /* 界面                                                          */
        /* ============================================================ */

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);
            stack.Controls.Add(BuildOpenCard());
            stack.Controls.Add(BuildEditCard());

            _view = host;
            RefreshButtons();
            return host;
        }

        private Card BuildOpenCard()
        {
            /* 高度要按「标题 40 + 标签 20 + 路径行 30 + 按钮条 34 + 两行信息 36 + 内边距」算，
               写小了信息行会被挤到卡片外面去 */
            var card = new Card { Title = "① 打开 NBT 文件", Hint = "存档 .dat、结构 .nbt、level.dat 都能直接读", Height = 200, Margin = new Padding(0, 0, 0, 12) };

            _pathBox = Ui.ReadOnlyBox("");
            _pathBox.Font = Theme.M(8.5f);

            FlatBtn browse;
            var row = PageKit.PathRow(_pathBox, "选文件…", () => OpenDialog(), out browse);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };

            var folderBtn = new FlatBtn("打开文件夹…", BtnKind.Outline, 110) { Margin = new Padding(0, 0, 8, 0) };
            folderBtn.Click += (s, e) => OpenFolderDialog();

            var newBtn = new FlatBtn("新建空文件", BtnKind.Outline, 100) { Margin = new Padding(0, 0, 8, 0) };
            newBtn.Click += (s, e) => NewDocument();

            var reloadBtn = new FlatBtn("重新载入", BtnKind.Ghost, 96) { Margin = new Padding(0, 0, 8, 0) };
            reloadBtn.Click += (s, e) => Reload();

            bar.Controls.Add(folderBtn);
            bar.Controls.Add(newBtn);
            bar.Controls.Add(reloadBtn);

            _info = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(8.8f),
                BackColor = Color.Transparent,
                Text = "还没有打开文件。gzip / zlib / 不压缩会自动识别；基岩版 level.dat 的「8 字节头 + 小端」也认得。",
            };

            card.Body.Controls.Add(_info);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(row);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("文件位置"));
            return card;
        }

        private Card BuildEditCard()
        {
            var card = new Card { Title = "② 浏览与编辑", Hint = "双击改名，回车改值；Ctrl+C/V/X 复制粘贴，Ctrl+Z 撤销", Height = 560, Margin = new Padding(0, 0, 0, 12) };

            /* 按钮多，窄窗口下会折成两行：高度留够两行，WrapContents 才不会把第二行裁掉 */
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 72,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent,
            };

            _saveBtn = Tool(bar, "保存", BtnKind.Primary, 78, () => Save(false));
            var saveAs = Tool(bar, "另存为…", BtnKind.Outline, 92, () => Save(true));
            _undoBtn = Tool(bar, "撤销", BtnKind.Ghost, 70, () => Undo());
            _redoBtn = Tool(bar, "重做", BtnKind.Ghost, 70, () => Redo());
            _addBtn = Tool(bar, "添加子标签…", BtnKind.Outline, 116, () => AddChild());
            _renameBtn = Tool(bar, "改名…", BtnKind.Ghost, 78, () => Rename());
            _valueBtn = Tool(bar, "改值…", BtnKind.Ghost, 78, () => EditValue());
            _deleteBtn = Tool(bar, "删除", BtnKind.Danger, 70, () => Delete());
            _cutBtn = Tool(bar, "剪切", BtnKind.Ghost, 70, () => Cut());
            _copyBtn = Tool(bar, "复制", BtnKind.Ghost, 70, () => Copy());
            _pasteBtn = Tool(bar, "粘贴", BtnKind.Ghost, 70, () => Paste());
            _upBtn = Tool(bar, "上移", BtnKind.Ghost, 70, () => Move(-1));
            _downBtn = Tool(bar, "下移", BtnKind.Ghost, 70, () => Move(1));
            Tool(bar, "查找替换…", BtnKind.Outline, 106, () => Find());
            Tool(bar, "导出 SNBT…", BtnKind.Ghost, 106, () => ExportSnbt());
            Tool(bar, "导入 SNBT…", BtnKind.Ghost, 106, () => ImportSnbt());
            Tool(bar, "导出 JSON…", BtnKind.Ghost, 106, () => ExportJson());
            Tool(bar, "展开全部", BtnKind.Ghost, 90, () => ExpandAll(true));
            Tool(bar, "全部折叠", BtnKind.Ghost, 90, () => ExpandAll(false));

            /* 左侧树 */
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.F(8.8f),
                HideSelection = false,
                ShowLines = false,
                ShowPlusMinus = true,
                ItemHeight = 21,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                FullRowSelect = false,
            };
            _tree.AfterSelect += (s, e) => OnSelect();
            _tree.BeforeExpand += (s, e) => FillChildren(e.Node);
            _tree.BeforeCollapse += (s, e) => RememberExpanded(e.Node, false);
            _tree.AfterExpand += (s, e) => RememberExpanded(e.Node, true);
            _tree.DrawNode += (s, e) => DrawNode(e);
            _tree.KeyDown += (s, e) => OnTreeKey(e);
            _tree.DoubleClick += (s, e) => EditValue();
            /* 树的滚动条是延迟创建的系统子窗口，出现时要再套一次深色主题，
               否则节点一多右侧就冒出一条浅灰滚动条 */
            _tree.HandleCreated += (s, e) => { DarkMode.ApplyTo(_tree.Handle); DarkMode.ApplyDeferred(_tree, 4); };

            /* 右侧详情 */
            var detail = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgPanel, Padding = new Padding(14, 10, 14, 10) };

            _detailTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                ForeColor = Theme.Fg,
                Font = Theme.F(10f, FontStyle.Bold),
                BackColor = Color.Transparent,
                Text = "未选中任何标签",
            };

            _pathLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = Theme.FgFaint,
                Font = Theme.M(8.2f),
                BackColor = Color.Transparent,
                Text = "",
            };

            _nameBox = NbtUi.Box("", 0, 0, 200, false, false);
            _nameBox.Dock = DockStyle.Top;

            _valueBox = NbtUi.Box("", 0, 0, 200, false, true);
            _valueBox.Dock = DockStyle.Top;
            _valueBox.Height = 22;

            var applyRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            var applyBtn = new FlatBtn("应用修改", BtnKind.Primary, 96) { Margin = new Padding(0, 2, 8, 0) };
            applyBtn.Click += (s, e) => ApplyDetail();
            var revertBtn = new FlatBtn("还原", BtnKind.Ghost, 70) { Margin = new Padding(0, 2, 0, 0) };
            revertBtn.Click += (s, e) => OnSelect();
            applyRow.Controls.Add(applyBtn);
            applyRow.Controls.Add(revertBtn);

            _detailHint = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
                Text = "选中左边任意标签后，这里可以改名和改值。\n\n复合标签与列表请用「添加子标签」。\n数组可以直接填一串数字，或用「改值…」里的十六进制编辑。",
            };

            detail.Controls.Add(_detailHint);
            detail.Controls.Add(applyRow);
            detail.Controls.Add(PageKit.Spacer(4));
            detail.Controls.Add(_valueBox);
            detail.Controls.Add(PageKit.Spacer(4));
            detail.Controls.Add(_nameBox);
            detail.Controls.Add(PageKit.Spacer(4));
            detail.Controls.Add(_pathLabel);
            detail.Controls.Add(_detailTitle);

            var split = PageKit.MakeSplit(Orientation.Vertical, 0.62, 320, 260);

            /* SplitContainer 的真实尺寸要等布局完，这里延迟挂内容 */
            split.HandleCreated += (s, e) =>
            {
                if (split.Panel1.Controls.Count == 0)
                {
                    split.Panel1.Controls.Add(_tree);
                    split.Panel2.Controls.Add(detail);
                }
            };
            split.SizeChanged += (s, e) =>
            {
                if (split.Panel1.Controls.Count == 0 && split.Width > 0)
                {
                    split.Panel1.Controls.Add(_tree);
                    split.Panel2.Controls.Add(detail);
                }
            };

            card.Body.Controls.Add(split);
            card.Body.Controls.Add(bar);
            return card;
        }

        private static FlatBtn Tool(FlowLayoutPanel bar, string text, BtnKind kind, int width, Action onClick)
        {
            var btn = new FlatBtn(text, kind, width) { Margin = new Padding(0, 0, 6, 0) };
            btn.Click += (s, e) => onClick();
            bar.Controls.Add(btn);
            return btn;
        }

        /* ============================================================ */
        /* 打开 / 保存                                                   */
        /* ============================================================ */

        private void OpenDialog()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "打开 NBT 文件";
                dlg.Filter = "NBT 数据 (*.dat;*.nbt;*.mcstructure)|*.dat;*.nbt;*.mcstructure|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                OpenFile(dlg.FileName);
            }
        }

        /// <summary>打开文件夹：像 NBTExplorer 的 Open Folder 那样，把里面的 NBT 文件列出来让你挑</summary>
        private void OpenFolderDialog()
        {
            string dir = PageKit.PickFolder(_view, "选择一个文件夹，会列出里面的 NBT 文件");
            if (dir == null) return;

            var files = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".dat" || ext == ".nbt" || ext == ".mcstructure" || ext == ".schematic" || ext == ".snbt")
                        files.Add(f);
                }
                if (files.Count == 0)
                {
                    /* 没有常见后缀就再找一层，玩家目录通常是 saves\<存档>\level.dat */
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        foreach (string f in Directory.GetFiles(sub))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".dat" || ext == ".nbt") files.Add(f);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "读文件夹失败：" + e.Message);
                return;
            }

            if (files.Count == 0)
            {
                PageKit.Error(_view, "这个文件夹里没有找到 .dat / .nbt 文件。");
                return;
            }

            using (var pick = new NbtPickForm("这个文件夹里的 NBT 文件（共 " + files.Count + " 个）", files))
            {
                if (pick.ShowDialog(_view) != DialogResult.OK) return;
                OpenFile(pick.Picked);
            }
        }

        private void NewDocument()
        {
            _doc = new NbtDocument
            {
                Path = null,
                Root = new NbtCompound(""),
                RootName = "",
                Compression = NbtCompression.GZip,
                LittleEndian = false,
                HasBedrockHeader = false,
                BedrockVersion = 9,
            };
            _history.Clear();
            _history.Disabled = false;
            _dirty = true;
            _pathBox.Text = "（尚未保存，点「另存为…」写到磁盘）";
            RebuildTree(null);
            Status("新建了一个空的 NBT 文件");
        }

        /// <summary>
        /// 打开一个 NBT 文件。界面上点「选文件…」走这里，
        /// 命令行的 --nbt &lt;文件&gt; 也走这里，省得两条路各写一遍。
        /// </summary>
        public void OpenFile(string path)
        {
            if (_view == null) CreateView();

            try
            {
                _doc = NbtIo.Load(path);
            }
            catch (Exception e)
            {
                PageKit.Error(_view == null ? null : _view, "打开失败：\n\n" + e.Message);
                return;
            }

            _history.Clear();
            _nodeCount = NbtEditOps.CountNodes(_doc.Root);
            _history.Disabled = _nodeCount > NbtHistory.MaxNodes;

            _dirty = false;
            _pathBox.Text = path;
            _expanded.Clear();
            RebuildTree(null);
            RefreshInfo();
            RefreshButtons();

            Status("已打开 " + Path.GetFileName(path) + " —— " + DescribeDoc());
            if (_history.Disabled)
                Status("这个文件有 " + _nodeCount + " 个标签，太大，已关闭撤销以省内存");
        }

        private void Reload()
        {
            if (_doc == null || string.IsNullOrEmpty(_doc.Path)) return;
            if (_dirty && !PageKit.Confirm(_view, "当前修改还没保存，重新载入会丢掉这些改动。继续吗？")) return;
            OpenFile(_doc.Path);
        }

        private void Save(bool saveAs)
        {
            if (_doc == null)
            {
                PageKit.Error(_view, "还没有打开任何文件。");
                return;
            }

            string path = _doc.Path;
            bool little = _doc.LittleEndian;
            bool header = _doc.HasBedrockHeader;
            NbtCompression compression = _doc.Compression;

            if (saveAs || string.IsNullOrEmpty(path))
            {
                string suggest = string.IsNullOrEmpty(path)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "untitled.dat")
                    : path;
                using (var dlg = new NbtSaveAsForm(_doc, suggest))
                {
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    path = dlg.PickedPath;
                    compression = dlg.PickedCompression;
                    little = dlg.PickedLittleEndian;
                    header = dlg.PickedBedrockHeader;
                }
            }

            try
            {
                NbtIo.Save(_doc, path, compression, little, header);
            }
            catch (Exception e)
            {
                PageKit.Error(_view, "保存失败：\n\n" + e.Message);
                return;
            }

            _doc.Path = path;
            _doc.Compression = compression;
            _doc.LittleEndian = little;
            _doc.HasBedrockHeader = header;
            _pathBox.Text = path;
            _dirty = false;
            RefreshButtons();
            Status("已保存到 " + path + "（" + NbtIo.DescribeCompression(compression) + (little ? " · 小端" : " · 大端") + "）");
        }

        /* ============================================================ */
        /* 树                                                            */
        /* ============================================================ */

        private void RebuildTree(NbtTag select)
        {
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                _filled.Clear();
                if (_doc == null) return;

                var root = MakeNode(_doc.Root, -1);
                root.Text = RootText();
                _tree.Nodes.Add(root);
                FillChildren(root);

                /* 默认展开两层：第一层看清根，第二层就能看到主要分组。
                   之后再按 _expanded 里记下的路径恢复用户自己的展开状态 */
                root.Expand();
                foreach (TreeNode child in root.Nodes)
                {
                    if (child.Nodes.Count == 0) continue;
                    if (_expanded.Count == 0 || _expanded.Contains(PathOfNode(child)))
                    {
                        child.Expand();
                        FillChildren(child);
                    }
                }

                /* 第二层里只有一两个子节点时再往里展开一层，省一次点击 */
                if (root.Nodes.Count == 1 && root.Nodes[0].Nodes.Count > 0 && root.Nodes[0].Nodes.Count <= 4)
                {
                    foreach (TreeNode grand in root.Nodes[0].Nodes)
                    {
                        if (grand.Nodes.Count > 0) { grand.Expand(); FillChildren(grand); }
                    }
                }

                if (select != null)
                {
                    var node = FindNode(_tree.Nodes, select);
                    if (node != null)
                    {
                        _tree.SelectedNode = node;
                        node.EnsureVisible();
                    }
                }
                else
                {
                    _tree.SelectedNode = root;
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
        }

        private string RootText()
        {
            string name = string.IsNullOrEmpty(_doc.RootName) ? "(无名)" : _doc.RootName;
            return name + "  ·  TAG_Compound  ·  " + _doc.Root.Children.Count + " 项";
        }

        private TreeNode MakeNode(NbtTag tag, int listIndex)
        {
            var node = new TreeNode(DisplayText(tag, listIndex));
            node.Tag = tag;
            if (tag.HasChildren) node.Nodes.Add(new TreeNode("读取中…"));
            return node;
        }

        private static string DisplayText(NbtTag tag, int listIndex)
        {
            string name = listIndex >= 0 ? "[" + listIndex + "] " + NbtTypeInfo.ShortName(tag.Type) : tag.Name;
            if (name.Length == 0) name = "(无名)";
            return name + "  =  " + tag.ValueText();
        }

        private void FillChildren(TreeNode node)
        {
            if (node == null || _filled.Contains(node)) return;
            _filled.Add(node);

            var tag = node.Tag as NbtTag;
            if (tag == null) return;

            node.Nodes.Clear();

            var compound = tag as NbtCompound;
            if (compound != null)
            {
                foreach (var child in compound.Children) node.Nodes.Add(MakeNode(child, -1));
                return;
            }

            var list = tag as NbtList;
            if (list != null)
            {
                for (int i = 0; i < list.Items.Count; i++) node.Nodes.Add(MakeNode(list.Items[i], i));
            }
        }

        private void RememberExpanded(TreeNode node, bool open)
        {
            string path = PathOfNode(node);
            if (open) _expanded.Add(path);
            else _expanded.Remove(path);
        }

        private string PathOfNode(TreeNode node)
        {
            if (_doc == null || node == null) return "";
            var tag = node.Tag as NbtTag;
            if (tag == null) return "";
            return NbtWalk.PathOf(_doc.Root, tag);
        }

        private static TreeNode FindNode(TreeNodeCollection nodes, NbtTag tag)
        {
            foreach (TreeNode n in nodes)
            {
                if (ReferenceEquals(n.Tag, tag)) return n;
                var found = FindNode(n.Nodes, tag);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>沿父链找到一个标签所在的位置（编辑操作全靠它定位）</summary>
        private NbtSpot SpotOf(TreeNode node)
        {
            if (node == null || _doc == null) return null;
            var tag = node.Tag as NbtTag;
            if (tag == null) return null;

            var spot = new NbtSpot { Tag = tag, Path = NbtWalk.PathOf(_doc.Root, tag), Index = -1 };
            if (node.Parent == null) return spot;

            var parentTag = node.Parent.Tag as NbtTag;
            var compound = parentTag as NbtCompound;
            if (compound != null)
            {
                spot.ParentCompound = compound;
                spot.Index = IndexOfRef(compound.Children, tag);
                return spot;
            }
            var list = parentTag as NbtList;
            if (list != null)
            {
                spot.ParentList = list;
                spot.Index = IndexOfRef(list.Items, tag);
            }
            return spot;
        }

        private static int IndexOfRef(List<NbtTag> items, NbtTag tag)
        {
            for (int i = 0; i < items.Count; i++)
                if (ReferenceEquals(items[i], tag)) return i;
            return -1;
        }

        private TreeNode Selected { get { return _tree.SelectedNode; } }

        private NbtTag SelectedTag
        {
            get
            {
                var node = Selected;
                return node == null ? null : node.Tag as NbtTag;
            }
        }

        /* ---------------------------- 自绘 ---------------------------- */

        /// <summary>
        /// 节点文字自绘：名字用亮色、值按类型上色，一眼能分出整数和字符串。
        /// 树的展开箭头和选中背景仍由系统画，只有文字这一段是我们负责的。
        /// </summary>
        private void DrawNode(DrawTreeNodeEventArgs e)
        {
            var tag = e.Node.Tag as NbtTag;
            var bounds = e.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var g = e.Graphics;

            /* 选中背景与行背景自己画：不画的话 OwnerDrawText 下会用系统浅色，深色主题里是白带。
               起点必须从文字区（bounds.Left）开始 —— 再往左就是展开箭头的地盘，
               从 0 开始画会把 +/- 整个盖掉。 */
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            using (var back = new SolidBrush(selected ? Theme.AccentSoft : Theme.BgInput))
                g.FillRectangle(back, new Rectangle(bounds.Left, bounds.Top,
                    Math.Max(0, _tree.ClientSize.Width - bounds.Left), bounds.Height));

            string name = e.Node.Text;
            int eq = name.IndexOf("  =  ", StringComparison.Ordinal);
            string left = eq < 0 ? name : name.Substring(0, eq);
            string right = eq < 0 ? "" : name.Substring(eq + 5);

            int x = bounds.Left;
            Color nameColor = selected ? Theme.Fg : Theme.FgMuted;

            using (var font = Theme.F(8.8f))
            {
                TextRenderer.DrawText(g, left, font, new Point(x, bounds.Top), nameColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(g, left, font, new Size(int.MaxValue, bounds.Height),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

                if (right.Length > 0)
                {
                    using (var sepFont = Theme.F(8.8f))
                    {
                        TextRenderer.DrawText(g, "  =  ", sepFont, new Point(x, bounds.Top), Theme.FgFaint,
                            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        x += TextRenderer.MeasureText(g, "  =  ", sepFont, new Size(int.MaxValue, bounds.Height),
                            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                    }

                    Color valueColor = tag != null ? TypeColor(tag.Type) : Theme.Fg;
                    TextRenderer.DrawText(g, right, font, new Point(x, bounds.Top), valueColor,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private static Color TypeColor(NbtType type)
        {
            switch (type)
            {
                case NbtType.Byte: return Color.FromArgb(150, 220, 160);
                case NbtType.Short: return Color.FromArgb(130, 214, 214);
                case NbtType.Int: return Color.FromArgb(120, 180, 255);
                case NbtType.Long: return Color.FromArgb(178, 150, 255);
                case NbtType.Float: return Color.FromArgb(255, 190, 120);
                case NbtType.Double: return Color.FromArgb(255, 214, 140);
                case NbtType.String: return Color.FromArgb(240, 226, 150);
                case NbtType.ByteArray: return Color.FromArgb(170, 180, 210);
                case NbtType.ShortArray: return Color.FromArgb(170, 180, 210);
                case NbtType.IntArray: return Color.FromArgb(160, 190, 220);
                case NbtType.LongArray: return Color.FromArgb(170, 170, 230);
                case NbtType.List: return Color.FromArgb(120, 220, 200);
                case NbtType.Compound: return Color.FromArgb(200, 210, 240);
            }
            return Theme.Fg;
        }

        /* ============================================================ */
        /* 选中与详情                                                    */
        /* ============================================================ */

        private void OnSelect()
        {
            var tag = SelectedTag;
            if (tag == null)
            {
                _detailTitle.Text = "未选中任何标签";
                _nameBox.Text = "";
                _valueBox.Text = "";
                _pathLabel.Text = "";
                _detailHint.Text = "";
                RefreshButtons();
                return;
            }

            _detailTitle.Text = NbtTypeInfo.Describe(tag.Type) + "　" + (tag.Name.Length == 0 ? "(无名)" : tag.Name);
            _nameBox.Text = tag.Name;
            _valueBox.Text = NbtTypeInfo.IsContainer(tag.Type) ? "" : tag.EditText();
            _valueBox.ReadOnly = NbtTypeInfo.IsContainer(tag.Type);
            var spot = SpotOf(Selected);
            _pathLabel.Text = spot != null ? spot.Path : "";

            if (NbtTypeInfo.IsContainer(tag.Type))
                _detailHint.Text = "这是容器标签（" + NbtTypeInfo.ShortName(tag.Type) + "），共 " + tag.ChildCount + " 项。\n用「添加子标签…」往里加，或用右键菜单/快捷键复制粘贴。";
            else if (NbtTypeInfo.IsArray(tag.Type))
                _detailHint.Text = "数组：直接填逗号分隔的数字即可；字节数组还能用「改值…」里的十六进制编辑。";
            else
                _detailHint.Text = "改完点「应用修改」，或直接按回车。";

            RefreshButtons();
        }

        private void ApplyDetail()
        {
            var spot = SpotOf(Selected);
            if (spot == null) return;
            var tag = spot.Tag;

            string wantName = _nameBox.Text;
            bool nameChanged = wantName != tag.Name;
            string wantValue = _valueBox.Text;
            bool valueChanged = !NbtTypeInfo.IsContainer(tag.Type) && wantValue != tag.EditText();

            if (!nameChanged && !valueChanged) { Status("没有改动"); return; }

            /* 改名冲突先问清楚：问完再记快照，避免「记了快照结果什么都没做」 */
            bool doRename = nameChanged;
            if (doRename && spot.ParentCompound != null && spot.ParentCompound.HasOther(wantName, tag))
            {
                doRename = PageKit.Confirm(_view,
                    "同层已经有叫「" + wantName + "」的标签了。NBT 允许重名，但游戏读取时后一个会覆盖前一个。仍然改名吗？");
            }

            /* 快照一定要在任何改动之前记，撤销才回得到「改之前」 */
            _history.Push(_doc.Root, _doc.RootName);

            if (valueChanged)
            {
                try { tag.SetFromText(wantValue); }
                catch (Exception e)
                {
                    _history.PopLast();
                    PageKit.Error(_view, e.Message);
                    return;
                }
            }

            if (doRename)
            {
                tag.Name = wantName;
                /* 根名在文档对象里是单独存的，写文件时用的是它，两处必须一起改 */
                if (spot.IsRoot) _doc.RootName = wantName;
            }

            MarkDirty();
            UpdateNodeText(Selected);
            OnSelect();
            Status("已修改 " + spot.Path);
        }

        private void UpdateNodeText(TreeNode node)
        {
            if (node == null) return;
            var tag = node.Tag as NbtTag;
            if (tag == null) return;

            int listIndex = -1;
            if (node.Parent != null)
            {
                var parentTag = node.Parent.Tag as NbtTag;
                var list = parentTag as NbtList;
                if (list != null) listIndex = IndexOfRef(list.Items, tag);
            }

            node.Text = node.Parent == null ? RootText() : DisplayText(tag, listIndex);
            _tree.Invalidate();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _nodeCount = NbtEditOps.CountNodes(_doc.Root);
            RefreshInfo();
            RefreshButtons();
        }

        private void RefreshInfo()
        {
            if (_doc == null) return;
            _info.Text = DescribeDoc();
        }

        private string DescribeDoc()
        {
            if (_doc == null) return "还没有打开文件。";
            string name = string.IsNullOrEmpty(_doc.Path) ? "（未保存）" : Path.GetFileName(_doc.Path);
            return name + "　·　" + _doc.SourceFormat() +
                   "　·　" + _nodeCount + " 个标签" +
                   (_dirty ? "　·　有未保存的修改" : "　·　与磁盘一致") +
                   (_history.Disabled ? "　·　撤销已关闭（文件过大）" : "");
        }

        private void RefreshButtons()
        {
            var tag = SelectedTag;
            bool has = tag != null;
            bool canAdd = has && (tag is NbtCompound || tag is NbtList);
            bool isRoot = has && Selected != null && Selected.Parent == null;
            bool canPaste = has && _clipboard != null;

            if (_saveBtn != null) _saveBtn.Enabled = _doc != null && _dirty;
            if (_undoBtn != null) _undoBtn.Enabled = _history.CanUndo;
            if (_redoBtn != null) _redoBtn.Enabled = _history.CanRedo;
            if (_addBtn != null) _addBtn.Enabled = canAdd;
            if (_renameBtn != null) _renameBtn.Enabled = has;   // 根标签也可以改名（写文件时用的就是根名）
            if (_valueBtn != null) _valueBtn.Enabled = has && !NbtTypeInfo.IsContainer(tag == null ? NbtType.End : tag.Type);
            if (_deleteBtn != null) _deleteBtn.Enabled = has && !isRoot;
            if (_cutBtn != null) _cutBtn.Enabled = has && !isRoot;
            if (_copyBtn != null) _copyBtn.Enabled = has;
            if (_pasteBtn != null) _pasteBtn.Enabled = canPaste;
            if (_upBtn != null) _upBtn.Enabled = has && !isRoot;
            if (_downBtn != null) _downBtn.Enabled = has && !isRoot;
        }

        /* ============================================================ */
        /* 编辑命令                                                      */
        /* ============================================================ */

        private void AddChild()
        {
            var spot = SpotOf(Selected);
            if (spot == null) return;

            var compound = spot.Tag as NbtCompound;
            var list = spot.Tag as NbtList;
            if (compound == null && list == null)
            {
                PageKit.Error(_view, "只有 TAG_Compound 和 TAG_List 能添加子标签。");
                return;
            }

            NbtType defaultType = list != null
                ? (list.ElementType == NbtType.End ? NbtType.Compound : list.ElementType)
                : NbtType.Compound;

            using (var dlg = new NbtAddForm(defaultType))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;

                NbtTag created;
                try
                {
                    created = NbtTypeInfo.CreateEmpty(dlg.PickedType, dlg.PickedName.Trim());
                    if (!NbtTypeInfo.IsContainer(dlg.PickedType)) created.SetFromText(dlg.PickedValue);
                }
                catch (Exception e)
                {
                    PageKit.Error(_view, e.Message);
                    return;
                }

                _history.Push(_doc.Root, _doc.RootName);
                try
                {
                    if (compound != null)
                    {
                        if (compound.Has(created.Name))
                        {
                            string unique = NbtEditOps.UniqueName(compound, created.Name);
                            Status("已有同名标签，新标签改名为「" + unique + "」");
                            created.Name = unique;
                        }
                        compound.Add(created);
                    }
                    else
                    {
                        created.Name = "";
                        list.Add(created);
                    }
                }
                catch (Exception e)
                {
                    PageKit.Error(_view, e.Message);
                    return;
                }

                MarkDirty();
                RefreshChildrenOf(Selected);
                var node = FindNode(_tree.Nodes, created);
                if (node != null) { _tree.SelectedNode = node; node.EnsureVisible(); }
                Status("已添加 " + NbtTypeInfo.TagName(created.Type) + "「" + created.Name + "」");
            }
        }

        /// <summary>只重建某个节点的子层，避免整棵树重画</summary>
        private void RefreshChildrenOf(TreeNode node)
        {
            if (node == null) return;

            var tag = node.Tag as NbtTag;
            bool wasExpanded = node.IsExpanded;
            _filled.Remove(node);
            node.Nodes.Clear();
            _filled.Remove(node);

            if (tag != null && tag.HasChildren) node.Nodes.Add(new TreeNode("读取中…"));
            if (wasExpanded)
            {
                FillChildren(node);
                node.Expand();
            }
            UpdateNodeText(node);
            _tree.Invalidate();
        }

        private void Rename()
        {
            var spot = SpotOf(Selected);
            if (spot == null) return;

            using (var dlg = new NbtInputForm("改名", spot.IsRoot ? "根标签名" : "标签名", spot.Tag.Name))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                string want = dlg.Value.Trim();
                if (want.Length == 0) { PageKit.Error(_view, "标签名不能为空。"); return; }
                if (want == spot.Tag.Name) return;

                _history.Push(_doc.Root, _doc.RootName);
                spot.Tag.Name = want;
                if (spot.IsRoot) _doc.RootName = want;

                MarkDirty();
                RefreshChildrenOf(Selected.Parent);
                UpdateNodeText(Selected);
                OnSelect();
                Status("已改名为「" + want + "」");
            }
        }

        private void EditValue()
        {
            var spot = SpotOf(Selected);
            if (spot == null || NbtTypeInfo.IsContainer(spot.Tag.Type)) return;

            using (var dlg = new NbtValueForm(spot.Tag))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
            }
        }

        private void Delete()
        {
            var spot = SpotOf(Selected);
            if (spot == null || spot.IsRoot) return;

            string what = NbtTypeInfo.TagName(spot.Tag.Type) + "「" + spot.Tag.Name + "」";
            if (!PageKit.Confirm(_view, "确定删除 " + what + " 吗？" + (spot.Tag.HasChildren ? "\n它下面的 " + spot.Tag.ChildCount + " 个子项会一起删掉。" : "")))
                return;

            _history.Push(_doc.Root, _doc.RootName);
            var parentNode = Selected.Parent;
            spot.Detach();

            MarkDirty();
            RefreshChildrenOf(parentNode);
            if (parentNode != null) _tree.SelectedNode = parentNode;
            Status("已删除 " + what);
        }

        private void Copy()
        {
            var spot = SpotOf(Selected);
            if (spot == null) return;
            _clipboard = spot.Tag.Clone();
            _clipboardPath = spot.Path;
            Status("已复制 " + NbtTypeInfo.TagName(_clipboard.Type) + "（" + spot.Path + "）");
            RefreshButtons();
        }

        private void Cut()
        {
            var spot = SpotOf(Selected);
            if (spot == null || spot.IsRoot) return;
            _clipboard = spot.Tag.Clone();
            _clipboardPath = spot.Path;

            _history.Push(_doc.Root, _doc.RootName);
            var parentNode = Selected.Parent;
            spot.Detach();
            MarkDirty();
            RefreshChildrenOf(parentNode);
            if (parentNode != null) _tree.SelectedNode = parentNode;
            Status("已剪切 " + NbtTypeInfo.TagName(_clipboard.Type));
        }

        private void Paste()
        {
            var spot = SpotOf(Selected);
            if (spot == null) return;

            /* 选中的是标量时，粘到它所属的容器里（NBTExplorer 也是这个习惯） */
            NbtTag target = spot.Tag;
            if (!(target is NbtCompound) && !(target is NbtList))
            {
                if (spot.IsRoot) { PageKit.Error(_view, "根标签不是容器，没法粘贴。"); return; }
                target = spot.ParentTag;
                spot = SpotOf(Selected.Parent);
            }

            string reason;
            if (!NbtEditOps.CanPaste(target, _clipboard, out reason))
            {
                PageKit.Error(_view, reason);
                return;
            }

            /* 刷新哪一层要在粘贴之前定下来：粘完 Selected 可能就变了 */
            TreeNode refreshTarget = target == spot.Tag ? Selected : Selected.Parent;

            _history.Push(_doc.Root, _doc.RootName);
            var created = NbtEditOps.Paste(target, _clipboard);
            if (created == null) { _history.PopLast(); return; }

            MarkDirty();
            RefreshChildrenOf(refreshTarget);

            var node = FindNode(_tree.Nodes, created);
            if (node != null) { _tree.SelectedNode = node; node.EnsureVisible(); }
            Status("已粘贴 " + NbtTypeInfo.TagName(created.Type) + "（来源 " + _clipboardPath + "）");
        }

        private void Move(int delta)
        {
            var spot = SpotOf(Selected);
            if (spot == null || spot.IsRoot) return;

            /* 先看还能不能动，动不了就别浪费一次快照 */
            int count = spot.ParentCompound != null ? spot.ParentCompound.Children.Count
                      : spot.ParentList != null ? spot.ParentList.Items.Count : 0;
            if (delta < 0 && spot.Index <= 0) { Status("已经在最前面了"); return; }
            if (delta > 0 && spot.Index >= count - 1) { Status("已经在最后面了"); return; }

            var parentNode = Selected.Parent;
            _history.Push(_doc.Root, _doc.RootName);   // 顺序变了也算改动，撤销要能还原

            bool ok = delta < 0 ? NbtEditOps.MoveUp(spot) : NbtEditOps.MoveDown(spot);
            if (!ok) { _history.PopLast(); return; }

            MarkDirty();
            RefreshChildrenOf(parentNode);
            var node = FindNode(_tree.Nodes, spot.Tag);
            if (node != null) { _tree.SelectedNode = node; node.EnsureVisible(); }
            Status("已" + (delta < 0 ? "上移" : "下移"));
        }

        private void Undo()
        {
            if (_doc == null) return;
            var root = _doc.Root;
            var name = _doc.RootName;
            if (!_history.Undo(ref root, ref name)) { Status("没有可撤销的操作"); return; }
            _doc.Root = root;
            _doc.RootName = name;
            _dirty = true;
            _nodeCount = NbtEditOps.CountNodes(_doc.Root);
            RebuildTree(null);
            Status("已撤销");
        }

        private void Redo()
        {
            if (_doc == null) return;
            var root = _doc.Root;
            var name = _doc.RootName;
            if (!_history.Redo(ref root, ref name)) { Status("没有可重做的操作"); return; }
            _doc.Root = root;
            _doc.RootName = name;
            _dirty = true;
            _nodeCount = NbtEditOps.CountNodes(_doc.Root);
            RebuildTree(null);
            Status("已重做");
        }

        private void ExpandAll(bool expand)
        {
            if (!expand) { _tree.CollapseAll(); _expanded.Clear(); if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand(); return; }

            if (_nodeCount > 20000 && !PageKit.Confirm(_view, "这棵树有 " + _nodeCount + " 个标签，全部展开可能要等一会儿。继续吗？"))
                return;

            _tree.BeginUpdate();
            try
            {
                ExpandRecursive(_tree.Nodes);
            }
            finally { _tree.EndUpdate(); }
            Status("已全部展开");
        }

        private void ExpandRecursive(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Nodes.Count == 0) continue;
                FillChildren(n);
                n.Expand();
                ExpandRecursive(n.Nodes);
            }
        }

        /* ============================================================ */
        /* 查找 / 导入导出                                               */
        /* ============================================================ */

        private void Find()
        {
            if (_doc == null) { PageKit.Error(_view, "先打开一个 NBT 文件。"); return; }

            using (var dlg = new NbtFindForm(_doc.Root, null))
            {
                dlg.Navigate = spot => LocateSpot(spot);
                dlg.Replaced = () =>
                {
                    if (!_history.Disabled) _history.Push(_doc.Root, _doc.RootName);
                    MarkDirty();
                    RebuildTree(null);
                };
                dlg.ShowDialog(_view);
            }

            /* 替换窗口里改了值，回来把树上文字刷新一遍 */
            if (_doc != null) { RebuildTree(null); RefreshInfo(); }
        }

        private void LocateSpot(NbtSpot spot)
        {
            if (spot == null || spot.Tag == null) return;

            /* 从根一路展开到目标 */
            var chain = new List<NbtTag>();
            if (!BuildChain(_doc.Root, spot.Tag, chain)) return;

            var nodes = _tree.Nodes;
            TreeNode node = null;
            foreach (var tag in chain)
            {
                node = FindNode(nodes, tag);
                if (node == null) break;
                FillChildren(node);
                node.Expand();
                nodes = node.Nodes;
            }

            if (node != null)
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                Status("定位到 " + spot.Path);
            }
        }

        private static bool BuildChain(NbtTag node, NbtTag target, List<NbtTag> chain)
        {
            chain.Add(node);
            if (ReferenceEquals(node, target)) return true;

            var compound = node as NbtCompound;
            if (compound != null)
            {
                foreach (var child in compound.Children)
                    if (BuildChain(child, target, chain)) return true;
            }
            var list = node as NbtList;
            if (list != null)
            {
                foreach (var item in list.Items)
                    if (BuildChain(item, target, chain)) return true;
            }
            chain.RemoveAt(chain.Count - 1);
            return false;
        }

        private void ExportSnbt()
        {
            if (_doc == null) { PageKit.Error(_view, "先打开一个 NBT 文件。"); return; }
            var tag = SelectedTag;
            if (tag == null) tag = _doc.Root;

            string text = NbtSnbt.FormatPretty(tag);
            string hint = "选中标签的 SNBT 文本（" + NbtTypeInfo.TagName(tag.Type) + "）。可以直接复制走，也可以存成文件。";

            using (var dlg = new NbtTextForm("导出 SNBT", hint, text, true, null))
            {
                dlg.ShowDialog(_view);
            }

            if (PageKit.Confirm(_view, "要把这段 SNBT 存成文本文件吗？"))
            {
                string path = PageKit.PickSave(_view, "保存 SNBT 文本", "SNBT 文本 (*.snbt)|*.snbt|文本文件 (*.txt)|*.txt", "export.snbt");
                if (path != null)
                {
                    try
                    {
                        File.WriteAllText(path, text, new UTF8Encoding(false));
                        Status("SNBT 已导出到 " + path);
                    }
                    catch (Exception e) { PageKit.Error(_view, "写入失败：" + e.Message); }
                }
            }
        }

        private void ImportSnbt()
        {
            if (_doc == null) { PageKit.Error(_view, "先打开一个 NBT 文件。"); return; }
            var spot = SpotOf(Selected);
            if (spot == null) { PageKit.Error(_view, "先选中要挂在哪个容器下面。"); return; }
            if (!(spot.Tag is NbtCompound) && !(spot.Tag is NbtList))
            {
                PageKit.Error(_view, "只能导入到 TAG_Compound 或 TAG_List 下面。");
                return;
            }

            using (var dlg = new NbtTextForm("从 SNBT 导入",
                "粘贴一段 SNBT，例如 {Name:\"Steve\",Health:20.0f} 或 [1,2,3]。可以一次粘多个标签，用换行分隔。",
                "", false, text => DescribeParseError(text)))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;

                var parsed = ParseMany(dlg.TextValue);
                if (parsed.Count == 0) { PageKit.Error(_view, "没有解析出任何标签。"); return; }

                _history.Push(_doc.Root, _doc.RootName);
                int added = 0;
                var failures = new List<string>();

                foreach (var tag in parsed)
                {
                    try
                    {
                        var compound = spot.Tag as NbtCompound;
                        if (compound != null)
                        {
                            tag.Name = NbtEditOps.UniqueName(compound, tag.Name);
                            compound.Add(tag);
                        }
                        else
                        {
                            tag.Name = "";
                            ((NbtList)spot.Tag).Add(tag);
                        }
                        added++;
                    }
                    catch (Exception e) { failures.Add(e.Message); }
                }

                MarkDirty();
                RefreshChildrenOf(Selected);
                Status("已导入 " + added + " 个标签" + (failures.Count > 0 ? "，失败 " + failures.Count + " 个" : ""));
                if (failures.Count > 0) PageKit.Error(_view, "有 " + failures.Count + " 个标签没能加进去：\n\n" + failures[0]);
            }
        }

        private static string DescribeParseError(string text)
        {
            var list = ParseMany(text);
            if (list.Count == 0) return "没能解析出任何标签，检查一下括号和引号是否配对。";
            return null;
        }

        /// <summary>支持一次贴多个标签：整体解析失败时，按行再试一次</summary>
        private static List<NbtTag> ParseMany(string text)
        {
            var result = new List<NbtTag>();
            if (text == null) return result;

            NbtTag whole;
            string error;
            if (NbtSnbt.TryParse(text, out whole, out error))
            {
                result.Add(whole);
                return result;
            }

            foreach (string line in text.Split('\n'))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;
                NbtTag tag;
                string e2;
                if (NbtSnbt.TryParse(s, out tag, out e2)) result.Add(tag);
            }
            return result;
        }

        private void ExportJson()
        {
            if (_doc == null) { PageKit.Error(_view, "先打开一个 NBT 文件。"); return; }
            var tag = SelectedTag;
            if (tag == null) tag = _doc.Root;

            string text;
            try { text = NbtJson.Write(tag, true); }
            catch (Exception e) { PageKit.Error(_view, "导出失败：" + e.Message); return; }

            using (var dlg = new NbtTextForm("导出 JSON",
                "对应 NBTExplorer 的 NBTUtil --print-json。注意：超过 2^53 的长整数在 JSON 里会丢精度，需要无损请用 SNBT。",
                text, true, null))
            {
                dlg.ShowDialog(_view);
            }

            if (PageKit.Confirm(_view, "要把这段 JSON 存成文件吗？"))
            {
                string path = PageKit.PickSave(_view, "保存 JSON", "JSON 文件 (*.json)|*.json|文本文件 (*.txt)|*.txt", "export.json");
                if (path != null)
                {
                    try
                    {
                        File.WriteAllText(path, text, new UTF8Encoding(false));
                        Status("JSON 已导出到 " + path);
                    }
                    catch (Exception e) { PageKit.Error(_view, "写入失败：" + e.Message); }
                }
            }
        }

        /* ============================================================ */
        /* 快捷键                                                        */
        /* ============================================================ */

        private void OnTreeKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { Delete(); e.Handled = true; return; }
            if (e.KeyCode == Keys.F2) { Rename(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter) { EditValue(); e.Handled = true; return; }

            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.C: Copy(); e.Handled = true; return;
                    case Keys.X: Cut(); e.Handled = true; return;
                    case Keys.V: Paste(); e.Handled = true; return;
                    case Keys.Z: Undo(); e.Handled = true; return;
                    case Keys.Y: Redo(); e.Handled = true; return;
                    case Keys.F: Find(); e.Handled = true; return;
                    case Keys.S: Save(false); e.Handled = true; return;
                }
            }
        }

        /* ============================================================ */

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
