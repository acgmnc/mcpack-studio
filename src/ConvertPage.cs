using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    /// <summary>
    /// 版本转换 · 资源包转换
    /// 把原来的单页界面拆成「源资源包 / 目标与输出 / 转换预览」三段式。
    /// </summary>
    internal sealed class ConvertPage : IModulePage
    {
        public string Title { get { return "资源包转换"; } }
        public string Subtitle { get { return "选源包 → 选目标版本 → 预览每一处改动 → 转换"; } }

        /* ---------------- 状态 ---------------- */
        private PackAnalysis _analysis;
        private ConvertPlan _plan;
        private readonly List<string> _excluded = new List<string>();
        private bool _busy;
        private Control _view;

        /* ---------------- 控件 ---------------- */
        private TextBox _srcBox;
        private FlatBtn _srcZipBtn, _srcFolderBtn;
        private Label _srcInfo;
        private FlatSelect _targetCombo;
        private Label _targetInfo;
        private ToolCheck _migrateAssets, _migrateLang;
        private ToolRadio _outFolder, _outZip;
        private TextBox _outBox;
        private FlatBtn _outBrowse;
        private FlatBtn _analyzeBtn, _previewBtn, _convertBtn;
        private ToolList _fileList;
        private RichTextBox _logBox;
        private FancyProgress _progress;
        private ReportGrid _reportGrid;
        private PageTabs _tabs;
        private Label _summaryLabel;

        private sealed class ComboItem
        {
            public int Format;
            public string Text;
            public override string ToString() { return Text; }
        }

        /// <summary>页面自己产生的状态文案，交给外壳显示在状态栏</summary>
        public event EventHandler<string> StatusChanged;

        private void Status(string text) { var h = StatusChanged; if (h != null) h(this, text); }

        /* ============================================================ */
        /* 页面构建                                                      */
        /* ============================================================ */

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
            host.Content.Controls.Add(stack);
            host.Content.Resize += (s, e) => ResizeStack(stack, host);

            stack.Controls.Add(BuildSourceCard());
            stack.Controls.Add(BuildTargetCard());
            stack.Controls.Add(BuildPreviewCard());

            _view = host;
            ResizeStack(stack, host);
            return host;
        }

        /// <summary>卡片宽度要等加入容器后才知道，这里统一按宿主宽度铺开</summary>
        private static void ResizeStack(FlowLayoutPanel stack, DarkScrollHost host)
        {
            int w = host.Content.ClientSize.Width - host.Padding.Horizontal - 4;
            if (w < 560) w = 560;
            foreach (Control c in stack.Controls)
            {
                c.Width = w;
            }
        }

        /* ---------------- 卡片 1：源资源包 ---------------- */

        private Control BuildSourceCard()
        {
            var card = new Card { Title = "源资源包", Hint = "支持 .zip 或包含 pack.mcmeta 的文件夹", Height = 168, Margin = new Padding(0, 0, 0, 12) };

            // 顶部一行：路径输入框 + 两个选择按钮
            var row = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
            _srcZipBtn = new FlatBtn("选择 ZIP…", BtnKind.Outline, 100) { Dock = DockStyle.Right };
            _srcFolderBtn = new FlatBtn("选择文件夹…", BtnKind.Outline, 112) { Dock = DockStyle.Right };
            _srcBox = Ui.ReadOnlyBox("");
            _srcBox.Dock = DockStyle.Fill;
            row.Controls.Add(_srcBox);
            row.Controls.Add(_srcZipBtn);
            row.Controls.Add(_srcFolderBtn);
            _srcZipBtn.Click += (s, e) => PickSource(true);
            _srcFolderBtn.Click += (s, e) => PickSource(false);

            // 中间：分析结果
            _srcInfo = new Label
            {
                Text = "还没有选择资源包",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Theme.FgDim,
                Font = Theme.F(9f),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };

            // 底部：提示 + 分析按钮
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.Transparent };
            _analyzeBtn = new FlatBtn("① 分析资源包", BtnKind.Primary, 130) { Dock = DockStyle.Right };
            _analyzeBtn.Click += (s, e) => DoAnalyze();
            var tip = Ui.Caption("也可以把资源包文件夹或 ZIP 直接拖进窗口");
            tip.Dock = DockStyle.Left;
            tip.AutoSize = false;
            tip.Width = 300;
            tip.TextAlign = ContentAlignment.MiddleLeft;
            bottom.Controls.Add(_analyzeBtn);
            bottom.Controls.Add(tip);

            card.Body.Controls.Add(_srcInfo);
            card.Body.Controls.Add(row);
            card.Body.Controls.Add(bottom);
            return card;
        }

        /* ---------------- 卡片 2：目标与输出 ---------------- */

        private Control BuildTargetCard()
        {
            var card = new Card { Title = "目标版本与输出", Hint = "迁移规则会随源版本与目标版本自动决定", Height = 232, Margin = new Padding(0, 0, 0, 12) };

            /* --- 目标版本 ---
               下拉框固定宽度，右边留出来显示当前选中项的解释；
               之前让它 Dock=Fill 撑满整行，右边一大截空白看着像没排好。 */
            var rowTarget = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
            _targetCombo = new FlatSelect { Dock = DockStyle.Left, Width = 360 };
            Ui.StyleSelect(_targetCombo);
            _targetCombo.SelectedIndexChanged += (s, e) => OnTargetChanged();

            _targetInfo = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.Info,
                Font = Theme.F(8.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                BackColor = Color.Transparent,
            };

            rowTarget.Controls.Add(_targetInfo);
            rowTarget.Controls.Add(_targetCombo);
            var lblTarget = MakeFieldLabel("目标版本");

            /* --- 迁移选项 --- */
            var rowOpt = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            _migrateAssets = new ToolCheck("迁移资源路径（1.13 扁平化等）", true, 240) { Dock = DockStyle.Left, Padding = new Padding(0, 3, 0, 0) };
            _migrateLang = new ToolCheck("改写语言键名", true, 132) { Dock = DockStyle.Left, Padding = new Padding(0, 3, 0, 0) };
            rowOpt.Controls.Add(_migrateLang);
            rowOpt.Controls.Add(_migrateAssets);

            /* --- 输出 --- */
            var rowOut = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
            _outBrowse = new FlatBtn("浏览…", BtnKind.Outline, 76) { Dock = DockStyle.Right };
            _outBrowse.Click += (s, e) => PickOutput();
            _outZip = new ToolRadio("ZIP", false) { Dock = DockStyle.Right, Width = 58, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) };
            _outFolder = new ToolRadio("文件夹", true) { Dock = DockStyle.Right, Width = 72, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) };
            _outFolder.CheckedChanged += (s, e) => { if (_outFolder.Checked) UpdateOutputHint(); };
            _outZip.CheckedChanged += (s, e) => { if (_outZip.Checked) UpdateOutputHint(); };
            _outBox = Ui.ReadOnlyBox("");
            _outBox.Font = Theme.M(8.5f);
            _outBox.Dock = DockStyle.Fill;
            rowOut.Controls.Add(_outBox);
            rowOut.Controls.Add(_outBrowse);
            rowOut.Controls.Add(_outZip);
            rowOut.Controls.Add(_outFolder);
            var lblOut = MakeFieldLabel("输出位置");

            // Dock.Top 是「后加的排在上面」，所以按视觉从上到下的逆序添加
            card.Body.Controls.Add(rowOut);
            card.Body.Controls.Add(lblOut);
            card.Body.Controls.Add(rowOpt);
            card.Body.Controls.Add(rowTarget);
            card.Body.Controls.Add(lblTarget);

            FillTargetsDefault();

            return card;
        }

        private static Label MakeFieldLabel(string text)
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

        /* ---------------- 卡片 3：预览与报告 ---------------- */

        private Control BuildPreviewCard()
        {
            var card = new Card { Title = "转换预览", Hint = "转换前请核对每一处改动，可取消勾选排除文件", Height = 468, Margin = new Padding(0, 0, 0, 12) };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _previewBtn = new FlatBtn("② 生成转换预览", BtnKind.Normal, 132) { Margin = new Padding(0, 0, 8, 0) };
            _convertBtn = new FlatBtn("③ 开始转换", BtnKind.Primary, 118) { Margin = new Padding(0, 0, 8, 0) };
            _previewBtn.Enabled = false;
            _convertBtn.Enabled = false;
            _previewBtn.Click += (s, e) => DoPreview();
            _convertBtn.Click += (s, e) => DoConvert();

            _progress = new FancyProgress { Height = 16, Width = 200, Margin = new Padding(14, 8, 0, 0) };

            _summaryLabel = new Label
            {
                AutoSize = true,
                ForeColor = Theme.FgDim,
                Font = Theme.F(8.5f),
                Margin = new Padding(14, 10, 0, 0),
                BackColor = Color.Transparent,
            };

            actions.Controls.Add(_previewBtn);
            actions.Controls.Add(_convertBtn);
            actions.Controls.Add(_progress);
            actions.Controls.Add(_summaryLabel);

            /* 用自绘的 PageTabs 而不是原生 TabControl —— 原生页眉那片
               系统浅灰压不住，在深色界面上就是一条白杠。 */
            _tabs = new PageTabs { Dock = DockStyle.Fill };

            // --- 改动明细
            var previewPage = new Panel { BackColor = Theme.BgPanel };
            var split = PageKit.MakeSplit(Orientation.Horizontal, 0.62, 90, 70);
            _fileList = new ToolList
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.None,
                Font = Theme.F(8.5f),
                CheckBoxes = true,
                HideSelection = false,
            };
            _fileList.SetColumns("", 28, "动作", 66, "源路径", 320, "→ 输出路径", 320, "说明", -1);
            _fileList.ItemChecked += (s, e) => OnFileChecked(e.Item);

            _logBox = Ui.LogBox();
            split.Panel1.Controls.Add(_fileList);
            split.Panel2.Controls.Add(_logBox);
            split.Dock = DockStyle.Fill;

            previewPage.Controls.Add(split);

            // --- 报告
            var reportPage = new Panel { BackColor = Theme.BgPanel };
            _reportGrid = new ReportGrid();
            reportPage.Controls.Add(_reportGrid);

            _tabs.AddPage("改动明细", previewPage);
            _tabs.AddPage("转换报告", reportPage);

            // Dock.Fill 需要最后添加才会填充剩余空间
            card.Body.Controls.Add(_tabs);
            card.Body.Controls.Add(actions);

            return card;
        }

        /* ============================================================ */
        /* 生命周期                                                      */
        /* ============================================================ */

        public void OnEnter() { }

        public void OnLeave() { }

        /// <summary>外部（拖放 / 启动参数）预填源资源包</summary>
        public void PreloadSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path) && !File.Exists(path)) return;
            SetSource(path);
            DoAnalyze();
        }

        /* ============================================================ */
        /* 交互                                                          */
        /* ============================================================ */

        private void PickSource(bool zip)
        {
            if (zip)
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择资源包 ZIP";
                    dlg.Filter = "Minecraft 资源包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    SetSource(dlg.FileName);
                }
            }
            else
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择资源包文件夹（应包含 pack.mcmeta）";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    SetSource(dlg.SelectedPath);
                }
            }
        }

        private void SetSource(string path)
        {
            _analysis = null;
            _plan = null;
            _excluded.Clear();
            _srcBox.Text = path;
            _srcInfo.Text = "已选择，点「① 分析资源包」读取它的版本信息";
            _srcInfo.ForeColor = Theme.FgDim;
            _fileList.Items.Clear();
            _logBox.Clear();
            _analyzeBtn.Enabled = true;
            _previewBtn.Enabled = false;
            _convertBtn.Enabled = false;
            _progress.Value = 0;
            _summaryLabel.Text = "";
            Status("已选择：" + path);
            SuggestOutput();
        }

        private string SourceBaseName()
        {
            string src = _srcBox.Text;
            if (string.IsNullOrEmpty(src)) return null;
            try
            {
                if (Directory.Exists(src)) return new DirectoryInfo(src.TrimEnd('\\', '/')).Name;
                return Path.GetFileNameWithoutExtension(src);
            }
            catch { return null; }
        }

        private void SuggestOutput()
        {
            string src = _srcBox.Text;
            if (string.IsNullOrEmpty(src)) return;
            try
            {
                string name = SourceBaseName();
                string dir = Path.GetDirectoryName(src.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(dir) || name == null) return;

                bool isZip = File.Exists(src);
                _outZip.Checked = isZip;
                _outFolder.Checked = !isZip;
                _outBox.Text = isZip
                    ? Path.Combine(dir, name + "_converted.zip")
                    : Path.Combine(dir, name + "_converted");
            }
            catch { }
        }

        private void UpdateOutputHint()
        {
            string src = _srcBox.Text;
            if (string.IsNullOrEmpty(src)) return;
            try
            {
                string name = SourceBaseName();
                string dir = Path.GetDirectoryName(src.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(dir) || name == null) return;
                _outBox.Text = _outZip.Checked
                    ? Path.Combine(dir, name + "_converted.zip")
                    : Path.Combine(dir, name + "_converted");
            }
            catch { }
        }

        private void PickOutput()
        {
            if (_outZip.Checked)
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "选择输出的 ZIP 路径";
                    dlg.Filter = "ZIP 文件 (*.zip)|*.zip";
                    dlg.FileName = _outBox.Text;
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    _outBox.Text = dlg.FileName;
                }
            }
            else
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择输出文件夹（不存在会自动创建，已存在会被替换）";
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    _outBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void SetBusy(bool busy, string what = null)
        {
            _busy = busy;
            _srcZipBtn.Enabled = !busy;
            _srcFolderBtn.Enabled = !busy;
            _analyzeBtn.Enabled = !busy && !string.IsNullOrEmpty(_srcBox.Text);
            _previewBtn.Enabled = !busy && _analysis != null && _analysis.Ok;
            _convertBtn.Enabled = !busy && _plan != null;
            _outBrowse.Enabled = !busy;
            _targetCombo.Enabled = !busy;
            Cursor.Current = busy ? Cursors.WaitCursor : Cursors.Default;
            if (busy && what != null) Status(what + "…");
            Application.DoEvents();
        }

        private void OnTargetChanged()
        {
            var sel = _targetCombo.SelectedItem as ComboItem;
            if (sel == null) { _targetInfo.Text = ""; return; }

            _targetInfo.Text = "pack_format " + sel.Format + "  ·  " + Settings.DescribeStage(sel.Format);
            if (_analysis != null && _analysis.Ok)
            {
                int cur = _analysis.Format.EffectiveFormat ?? -1;
                string arrow = sel.Format == cur
                    ? "   （与源相同，只会重写版本声明）"
                    : "   →   " + sel.Text.Replace("pack_format " + sel.Format + "   ", "").Replace("   ← 当前", "");
                _srcInfo.Text = _analysis.Format.DescribeCurrent() + arrow;
                _srcInfo.ForeColor = Theme.Info;
                _previewBtn.Enabled = !_busy;
            }
        }

        /* ============================================================ */
        /* ① 分析                                                        */
        /* ============================================================ */

        private void DoAnalyze()
        {
            string src = _srcBox.Text;
            if (string.IsNullOrEmpty(src)) return;

            SetBusy(true, "正在分析资源包");
            _logBox.Clear();
            _fileList.Items.Clear();
            _plan = null;
            _excluded.Clear();

            try
            {
                if (_analysis != null) _analysis.Cleanup();
                _analysis = PackAnalyzer.Analyze(src);
            }
            catch (Exception e)
            {
                SetBusy(false);
                _srcInfo.Text = "✗ 分析失败：" + e.Message;
                _srcInfo.ForeColor = Theme.Danger;
                Status("分析失败：" + e.Message);
                return;
            }

            SetBusy(false);

            if (!_analysis.Ok)
            {
                _srcInfo.Text = "✗ " + _analysis.Error;
                _srcInfo.ForeColor = Theme.Danger;
                Status("分析失败：" + _analysis.Error);
                Ui.AppendLog(_logBox, "分析失败：" + _analysis.Error);
                _analyzeBtn.Enabled = true;
                return;
            }

            var f = _analysis.Format;
            _srcInfo.Text = "✓ " + f.DescribeCurrent() + "   ·   " + _analysis.Counts.Total + " 个文件   ·   " +
                Ui.FormatBytes(_analysis.Counts.TotalBytes) + "   ·   命名空间 " + _analysis.Namespaces.Count + " 个";
            _srcInfo.ForeColor = Theme.Ok;

            var log = _logBox;
            Ui.AppendLog(log, "=== 资源包分析 ===");
            Ui.AppendLog(log, "路径        : " + _analysis.SourcePath);
            Ui.AppendLog(log, "类型        : " + (_analysis.IsZip ? "ZIP（已解压到临时目录）" : "文件夹"));
            if (_analysis.IsZip && !string.IsNullOrEmpty(_analysis.ZipInnerPrefix))
                Ui.AppendLog(log, "内侧前缀    : " + _analysis.ZipInnerPrefix + "（已自动剥离）");
            Ui.AppendLog(log, "pack.mcmeta : " + (_analysis.PackMcmetaAtRoot ? "位于根目录 ✓" : "缺失 ✗"));
            Ui.AppendLog(log, "当前版本    : " + f.DescribeCurrent());
            if (!string.IsNullOrEmpty(f.RawPackFormat)) Ui.AppendLog(log, "原始写法    : pack_format = " + f.RawPackFormat);
            if (!string.IsNullOrEmpty(f.ParseError)) Ui.AppendLog(log, "解析问题    : " + f.ParseError);
            Ui.AppendLog(log, "描述        : " + (string.IsNullOrEmpty(f.Description) ? "(空)" : f.Description.Replace("\n", "\\n")));
            Ui.AppendLog(log, "");
            Ui.AppendLog(log, "文件构成：");
            Ui.AppendLog(log, "  纹理        " + _analysis.Counts.Textures);
            Ui.AppendLog(log, "  模型        " + _analysis.Counts.Models);
            Ui.AppendLog(log, "  方块状态    " + _analysis.Counts.BlockStates);
            Ui.AppendLog(log, "  物品模型    " + _analysis.Counts.ItemModels);
            Ui.AppendLog(log, "  语言        " + _analysis.Counts.Lang);
            Ui.AppendLog(log, "  音效        " + _analysis.Counts.Sounds);
            Ui.AppendLog(log, "  其它 JSON   " + _analysis.Counts.Json);
            Ui.AppendLog(log, "  其它        " + _analysis.Counts.Other);
            Ui.AppendLog(log, "命名空间    : " + (_analysis.Namespaces.Count > 0 ? string.Join(", ", _analysis.Namespaces.ToArray()) : "(无 assets 目录)"));
            foreach (string w in _analysis.Warnings) Ui.AppendLog(log, "提示        : " + w);

            FillTargets();
            OnTargetChanged();
            _previewBtn.Enabled = true;
            Status("分析完成 —— 选择目标版本后点「② 生成转换预览」");
        }

        private void FillTargets()
        {
            _targetCombo.Items.Clear();

            var list = Settings.DisplayVersions().OrderByDescending(v => v.Format).ToList();
            int currentFormat = _analysis != null && _analysis.Format != null && _analysis.Format.EffectiveFormat.HasValue
                ? _analysis.Format.EffectiveFormat.Value
                : -1;

            int selectIndex = 0;
            if (list.Count > 1 && currentFormat == list[0].Format) selectIndex = 1;

            foreach (var v in list)
            {
                string label = Settings.Describe(v);
                _targetCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + label + (v.Format == currentFormat ? "   ← 当前" : ""),
                });
            }
            if (_targetCombo.Items.Count > 0)
            {
                if (selectIndex >= _targetCombo.Items.Count) selectIndex = 0;
                _targetCombo.SelectedIndex = selectIndex;
            }
        }

        /// <summary>
        /// 构造时先把版本列表填上：还没选资源包时下拉框就是空的，
        /// 看着像坏掉了，而且用户想先选版本也没得选。
        /// </summary>
        private void FillTargetsDefault()
        {
            if (_targetCombo.Items.Count > 0) return;
            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format))
            {
                _targetCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            }
            if (_targetCombo.Items.Count > 0) _targetCombo.SelectedIndex = 0;
            OnTargetChanged();
        }

        /* ============================================================ */
        /* ② 预览                                                        */
        /* ============================================================ */

        private ConvertOptions CurrentOptions()
        {
            var sel = _targetCombo.SelectedItem as ComboItem;
            return new ConvertOptions
            {
                Mode = _outZip.Checked ? OutputMode.Zip : OutputMode.Folder,
                OutputPath = _outBox.Text,
                TargetFormat = sel != null ? sel.Format : 0,
                TargetVersionLabel = sel != null ? sel.Text.Replace("pack_format " + sel.Format + "   ", "").Replace("   ← 当前", "") : "",
                MigrateAssets = _migrateAssets.Checked,
                MigrateLangContent = _migrateLang.Checked,
                Excluded = new HashSet<string>(_excluded, StringComparer.OrdinalIgnoreCase),
            };
        }

        private void DoPreview()
        {
            if (_analysis == null || !_analysis.Ok) return;
            var sel = _targetCombo.SelectedItem as ComboItem;
            if (sel == null) { Status("请先选择目标版本"); return; }

            SetBusy(true, "正在生成预览");
            try
            {
                _plan = PlanBuilder.Build(_analysis, CurrentOptions());
                RenderPlan(_plan, _analysis.Format.EffectiveFormat ?? 0, sel.Format);
                _tabs.SelectedIndex = 0;
            }
            catch (Exception e)
            {
                Status("生成预览失败：" + e.Message);
                Ui.AppendLog(_logBox, "生成预览失败：" + e.Message);
            }
            finally
            {
                SetBusy(false);
            }

            _convertBtn.Enabled = _plan != null;
            if (_plan != null) Status("预览就绪 —— " + _plan.Describe() + "，确认后点「③ 开始转换」");
        }

        private void RenderPlan(ConvertPlan plan, int fromFormat, int toFormat)
        {
            _fileList.BeginUpdate();
            _fileList.Items.Clear();
            _logBox.Clear();

            Ui.AppendLog(_logBox, "=== 转换计划 ===");
            Ui.AppendLog(_logBox, "源版本      : pack_format " + fromFormat + "  ·  " + Settings.DescribeStage(fromFormat));
            Ui.AppendLog(_logBox, "目标版本    : pack_format " + toFormat + "  ·  " + Settings.DescribeStage(toFormat));
            Ui.AppendLog(_logBox, "输出        : " + (_outZip.Checked ? "[ZIP] " : "[文件夹] ") + _outBox.Text);
            Ui.AppendLog(_logBox, "");
            foreach (string n in plan.Notes) Ui.AppendLog(_logBox, "· " + n);

            if (plan.RuleIds.Count > 0)
            {
                Ui.AppendLog(_logBox, "");
                Ui.AppendLog(_logBox, "触发的迁移规则：");
                foreach (string id in plan.RuleIds)
                {
                    var rule = MigrationRules.All.FirstOrDefault(r => r.Id == id);
                    if (rule != null) Ui.AppendLog(_logBox, "  · " + rule.Title + " —— " + rule.Detail);
                    else if (id == "flatten-lang-file") Ui.AppendLog(_logBox, "  · 语言文件规范化（地区码小写、.lang → .json）");
                    else if (id == "flatten-lang-keys") Ui.AppendLog(_logBox, "  · 语言键名迁移（block.x → block.minecraft.x）");
                    else if (id == "pack-mcmeta") Ui.AppendLog(_logBox, "  · 重写 pack.mcmeta 的版本声明");
                }
            }

            Ui.AppendLog(_logBox, "");
            Ui.AppendLog(_logBox, "=== 逐文件明细 ===");

            int rename = 0, rewrite = 0, exclude = 0, keep = 0;
            foreach (var a in plan.Actions)
            {
                string kindText;
                Color color;
                switch (a.Kind)
                {
                    case PlanActionKind.Rename: kindText = "重命名"; color = Theme.ActRename; rename++; break;
                    case PlanActionKind.Rewrite: kindText = "改写"; color = Theme.ActRewrite; rewrite++; break;
                    case PlanActionKind.Exclude: kindText = "排除"; color = Theme.ActExclude; exclude++; break;
                    default: kindText = "复制"; color = Theme.ActCopy; keep++; break;
                }

                var item = new ListViewItem(new[]
                {
                    "",
                    kindText,
                    a.From,
                    (a.Kind == PlanActionKind.Rename || a.Kind == PlanActionKind.Rewrite) ? a.To : "",
                    string.Join("；", a.Notes.ToArray()),
                });
                item.ForeColor = color;
                item.Tag = a;
                item.Checked = a.Kind != PlanActionKind.Exclude;
                _fileList.Items.Add(item);

                if (a.Kind == PlanActionKind.Rename || a.Kind == PlanActionKind.Rewrite)
                {
                    Ui.AppendLog(_logBox, "[" + kindText + "] " + a.From);
                    if (a.Kind == PlanActionKind.Rename) Ui.AppendLog(_logBox, "        → " + a.To);
                    foreach (string n in a.Notes) Ui.AppendLog(_logBox, "        · " + n);
                }
            }

            _fileList.EndUpdate();

            _summaryLabel.Text = "共 " + plan.TotalFiles + " 个文件，重命名 " + rename + "，改写 " + rewrite + "，复制 " + keep + (exclude > 0 ? "，排除 " + exclude : "");
        }

        private void OnFileChecked(ListViewItem item)
        {
            var a = item.Tag as FileAction;
            if (a == null || _plan == null) return;

            if (item.Checked)
            {
                _excluded.Remove(a.From);
                a.Kind = a.NewContent != null
                    ? PlanActionKind.Rewrite
                    : (string.Equals(a.To, a.From, StringComparison.Ordinal) ? PlanActionKind.Keep : PlanActionKind.Rename);
            }
            else
            {
                if (!_excluded.Contains(a.From)) _excluded.Add(a.From);
                a.Kind = PlanActionKind.Exclude;
            }
            if (_plan.Options != null)
                _plan.Options.Excluded = new HashSet<string>(_excluded, StringComparer.OrdinalIgnoreCase);
        }

        /* ============================================================ */
        /* ③ 转换                                                        */
        /* ============================================================ */

        private void DoConvert()
        {
            if (_plan == null) return;
            if (string.IsNullOrWhiteSpace(_outBox.Text)) { Status("请先指定输出位置"); return; }

            var opt = CurrentOptions();
            _plan = PlanBuilder.Build(_analysis, opt);
            RenderPlan(_plan, _analysis.Format.EffectiveFormat ?? 0, opt.TargetFormat);

            string outPath = opt.OutputPath;
            if (Directory.Exists(outPath) && opt.Mode == OutputMode.Folder)
            {
                var ans = MessageBox.Show(_view,
                    "输出文件夹已存在：\n" + outPath + "\n\n它会被转换结果替换（原有内容先备份，失败会自动还原）。继续吗？",
                    "确认覆盖", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (ans != DialogResult.OK) return;
            }
            if (File.Exists(outPath) && opt.Mode == OutputMode.Zip)
            {
                var ans = MessageBox.Show(_view,
                    "输出 ZIP 已存在：\n" + outPath + "\n\n它会被覆盖。继续吗？",
                    "确认覆盖", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (ans != DialogResult.OK) return;
            }

            SetBusy(true, "正在转换");
            _progress.Maximum = Math.Max(1, _plan.Actions.Count);
            _progress.Value = 0;

            ConvertReport report;
            try
            {
                report = ConvertExecutor.Execute(_plan, (cur, total, file) =>
                {
                    if (cur % 20 == 0 || cur == total)
                    {
                        _progress.Value = Math.Min(_progress.Maximum, cur);
                        Status("正在转换 " + cur + " / " + total + "   " + file);
                        Application.DoEvents();
                    }
                });
            }
            catch (Exception e)
            {
                SetBusy(false);
                MessageBox.Show(_view, "转换过程中出现未预期的错误：\n" + e.Message, "转换失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Status("转换失败：" + e.Message);
                return;
            }

            SetBusy(false);
            _progress.Value = _progress.Maximum;
            RenderReport(report);
            _tabs.SelectedIndex = 1;

            if (report.Success)
            {
                Status("✓ " + report.Describe());
                var open = MessageBox.Show(_view,
                    report.Describe() + "\n\n输出位置：\n" + report.OutputPath + "\n\n要打开输出所在位置吗？",
                    "转换完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes) OpenInExplorer(report.OutputPath);
            }
            else
            {
                Status("✗ 转换失败：" + report.Error);
                MessageBox.Show(_view, "转换失败：\n" + report.Error, "转换失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void OpenInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        private void RenderReport(ConvertReport r)
        {
            _reportGrid.Rows.Clear();

            _reportGrid.Add("结果", r.Success ? "成功" : "失败：" + r.Error);
            if (r.Success)
            {
                _reportGrid.Add("输出位置", r.OutputPath);
                _reportGrid.Add("输出大小", Ui.FormatBytes(r.OutputBytes));
            }
            _reportGrid.Add("文件总数", r.TotalFiles.ToString());
            _reportGrid.Add("已写出", r.Written.ToString());
            if (r.Renamed > 0) _reportGrid.Add("重命名", r.Renamed.ToString());
            if (r.Rewritten > 0) _reportGrid.Add("内容改写", r.Rewritten.ToString());
            if (r.Excluded > 0) _reportGrid.Add("已排除", r.Excluded.ToString());
            _reportGrid.Add("耗时", r.ElapsedMs + " 毫秒");
            if (r.RulesApplied.Count > 0) _reportGrid.Add("应用的规则", string.Join("、", r.RulesApplied.ToArray()));

            if (r.Notes.Count > 0)
            {
                _reportGrid.AddHeader("说明");
                foreach (string n in r.Notes) _reportGrid.Add("", n);
            }

            _reportGrid.AddHeader("pack.mcmeta 变化");
            _reportGrid.AddMono("转换前", r.PackMcmetaBefore ?? "(无)");
            _reportGrid.AddMono("转换后", r.PackMcmetaAfter ?? "(无)");

            if (r.Problems.Count > 0)
            {
                _reportGrid.AddHeader("注意");
                foreach (string p in r.Problems) _reportGrid.Add("", p);
            }
        }
    }
}
