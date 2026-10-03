using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 版本转换 · 批量转换                                                  */
    /* ================================================================== */

    internal sealed class BatchPage : IModulePage
    {
        public string Title { get { return "批量转换"; } }
        public string Subtitle { get { return "一次把多个资源包转换到同一目标版本，逐条给出结果"; } }

        private Control _view;
        private TextBox _rootBox, _outBox;
        private FlatSelect _targetCombo;
        private ToolCheck _migrateAssets;
        private ToolList _list;
        private RichTextBox _log;
        private FancyProgress _progress;
        private FlatBtn _scanBtn, _runBtn, _outBrowse;
        private Label _summary;
        private bool _running;

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private sealed class Item
        {
            public string Path;
            public string Name;
            public bool IsZip;
            public string Result = "待处理";
        }

        private readonly List<Item> _items = new List<Item>();

        private sealed class ComboItem
        {
            public int Format;
            public string Text;
            public override string ToString() { return Text; }
        }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
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

            /* ---- 设置卡 ---- */
            var card = new Card { Title = "批量设置", Hint = "扫描所选文件夹下的所有资源包", Height = 246, Margin = new Padding(0, 0, 0, 12) };

            var pick = new FlatBtn("选择文件夹…", BtnKind.Outline, 120) { Dock = DockStyle.Right };
            pick.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择包含若干资源包的文件夹（一级子文件夹与 .zip 都会被扫描）";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    _rootBox.Text = dlg.SelectedPath;
                }
            };
            _rootBox = Ui.ReadOnlyBox("");
            _rootBox.Dock = DockStyle.Fill;
            var row1 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row1.Controls.Add(_rootBox);
            row1.Controls.Add(pick);
            var lbl1 = MakeLabel("资源包所在文件夹");

            _targetCombo = new FlatSelect { Dock = DockStyle.Left, Width = 360 };
            Ui.StyleSelect(_targetCombo);
            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format))
            {
                _targetCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            }
            if (_targetCombo.Items.Count > 0) _targetCombo.SelectedIndex = 0;

            _migrateAssets = new ToolCheck("同时迁移资源路径", true, 170) { Dock = DockStyle.Left, Padding = new Padding(14, 3, 0, 0) };
            var row2 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row2.Controls.Add(_migrateAssets);
            row2.Controls.Add(_targetCombo);
            var lbl2 = MakeLabel("目标版本");

            _outBrowse = new FlatBtn("浏览…", BtnKind.Outline, 76) { Dock = DockStyle.Right };
            _outBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择输出根目录（每个资源包会输出到它自己的子文件夹）";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    _outBox.Text = dlg.SelectedPath;
                }
            };
            _outBox = Ui.ReadOnlyBox("");
            _outBox.Font = Theme.M(8.5f);
            _outBox.Dock = DockStyle.Fill;
            var row3 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row3.Controls.Add(_outBox);
            row3.Controls.Add(_outBrowse);
            var lbl3 = MakeLabel("输出目录");

            var sp1 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };
            var sp2 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };

            // Dock.Top 后加的在上，所以按视觉逆序添加
            card.Body.Controls.Add(row3);
            card.Body.Controls.Add(sp2);
            card.Body.Controls.Add(lbl3);
            card.Body.Controls.Add(row2);
            card.Body.Controls.Add(sp1);
            card.Body.Controls.Add(lbl2);
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(lbl1);
            stack.Controls.Add(card);

            /* ---- 列表卡 ---- */
            var listCard = new Card { Title = "扫描结果", Hint = "点「开始批量转换」后逐条处理", Height = 380, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _scanBtn = new FlatBtn("① 扫描资源包", BtnKind.Normal, 116) { Margin = new Padding(0, 0, 8, 0) };
            _runBtn = new FlatBtn("② 开始批量转换", BtnKind.Primary, 132) { Margin = new Padding(0, 0, 8, 0), Enabled = false };
            _progress = new FancyProgress { Height = 16, Width = 240, Margin = new Padding(8, 8, 0, 0) };
            _summary = new Label { AutoSize = true, ForeColor = Theme.FgDim, Font = Theme.F(8.5f), Margin = new Padding(12, 9, 0, 0), BackColor = Color.Transparent };
            bar.Controls.Add(_scanBtn);
            bar.Controls.Add(_runBtn);
            bar.Controls.Add(_progress);
            bar.Controls.Add(_summary);

            _scanBtn.Click += (s, e) => DoScan();
            _runBtn.Click += (s, e) => DoRun();

            var split = PageKit.MakeSplit(Orientation.Horizontal, 0.6, 90, 70);
            _list = new ToolList
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
            _list.SetColumns("#", 40, "资源包", 240, "类型", 70, "结果", -1);
            _log = Ui.LogBox();
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_log);

            var gap = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Color.Transparent };
            listCard.Body.Controls.Add(split);
            listCard.Body.Controls.Add(gap);
            listCard.Body.Controls.Add(bar);
            stack.Controls.Add(listCard);

            _view = host;
            return host;
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 2),
                BackColor = Color.Transparent,
            };
        }

        public void OnEnter() { }
        public void OnLeave() { }

        private void DoScan()
        {
            string root = _rootBox.Text;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                MessageBox.Show(_view, "请先选择一个有效的文件夹。", "批量转换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _items.Clear();
            _list.Items.Clear();
            _log.Clear();

            // 顶层 .zip
            foreach (string f in Directory.GetFiles(root, "*.zip"))
            {
                _items.Add(new Item { Path = f, Name = Path.GetFileNameWithoutExtension(f), IsZip = true });
            }
            // 子目录里带 pack.mcmeta 的
            foreach (string d in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(d, "pack.mcmeta")))
                {
                    _items.Add(new Item { Path = d, Name = new DirectoryInfo(d).Name, IsZip = false });
                }
            }

            if (_items.Count == 0)
            {
                Ui.AppendLog(_log, "在 " + root + " 下没找到资源包。");
                Ui.AppendLog(_log, "说明：只扫描该文件夹顶层的 .zip，以及直接包含 pack.mcmeta 的一级子文件夹。");
                _summary.Text = "扫描到 0 个资源包";
                _runBtn.Enabled = false;
                Status("批量转换：没有扫描到资源包");
                return;
            }

            int i = 1;
            foreach (var it in _items)
            {
                var row = new ListViewItem(new[] { i.ToString(), it.Name, it.IsZip ? "ZIP" : "文件夹", "待处理" });
                row.Tag = it;
                _list.Items.Add(row);
                i++;
            }

            if (string.IsNullOrEmpty(_outBox.Text))
                _outBox.Text = Path.Combine(root, "_converted");

            _summary.Text = "扫描到 " + _items.Count + " 个资源包";
            _runBtn.Enabled = true;
            Status("批量转换：扫描到 " + _items.Count + " 个资源包");
        }

        private void DoRun()
        {
            if (_running) return;
            var sel = _targetCombo.SelectedItem as ComboItem;
            if (sel == null) { MessageBox.Show(_view, "请选择目标版本。", "批量转换", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (string.IsNullOrEmpty(_outBox.Text)) { MessageBox.Show(_view, "请选择输出目录。", "批量转换", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_items.Count == 0) { MessageBox.Show(_view, "请先扫描资源包。", "批量转换", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            _running = true;
            _runBtn.Enabled = false;
            _scanBtn.Enabled = false;
            _progress.Maximum = _items.Count;
            _progress.Value = 0;
            _log.Clear();

            int ok = 0, fail = 0;
            string label = sel.Text.Replace("pack_format " + sel.Format + "   ", "");
            Directory.CreateDirectory(_outBox.Text);

            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var it = _items[i];
                    var row = _list.Items[i];
                    Status("批量转换 " + (i + 1) + " / " + _items.Count + "：" + it.Name);
                    Ui.AppendLog(_log, "[" + (i + 1) + "/" + _items.Count + "] " + it.Name);

                    try
                    {
                        var analysis = PackAnalyzer.Analyze(it.Path);
                        if (!analysis.Ok)
                        {
                            it.Result = "跳过：" + analysis.Error;
                            fail++;
                            Ui.AppendLog(_log, "    跳过：" + analysis.Error);
                        }
                        else
                        {
                            string outPath = Path.Combine(_outBox.Text, it.Name + (it.IsZip ? ".zip" : ""));
                            var opt = new ConvertOptions
                            {
                                Mode = it.IsZip ? OutputMode.Zip : OutputMode.Folder,
                                OutputPath = outPath,
                                TargetFormat = sel.Format,
                                TargetVersionLabel = label,
                                MigrateAssets = _migrateAssets.Checked,
                                MigrateLangContent = _migrateAssets.Checked,
                            };
                            var plan = PlanBuilder.Build(analysis, opt);
                            var report = ConvertExecutor.Execute(plan, null);

                            if (report.Success)
                            {
                                it.Result = "成功 —— 输出 " + report.Written + " 个文件（重命名 " + report.Renamed + "，改写 " + report.Rewritten + "）→ " + outPath;
                                ok++;
                                Ui.AppendLog(_log, "    成功：" + report.Written + " 个文件，重命名 " + report.Renamed + "，改写 " + report.Rewritten);
                            }
                            else
                            {
                                it.Result = "失败：" + report.Error;
                                fail++;
                                Ui.AppendLog(_log, "    失败：" + report.Error);
                            }
                            analysis.Cleanup();
                        }
                    }
                    catch (Exception e)
                    {
                        it.Result = "异常：" + e.Message;
                        fail++;
                        Ui.AppendLog(_log, "    异常：" + e.Message);
                    }

                    if (row != null)
                    {
                        row.SubItems[3].Text = it.Result;
                        row.ForeColor = it.Result.StartsWith("成功") ? Theme.Ok
                            : it.Result.StartsWith("跳过") ? Theme.Warn : Theme.Danger;
                    }
                    _progress.Value = i + 1;
                    Application.DoEvents();
                }
            }
            finally
            {
                _running = false;
                _runBtn.Enabled = true;
                _scanBtn.Enabled = true;
            }

            _summary.Text = "完成：成功 " + ok + "，失败 " + fail;
            Ui.AppendLog(_log, "");
            Ui.AppendLog(_log, "=== 批量转换结束：成功 " + ok + " 个，失败 " + fail + " 个 ===");
            Status("批量转换完成：成功 " + ok + " 个，失败 " + fail + " 个");

            MessageBox.Show(_view,
                "批量转换完成。\n\n成功 " + ok + " 个，失败 " + fail + " 个。\n输出目录：\n" + _outBox.Text,
                "批量转换", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /* ================================================================== */
    /* 工具箱 · 版本对照表                                                  */
    /* ================================================================== */

    internal sealed class VersionTablePage : IModulePage
    {
        public string Title { get { return "版本对照表"; } }
        public string Subtitle { get { return "查询 pack_format 与 Minecraft 版本的对应关系，共 " + Settings.AllVersions().Count + " 条"; } }

        private Control _view;
        private ToolList _list;
        private TextBox _filter;
        private Label _summary;

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgBase, Padding = new Padding(18, 16, 18, 14) };

            var card = new Card { Dock = DockStyle.Fill, Title = "pack_format 对照表", Hint = "只显示正式版；包含快照的格式会标注" };

            var bar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
            _filter = new TextBox
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
            _filter.TextChanged += (s, e) => Fill();
            bar.Controls.Add(_filter);

            _summary = new Label
            {
                Dock = DockStyle.Right,
                Width = 200,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                BackColor = Color.Transparent,
            };
            bar.Controls.Add(_summary);

            var hint = Ui.Caption("输入 pack_format 或版本号过滤，例如 46、1.20、snapshot");
            hint.Dock = DockStyle.Fill;
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Padding = new Padding(12, 0, 0, 0);
            bar.Controls.Add(hint);

            _list = new ToolList
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.F(9f),
                HideSelection = false,
            };
            _list.SetColumns("pack_format", 110, "版本范围", 260, "阶段", -1, "版本数量", 90);

            var gap = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Color.Transparent };

            card.Body.Controls.Add(_list);
            card.Body.Controls.Add(gap);
            card.Body.Controls.Add(bar);

            host.Controls.Add(card);
            _view = host;
            Fill();
            return host;
        }

        public void OnEnter() { }
        public void OnLeave() { }

        private void Fill()
        {
            if (_list == null) return;
            string q = (_filter.Text ?? "").Trim().ToLowerInvariant();

            _list.BeginUpdate();
            _list.Items.Clear();

            var all = Settings.DisplayVersions().OrderByDescending(v => v.Format).ToList();
            int shown = 0;
            foreach (var v in all)
            {
                string desc = Settings.Describe(v);
                string stage = Settings.DescribeStage(v.Format);
                int count = v.AllVersions != null ? v.AllVersions.Length : v.Versions.Length;

                if (q.Length > 0)
                {
                    string hay = v.Format + " " + desc + " " + stage + " " + string.Join(" ", v.AllVersions ?? v.Versions);
                    if (hay.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }

                var row = new ListViewItem(new[]
                {
                    v.Format.ToString(),
                    desc,
                    stage,
                    count.ToString(),
                });
                row.ForeColor = v.Format >= Settings.NewFormatThreshold ? Theme.Info : Theme.Fg;
                _list.Items.Add(row);
                shown++;
            }

            _list.EndUpdate();
            if (_summary != null) _summary.Text = "显示 " + shown + " / " + all.Count + " 条";
        }
    }

    /* ================================================================== */
    /* 设置 · 转换规则说明                                                  */
    /* ================================================================== */

    internal sealed class RulesPage : IModulePage
    {
        public string Title { get { return "转换规则说明"; } }
        public string Subtitle { get { return "本工具会做哪些迁移、不会做哪些，以及原因"; } }

        private Control _view;

        public event EventHandler<string> StatusChanged;

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();

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

            /* ---- 会做的 ---- */
            const int bodyPad = 24;                 // 卡片内容左右留白
            int bodyW = CardDefWidth - bodyPad * 2;

            var doCard = new Card
            {
                Title = "会做的迁移",
                Hint = "全部集中在 1.13「扁平化」，有官方记录可核对",
                BodyPadding = new Padding(18, 6, 18, 10),
            };

            int y = 4;
            foreach (var rule in MigrationRules.All)
            {
                var before = new Label
                {
                    Text = rule.OldPrefix,
                    Font = Theme.M(9f),
                    ForeColor = Theme.Danger,
                    AutoSize = true,
                    Location = new Point(bodyPad, y),
                    BackColor = Color.Transparent,
                };
                var arrow = new Label
                {
                    Text = "→",
                    Font = Theme.F(10f, FontStyle.Bold),
                    ForeColor = Theme.FgFaint,
                    AutoSize = true,
                    Location = new Point(bodyPad + 256, y - 2),
                    BackColor = Color.Transparent,
                };
                var after = new Label
                {
                    Text = rule.NewPrefix,
                    Font = Theme.M(9f),
                    ForeColor = Theme.Ok,
                    AutoSize = true,
                    Location = new Point(bodyPad + 286, y),
                    BackColor = Color.Transparent,
                };
                doCard.Body.Controls.Add(before);
                doCard.Body.Controls.Add(arrow);
                doCard.Body.Controls.Add(after);

                /* 说明文字会折行，高度必须实测，写死 20 就会被裁掉半行 */
                doCard.Body.Controls.Add(WrapLabel(rule.Title + " —— " + rule.Detail,
                    Theme.F(8.5f), Theme.FgDim, bodyW, bodyPad, y + 22));

                y += 22 + MeasureHeight(rule.Title + " —— " + rule.Detail, Theme.F(8.5f), bodyW) + 16;
            }

            doCard.Body.Controls.Add(WrapLabel(
                "语言文件：地区码 en_US → en_us；扩展名 .lang → .json；内容 key=value 转 JSON；" +
                "键名 block.stone / tile.chest → block.minecraft.stone",
                Theme.F(8.5f), Theme.FgMuted, bodyW, bodyPad, y + 2));

            doCard.Height = Card.HeaderHeight + 6 + y + 2 +
                MeasureHeight("语言文件：地区码 en_US → en_us；扩展名 .lang → .json；内容 key=value 转 JSON；" +
                              "键名 block.stone / tile.chest → block.minecraft.stone",
                              Theme.F(8.5f), bodyW) + 10;
            stack.Controls.Add(doCard);

            /* ---- 不会做的 ---- */
            var dontCard = new Card
            {
                Title = "不会做的改动",
                Hint = "没有权威对照表，做了就是猜",
                BodyPadding = new Padding(18, 6, 18, 10),
            };
            var dont = new[]
            {
                "个别贴图改名（如某方块贴图在新版本换了文件名）—— 各版本零散变动，没有完整清单",
                "模型与方块状态的文件内容 —— 只跟随目录迁移，JSON 内容原样保留",
                "1.8 时代的旧模型格式转换 —— 与今天差异较大，需要手工调整",
                "语言键的三段式自定义键（block.mymod.xxx）—— 判定为 mod 键，一律不动",
                "组件系统（1.20.5+）相关的资源重写",
                "音效 sounds.json 的结构调整",
            };

            int yy = 2;
            foreach (string s in dont)
            {
                string text = "·  " + s;
                int h = MeasureHeight(text, Theme.F(8.2f), bodyW);
                dontCard.Body.Controls.Add(WrapLabel(text, Theme.F(8.2f), Theme.FgMuted, bodyW, bodyPad, yy));
                yy += h + 12;
            }
            dontCard.Height = Card.HeaderHeight + 6 + yy + 6;
            stack.Controls.Add(dontCard);

            /* ---- 建议 ---- */
            var tipCard = new Card
            {
                Title = "使用建议",
                BodyPadding = new Padding(18, 6, 18, 10),
            };
            var tips = new[]
            {
                "转换后建议在游戏里实际加载一次资源包验证效果。",
                "转换报告会列出每一处改动，可以逐项核对。",
                "在预览表格里可以取消勾选任意文件把它排除掉。",
                "只想改版本号、不动任何资源文件时，取消勾选「迁移资源路径」。",
            };

            int ty = 2;
            foreach (string s in tips)
            {
                string text = "·  " + s;
                int h = MeasureHeight(text, Theme.F(8.8f), bodyW);
                tipCard.Body.Controls.Add(WrapLabel(text, Theme.F(8.8f), Theme.FgMuted, bodyW, bodyPad, ty));
                ty += h + 10;
            }
            tipCard.Height = Card.HeaderHeight + 6 + ty + 6;
            stack.Controls.Add(tipCard);

            _view = host;
            return host;
        }

        /// <summary>卡片大概宽度，用于提前算好折行高度（实际宽度由 stack 统一设定）</summary>
        private const int CardDefWidth = 1100;

        /// <summary>这段文字在给定宽度下需要多高</summary>
        private static int MeasureHeight(string text, Font f, int width)
        {
            return TextRenderer.MeasureText(text, f, new Size(width, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 2;
        }

        /// <summary>按实测高度创建的说明文字块，不会被裁行</summary>
        private static Label WrapLabel(string text, Font f, Color color, int width, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = f,
                ForeColor = color,
                AutoSize = false,
                Width = width,
                Height = MeasureHeight(text, f, width),
                Location = new Point(x, y),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent,
            };
        }

        public void OnEnter() { }
        public void OnLeave() { }
    }

    /* ================================================================== */
    /* 设置 · 关于与自测                                                    */
    /* ================================================================== */

    internal sealed class AboutPage : IModulePage
    {
        public string Title { get { return "关于与自测"; } }
        public string Subtitle { get { return "版本信息、数据来源，以及运行内置的 232 项自测"; } }

        private Control _view;
        private RichTextBox _log;
        private FlatBtn _runBtn;
        private ReportGrid _info;

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgBase, Padding = new Padding(18, 16, 18, 14) };

            var split = PageKit.MakeSplit(Orientation.Horizontal, 0.46, 130, 130);

            /* ---- 上：信息 + 自测按钮 ---- */
            var top = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgBase };

            /* 按钮条先加（Dock.Bottom），信息卡再 Fill 剩下的 */
            var btnBar = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.BgBase };
            _runBtn = new FlatBtn("运行内置自测", BtnKind.Primary, 130) { Location = new Point(0, 8) };
            _runBtn.Click += (s, e) => RunSelfTest();
            btnBar.Controls.Add(_runBtn);
            var note = Ui.Caption("自测会在临时目录里跑完整转换流程并逐项断言，不会动你的文件");
            note.Location = new Point(146, 16);
            btnBar.Controls.Add(note);

            var infoCard = new Card { Dock = DockStyle.Fill, Title = "程序信息" };
            _info = new ReportGrid { Dock = DockStyle.Fill };
            infoCard.Body.Padding = new Padding(12, 8, 12, 12);
            infoCard.Body.Controls.Add(_info);
            FillInfo();

            top.Controls.Add(infoCard);
            top.Controls.Add(btnBar);

            /* ---- 下：自测输出 ---- */
            var logCard = new Card { Dock = DockStyle.Fill, Title = "自测输出" };
            _log = Ui.LogBox();
            _log.Dock = DockStyle.Fill;
            logCard.Body.Padding = new Padding(12, 8, 12, 12);
            logCard.Body.Controls.Add(_log);

            split.Panel1.Controls.Add(top);
            split.Panel2.Controls.Add(logCard);
            host.Controls.Add(split);

            _view = host;
            return host;
        }

        private void FillInfo()
        {
            _info.Rows.Clear();
            _info.Add("程序", Settings.AppName);
            _info.Add("版本", Settings.AppVersion);
            _info.Add("用途", Settings.AppTagline);
            _info.Add("运行环境", ".NET Framework " + Environment.Version + "   ·   " + (Environment.Is64BitProcess ? "64 位" : "32 位"));
            _info.Add("操作系统", Environment.OSVersion.VersionString);
            _info.AddHeader("内置数据");
            _info.Add("版本对照表", Settings.AllVersions().Count + " 条 pack_format 记录（含快照）");
            _info.Add("迁移规则", MigrationRules.All.Length + " 条路径规则 + 语言文件规范化与键名迁移");
            _info.Add("数据来源", "Little100/Minecraft-Resourcespack-Editor 的 version_map.json（GPL-3.0）");
            _info.AddHeader("许可");
            _info.Add("许可协议", "GPL-3.0");
            _info.Add("原项目", "https://github.com/Little100/Minecraft-Resourcespack-Editor");
        }

        public void OnEnter() { }
        public void OnLeave() { }

        private void RunSelfTest()
        {
            _runBtn.Enabled = false;
            _log.Clear();
            Ui.AppendLog(_log, "正在运行自测，请稍候…");
            Status("正在运行内置自测…");
            Application.DoEvents();

            string outFile = Path.Combine(Path.GetTempPath(), "mcpack_selftest_ui_" + Guid.NewGuid().ToString("N") + ".txt");
            int code = -1;
            string text = null;

            var worker = new Thread(() =>
            {
                try
                {
                    code = SelfTest.Run(outFile);
                    if (File.Exists(outFile)) text = File.ReadAllText(outFile);
                }
                catch (Exception e)
                {
                    text = "自测异常：" + e.Message;
                }
            });
            worker.IsBackground = true;
            worker.Start();

            while (worker.IsAlive)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }

            _log.Clear();
            if (!string.IsNullOrEmpty(text)) Ui.AppendLog(_log, text);
            else Ui.AppendLog(_log, "自测没有产生输出。");

            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }

            _runBtn.Enabled = true;
            Status(code == 0 ? "自测通过：全部断言成功" : "自测存在失败项，详见下方输出");

            if (code != 0)
            {
                MessageBox.Show(_view, "自测存在失败项，请查看下方输出。", "自测结果",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
