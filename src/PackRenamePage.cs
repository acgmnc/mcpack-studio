using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 资源包 · 批量重命名                                                  */
    /* ================================================================== */

    internal sealed class PackRenamePage : IModulePage
    {
        public string Title { get { return "批量重命名"; } }
        public string Subtitle { get { return "按规则批量改名，支持正则与捕获组，执行前先逐条预览"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;
        private PackAnalysis _analysis;

        private TextBox _pathBox, _findBox, _replaceBox, _prefixBox, _suffixBox, _extBox, _dirBox, _containsBox, _numFmtBox, _log;
        private ComboBox _modeCombo, _scopeCombo;
        private NumericUpDown _numStart, _numStep;
        private ToolCheck _useRegex, _caseSensitive;
        private ListView _preview;
        private FlatBtn _planBtn, _applyBtn;
        private Label _summary;

        public void OnEnter() { }
        public void OnLeave() { if (_analysis != null) { _analysis.Cleanup(); _analysis = null; } }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildSourceCard());
            stack.Controls.Add(BuildRuleCard());
            stack.Controls.Add(BuildPreviewCard());

            _view = host;
            UpdateModeUi();
            return host;
        }

        /* ------------------------------------------------------------ */

        private Card BuildSourceCard()
        {
            var card = new Card { Title = "① 选择资源包", Hint = "文件夹或 ZIP；ZIP 会在保存时重新打包", Height = 168, Margin = new Padding(0, 0, 0, 12) };

            _pathBox = Ui.ReadOnlyBox("");
            _pathBox.Font = Theme.M(8.5f);
            FlatBtn browse, zipBtn;
            var row1 = PageKit.PathRow(_pathBox, "选文件夹…", () =>
            {
                string dir = PageKit.PickFolder(_view, "选择要改名的资源包文件夹");
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

            _summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(8.8f),
                BackColor = Color.Transparent,
            };

            card.Body.Controls.Add(_summary);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(bar);
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("资源包位置"));
            return card;
        }

        private Card BuildRuleCard()
        {
            var card = new Card { Title = "② 重命名规则", Hint = "改动任一选项后点「预览」重新计算结果", Height = 398, Margin = new Padding(0, 0, 0, 12) };

            _modeCombo = new ComboBox { Dock = DockStyle.Left, Width = 200 };
            Ui.StyleCombo(_modeCombo);
            _modeCombo.Items.Add("查找替换");
            _modeCombo.Items.Add("加前缀");
            _modeCombo.Items.Add("加后缀");
            _modeCombo.Items.Add("序号编号");
            _modeCombo.SelectedIndex = 0;
            _modeCombo.SelectedIndexChanged += (s, e) => UpdateModeUi();

            _scopeCombo = new ComboBox { Dock = DockStyle.Left, Width = 280 };
            Ui.StyleCombo(_scopeCombo);
            _scopeCombo.Items.Add("只改文件名（保留扩展名）");
            _scopeCombo.Items.Add("文件名 + 扩展名");
            _scopeCombo.Items.Add("完整相对路径（可改目录名）");
            _scopeCombo.SelectedIndex = 0;
            var rowM = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowM.Controls.Add(_scopeCombo);
            rowM.Controls.Add(_modeCombo);

            _findBox = MakeInput();
            var rowF = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowF.Controls.Add(_findBox);

            _replaceBox = MakeInput();
            var rowR = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowR.Controls.Add(_replaceBox);

            _useRegex = new ToolCheck("用正则表达式（替换里可用 $1 $2 引用捕获组）", false, 340) { Dock = DockStyle.Left };
            _caseSensitive = new ToolCheck("区分大小写", true, 120) { Dock = DockStyle.Left };
            var rowO = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowO.Controls.Add(_caseSensitive);
            rowO.Controls.Add(_useRegex);

            _prefixBox = MakeInput();
            var rowP = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowP.Controls.Add(_prefixBox);

            _suffixBox = MakeInput();
            var rowS = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowS.Controls.Add(_suffixBox);

            _numFmtBox = MakeInput();
            _numFmtBox.Text = "{0:000}";
            _numStart = MakeNum(1);
            _numStep = MakeNum(1);
            var rowN = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            rowN.Controls.Add(MakeLabelInline("起始"));
            rowN.Controls.Add(_numStart);
            rowN.Controls.Add(MakeLabelInline("步长"));
            rowN.Controls.Add(_numStep);
            rowN.Controls.Add(_numFmtBox);

            /* 筛选 */
            _extBox = MakeInput();
            _extBox.Text = "";
            _dirBox = MakeInput();
            _containsBox = MakeInput();
            var rowE = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowE.Controls.Add(_extBox);
            var rowD = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowD.Controls.Add(_dirBox);
            var rowC = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            rowC.Controls.Add(_containsBox);

            /* 按视觉逆序添加（Dock.Top 后加的在上） */
            card.Body.Controls.Add(rowC);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("文件名包含（空 = 不限）"));
            card.Body.Controls.Add(rowD);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("限定目录（相对路径，空 = 整个包）"));
            card.Body.Controls.Add(rowE);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("只处理这些扩展名（逗号分隔，如 png,json；空 = 全部）"));
            card.Body.Controls.Add(rowN);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("序号格式 / 起始 / 步长（仅序号编号）"));
            card.Body.Controls.Add(rowS);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("后缀（仅加后缀）"));
            card.Body.Controls.Add(rowP);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("前缀（仅加前缀）"));
            card.Body.Controls.Add(rowO);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(rowR);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("替换为"));
            card.Body.Controls.Add(rowF);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("查找内容"));
            card.Body.Controls.Add(rowM);
            card.Body.Controls.Add(PageKit.Spacer(4));
            card.Body.Controls.Add(PageKit.MakeLabel("模式 / 作用范围"));
            return card;
        }

        private static TextBox MakeInput()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
        }

        private static NumericUpDown MakeNum(int val)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 64,
                Minimum = -9999,
                Maximum = 9999,
                Value = val,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
            };
        }

        private static Label MakeLabelInline(string t)
        {
            return new Label
            {
                Text = t,
                Dock = DockStyle.Left,
                Width = 46,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                Padding = new Padding(6, 7, 0, 0),
                BackColor = Color.Transparent,
            };
        }

        /// <summary>按模式启用/禁用对应的输入框，避免用户对着无效字段发呆</summary>
        private void UpdateModeUi()
        {
            if (_modeCombo == null) return;
            int m = _modeCombo.SelectedIndex;

            bool find = m == 0, pre = m == 1, suf = m == 2, num = m == 3;

            _findBox.Enabled = find;
            _replaceBox.Enabled = find;
            _useRegex.Enabled = find;
            _caseSensitive.Enabled = find;
            _scopeCombo.Enabled = find;

            _prefixBox.Enabled = pre;
            _suffixBox.Enabled = suf;
            _numFmtBox.Enabled = num;
            _numStart.Enabled = num;
            _numStep.Enabled = num;

            _findBox.BackColor = find ? Theme.BgInput : Theme.BgPanel;
            _replaceBox.BackColor = find ? Theme.BgInput : Theme.BgPanel;
            _prefixBox.BackColor = pre ? Theme.BgInput : Theme.BgPanel;
            _suffixBox.BackColor = suf ? Theme.BgInput : Theme.BgPanel;
            _numFmtBox.BackColor = num ? Theme.BgInput : Theme.BgPanel;
        }

        private Card BuildPreviewCard()
        {
            var card = new Card { Title = "③ 预览与执行", Hint = "绿色是可执行的改名，红色是冲突或非法（会阻止执行）", Height = 400, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _planBtn = new FlatBtn("① 预览改动", BtnKind.Normal, 116) { Margin = new Padding(0, 0, 8, 0) };
            _applyBtn = new FlatBtn("② 执行改名", BtnKind.Primary, 116) { Margin = new Padding(0, 0, 8, 0), Enabled = false };
            var reset = new FlatBtn("清空规则", BtnKind.Ghost, 90);
            _planBtn.Click += (s, e) => DoPlan();
            _applyBtn.Click += (s, e) => DoApply();
            reset.Click += (s, e) => ResetRules();
            bar.Controls.Add(_planBtn);
            bar.Controls.Add(_applyBtn);
            bar.Controls.Add(reset);

            _preview = new ListView
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
            _preview.Columns.Add("原路径", 340);
            _preview.Columns.Add("新路径", 340);
            _preview.Columns.Add("状态", 280);

            _log = Ui.LogBox();
            _log.Height = 70;

            card.Body.Controls.Add(_preview);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(_log);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(bar);
            return card;
        }

        private void ResetRules()
        {
            _modeCombo.SelectedIndex = 0;
            _scopeCombo.SelectedIndex = 0;
            _findBox.Text = "";
            _replaceBox.Text = "";
            _prefixBox.Text = "";
            _suffixBox.Text = "";
            _numFmtBox.Text = "{0:000}";
            _numStart.Value = 1;
            _numStep.Value = 1;
            _useRegex.Checked = false;
            _caseSensitive.Checked = true;
            _extBox.Text = "";
            _dirBox.Text = "";
            _containsBox.Text = "";
            _preview.Items.Clear();
            _applyBtn.Enabled = false;
            UpdateModeUi();
            Status("规则已清空");
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
                _summary.Text = "读取失败：" + a.Error;
                _applyBtn.Enabled = false;
                PageKit.Error(_view, a.Error);
                Status("读取失败：" + a.Error);
                return;
            }

            _summary.Text = a.DisplayName + "　·　" + (a.Format != null ? a.Format.DescribeCurrent() : "版本未知")
                            + "　·　" + a.Counts.Total + " 个文件 / " + Ui.FormatBytes(a.Counts.TotalBytes)
                            + "\n" + (a.IsZip ? "来自 ZIP：改名后需要写回 ZIP（会先备份原包）"
                                              : "来自文件夹：会在同级生成一个新目录，原目录保持不动");
            _preview.Items.Clear();
            _applyBtn.Enabled = false;
            Status("已打开：" + a.DisplayName);
        }

        private RenameRule CurrentRule()
        {
            return new RenameRule
            {
                Mode = (RenameMode)_modeCombo.SelectedIndex,
                Scope = (RenameScope)_scopeCombo.SelectedIndex,
                Find = _findBox.Text,
                Replace = _replaceBox.Text,
                UseRegex = _useRegex.Checked,
                CaseSensitive = _caseSensitive.Checked,
                Prefix = _prefixBox.Text,
                Suffix = _suffixBox.Text,
                NumberFormat = _numFmtBox.Text,
                NumberStart = (int)_numStart.Value,
                NumberStep = (int)_numStep.Value == 0 ? 1 : (int)_numStep.Value,
                ExtFilter = _extBox.Text,
                DirFilter = _dirBox.Text,
                ContainsFilter = _containsBox.Text,
            };
        }

        private void DoPlan()
        {
            if (_analysis == null) { PageKit.Error(_view, "先选择一个资源包。"); return; }

            _log.Clear();
            _planBtn.Enabled = false;

            try
            {
                var plan = PackRenamer.Plan(_analysis.RootDir, CurrentRule(), t => Ui.AppendLog(_log, t));

                _preview.BeginUpdate();
                _preview.Items.Clear();
                foreach (var it in plan.Items)
                {
                    var item = new ListViewItem(it.From);
                    item.SubItems.Add(it.To);
                    item.SubItems.Add(it.Invalid ? "非法：" + it.Reason
                                     : it.Conflict ? "冲突：" + it.Reason
                                     : it.Changed ? "将改名"
                                     : "不变");
                    if (it.Invalid) item.ForeColor = Theme.Danger;
                    else if (it.Conflict) item.ForeColor = Theme.Warn;
                    else if (it.Changed) item.ForeColor = Theme.Ok;
                    else item.ForeColor = Theme.FgFaint;
                    _preview.Items.Add(item);
                }
                _preview.EndUpdate();

                Ui.AppendLog(_log, plan.Describe());
                foreach (string n in plan.Notes) Ui.AppendLog(_log, "· " + n);

                if (plan.ConflictCount > 0)
                    Ui.AppendLog(_log, "有 " + plan.ConflictCount + " 个冲突，请调整规则后再执行。");
                if (plan.InvalidCount > 0)
                    Ui.AppendLog(_log, "有 " + plan.InvalidCount + " 个非法结果，请调整规则。");

                _applyBtn.Enabled = plan.CanApply;
                Status("预览完成：" + plan.Describe());
            }
            catch (Exception e)
            {
                Ui.AppendLog(_log, "失败：" + e.Message);
                PageKit.Error(_view, "预览失败：" + e.Message);
                Status("预览失败：" + e.Message);
            }
            finally
            {
                _planBtn.Enabled = true;
            }
        }

        private void DoApply()
        {
            if (_analysis == null) { PageKit.Error(_view, "先选择一个资源包。"); return; }

            var rule = CurrentRule();
            var plan = PackRenamer.Plan(_analysis.RootDir, rule, null);

            if (!plan.CanApply)
            {
                PageKit.Error(_view, "当前计划不能执行：\n\n" + plan.Describe());
                return;
            }

            string target;
            bool zipSource = _analysis.IsZip;
            if (zipSource)
            {
                target = _analysis.SourcePath;
                if (!PageKit.Confirm(_view,
                    "即将把 " + plan.ChangedCount + " 个文件改名并重新打包回：\n\n" + target +
                    "\n\n原 ZIP 会先备份，成功后删除备份。继续吗？")) return;
            }
            else
            {
                string parent = Path.GetDirectoryName(_analysis.RootDir.TrimEnd('\\', '/'));
                string name = new DirectoryInfo(_analysis.RootDir).Name;
                target = Path.Combine(parent, name + "_renamed");
                if (!PageKit.Confirm(_view,
                    "即将把 " + plan.ChangedCount + " 个文件改名，输出到：\n\n" + target +
                    "\n\n原目录不会被动。继续吗？")) return;
            }

            _applyBtn.Enabled = false;
            if (_view != null) _view.UseWaitCursor = true;

            try
            {
                /*
                 * 改名引擎只会产出「文件夹」。
                 * 非 ZIP 源：直接输出到 <原名>_renamed。
                 * ZIP 源：先输出到临时目录，再打包成 ZIP 原子替换原包。
                 */
                string tempOut = Path.Combine(Path.GetTempPath(),
                    "mcpack_rename_" + Guid.NewGuid().ToString("N").Substring(0, 12));

                string stageTarget = zipSource ? tempOut : target;

                var report = PackRenamer.Execute(plan, stageTarget, (i, total, p) =>
                {
                    if (i % 40 == 0 || i == total) Ui.AppendLog(_log, "[" + i + "/" + total + "] " + p);
                });

                if (report.Ok && zipSource)
                {
                    Ui.AppendLog(_log, "改名完成，正在打包回 ZIP…");
                    var zipReport = StagedWriter.Write(target, StageOutput.Zip, staging =>
                    {
                        StagedWriter.CopyInto(report.OutputPath, staging, null);
                    }, n => Ui.AppendLog(_log, "· " + n));

                    ZipIo.DeleteDirectory(report.OutputPath);

                    if (!zipReport.Ok)
                    {
                        Ui.AppendLog(_log, "打包失败：" + zipReport.Error);
                        PageKit.Error(_view, "改名成功但打包失败：\n\n" + zipReport.Error);
                        Status("打包失败：" + zipReport.Error);
                        return;
                    }
                    report.OutputPath = zipReport.OutputPath;
                }

                Ui.AppendLog(_log, "");
                foreach (string n in report.Notes) Ui.AppendLog(_log, "· " + n);

                if (report.Ok)
                {
                    Ui.AppendLog(_log, "完成：" + report.Describe());
                    Ui.AppendLog(_log, "输出：" + report.OutputPath);
                    Status("改名完成：" + report.Describe());
                    MessageBox.Show(_view,
                        "改名完成。\n\n" + report.Describe() + "\n\n" + report.OutputPath,
                        "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Ui.AppendLog(_log, "失败：" + report.Error);
                    Status("改名失败：" + report.Error);
                    PageKit.Error(_view, "改名失败：\n\n" + report.Error);
                }
            }
            catch (Exception e)
            {
                Ui.AppendLog(_log, "异常：" + e.Message);
                PageKit.Error(_view, e.Message);
                Status("改名失败：" + e.Message);
            }
            finally
            {
                _applyBtn.Enabled = true;
                if (_view != null) _view.UseWaitCursor = false;
            }
        }
    }
}
