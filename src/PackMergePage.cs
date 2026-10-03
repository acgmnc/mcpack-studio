using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 资源包 · 合并资源包                                                  */
    /* ================================================================== */

    internal sealed class PackMergePage : IModulePage
    {
        public string Title { get { return "合并资源包"; } }
        public string Subtitle { get { return "把多个资源包按优先级融合成一个，冲突文件可逐个决定"; } }

        public event EventHandler<string> StatusChanged;
        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        private Control _view;

        private ListView _sourceList;
        private ListView _conflictList;
        private ComboBox _strategy;
        private ComboBox _formatCombo;
        private ComboBox _modeCombo;
        private TextBox _descBox;
        private TextBox _outBox;
        private TextBox _log;
        private ProgressBar _progress;
        private Label _summary;
        private FlatBtn _planBtn, _runBtn;

        private readonly List<MergeSource> _sources = new List<MergeSource>();
        private MergePlan _plan;

        /// <summary>冲突的当前选择：relPath → 来源下标（-1 = 排除）</summary>
        private readonly Dictionary<string, int> _choices =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public void OnEnter() { }
        public void OnLeave() { }

        public Control CreateView()
        {
            if (_view != null) return _view;

            var host = PageKit.MakeHost();
            var stack = PageKit.MakeStack(host);

            stack.Controls.Add(BuildSourceCard());
            stack.Controls.Add(BuildOutputCard());
            stack.Controls.Add(BuildConflictCard());
            stack.Controls.Add(BuildActionCard());

            _view = host;
            return host;
        }

        /* ------------------------------------------------------------ */
        /* 来源                                                                */
        /* ------------------------------------------------------------ */

        private Card BuildSourceCard()
        {
            var card = new Card
            {
                Title = "① 来源资源包",
                Hint = "从上到下的顺序就是优先级：冲突时上面（编号小）的胜出",
                Height = 268,
                Margin = new Padding(0, 0, 0, 12),
            };

            _sourceList = new ListView
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
            _sourceList.Columns.Add("优先", 48);
            _sourceList.Columns.Add("来源", 320);
            _sourceList.Columns.Add("类型", 64);
            _sourceList.Columns.Add("版本", 220);
            _sourceList.Columns.Add("文件", 60);
            _sourceList.Columns.Add("路径", 420);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            var addFolder = new FlatBtn("＋ 添加文件夹", BtnKind.Outline, 116) { Margin = new Padding(0, 0, 8, 0) };
            var addZip = new FlatBtn("＋ 添加 ZIP", BtnKind.Outline, 100) { Margin = new Padding(0, 0, 8, 0) };
            var up = new FlatBtn("↑ 上移", BtnKind.Normal, 72) { Margin = new Padding(0, 0, 6, 0) };
            var down = new FlatBtn("↓ 下移", BtnKind.Normal, 72) { Margin = new Padding(0, 0, 6, 0) };
            var del = new FlatBtn("移除", BtnKind.Danger, 64) { Margin = new Padding(0, 0, 6, 0) };
            var clr = new FlatBtn("清空", BtnKind.Ghost, 60);
            bar.Controls.Add(addFolder);
            bar.Controls.Add(addZip);
            bar.Controls.Add(up);
            bar.Controls.Add(down);
            bar.Controls.Add(del);
            bar.Controls.Add(clr);

            addFolder.Click += (s, e) => AddSources(true);
            addZip.Click += (s, e) => AddSources(false);
            up.Click += (s, e) => MoveSource(-1);
            down.Click += (s, e) => MoveSource(+1);
            del.Click += (s, e) => RemoveSource();
            clr.Click += (s, e) =>
            {
                if (_sources.Count == 0) return;
                if (!PageKit.Confirm(_view, "确定要清空全部 " + _sources.Count + " 个来源吗？")) return;
                _sources.Clear();
                _plan = null;
                RefreshSources();
                Status("已清空来源");
            };

            card.Body.Controls.Add(_sourceList);
            card.Body.Controls.Add(PageKit.Spacer(8));
            card.Body.Controls.Add(bar);
            return card;
        }

        private void AddSources(bool folder)
        {
            if (folder)
            {
                string dir = PageKit.PickFolder(_view, "选择资源包文件夹");
                if (dir == null) return;
                AddOne(dir);
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择资源包 ZIP（可多选）";
                    dlg.Filter = "Minecraft 资源包 (*.zip)|*.zip";
                    dlg.Multiselect = true;
                    if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                    foreach (string f in dlg.FileNames) AddOne(f);
                }
            }
            RefreshSources();
        }

        private void AddOne(string path)
        {
            if (_sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                Status("已经在列表里了：" + path);
                return;
            }
            _sources.Add(new MergeSource { Path = path, Label = MakeLabelFor(path) });
            _plan = null;   // 来源变了，旧计划作废
            Status("已添加：" + MakeLabelFor(path));
        }

        private static string MakeLabelFor(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    return new DirectoryInfo(path.TrimEnd('\\', '/')).Name;
                return Path.GetFileNameWithoutExtension(path);
            }
            catch { return path; }
        }

        private void MoveSource(int delta)
        {
            if (_sourceList.SelectedIndices.Count == 0) return;
            int i = _sourceList.SelectedIndices[0];
            int j = i + delta;
            if (j < 0 || j >= _sources.Count) return;

            var tmp = _sources[i];
            _sources[i] = _sources[j];
            _sources[j] = tmp;

            RefreshSources();
            _sourceList.Items[j].Selected = true;
            _sourceList.Items[j].Focused = true;
            Status("已调整优先级");
        }

        private void RemoveSource()
        {
            if (_sourceList.SelectedIndices.Count == 0) return;
            int i = _sourceList.SelectedIndices[0];
            string label = _sources[i].Label;
            _sources.RemoveAt(i);
            _plan = null;
            RefreshSources();
            Status("已移除：" + label);
        }

        private void RefreshSources()
        {
            _sourceList.BeginUpdate();
            _sourceList.Items.Clear();
            for (int i = 0; i < _sources.Count; i++)
            {
                var s = _sources[i];
                string type = "—";
                try { type = Directory.Exists(s.Path) ? "文件夹" : "ZIP"; } catch { }

                string ver = "";
                string files = "";
                if (s.Analysis != null && s.Analysis.Ok)
                {
                    ver = s.Analysis.Format != null ? s.Analysis.Format.DescribeCurrent() : "";
                    files = s.Analysis.Counts.Total.ToString();
                }

                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(s.Enabled ? s.Label : s.Label + "（已跳过）");
                item.SubItems.Add(type);
                item.SubItems.Add(ver);
                item.SubItems.Add(files);
                item.SubItems.Add(s.Path);
                if (!s.Enabled) item.ForeColor = Theme.FgFaint;
                _sourceList.Items.Add(item);
            }
            _sourceList.EndUpdate();

            _runBtn.Enabled = _sources.Count >= 2;
            if (_summary != null)
                _summary.Text = _sources.Count + " 个来源" + (_sources.Count < 2 ? "（至少需要 2 个才能合并）" : "");
        }

        /* ------------------------------------------------------------ */
        /* 输出                                                                */
        /* ------------------------------------------------------------ */

        private Card BuildOutputCard()
        {
            var card = new Card { Title = "② 输出设置", Hint = "不会原地覆盖；已存在的目标会先备份、失败自动还原", Height = 228, Margin = new Padding(0, 0, 0, 12) };

            _outBox = Ui.ReadOnlyBox("");
            _outBox.Font = Theme.M(8.5f);
            FlatBtn outBtn;
            var row1 = PageKit.PathRow(_outBox, "浏览…", () =>
            {
                string dir = PageKit.PickFolder(_view, "选择输出文件夹");
                if (dir != null) _outBox.Text = dir;
            }, out outBtn);

            _modeCombo = new ComboBox { Dock = DockStyle.Left, Width = 200 };
            Ui.StyleCombo(_modeCombo);
            _modeCombo.Items.Add("输出为文件夹");
            _modeCombo.Items.Add("输出为 ZIP");
            _modeCombo.SelectedIndex = 0;
            var row2 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row2.Controls.Add(_modeCombo);

            _formatCombo = new ComboBox { Dock = DockStyle.Left, Width = 380 };
            Ui.StyleCombo(_formatCombo);
            _formatCombo.Items.Add("（沿用来源的 pack_format）");
            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format))
                _formatCombo.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            _formatCombo.SelectedIndex = 0;
            var row3 = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            row3.Controls.Add(_formatCombo);

            _descBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
                Text = "",
            };
            var row4 = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            row4.Controls.Add(_descBox);

            card.Body.Controls.Add(row4);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("输出描述（留空则沿用来源的描述）"));
            card.Body.Controls.Add(row3);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("目标版本（pack_format）"));
            card.Body.Controls.Add(row2);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("输出形式"));
            card.Body.Controls.Add(row1);
            card.Body.Controls.Add(PageKit.Spacer(6));
            card.Body.Controls.Add(PageKit.MakeLabel("输出路径"));
            return card;
        }

        /* ------------------------------------------------------------ */
        /* 冲突                                                                */
        /* ------------------------------------------------------------ */

        private Card BuildConflictCard()
        {
            var card = new Card
            {
                Title = "③ 同名冲突",
                Hint = "内容完全相同的同名文件会自动合并，只有内容不同的才需要决定",
                Height = 300,
                Margin = new Padding(0, 0, 0, 12),
            };

            _conflictList = new ListView
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
            _conflictList.Columns.Add("路径", 460);
            _conflictList.Columns.Add("状态", 90);
            _conflictList.Columns.Add("保留", 200);
            _conflictList.Columns.Add("涉及来源", 380);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _strategy = new ComboBox { Width = 260, Margin = new Padding(0, 2, 8, 0) };
            Ui.StyleCombo(_strategy);
            _strategy.Items.Add("按优先级（默认）");
            _strategy.Items.Add("全部保留最高优先级");
            _strategy.Items.Add("全部保留最低优先级");
            _strategy.Items.Add("全部排除这些文件");
            _strategy.SelectedIndex = 0;
            _strategy.SelectedIndexChanged += (s, e) => ApplyBulkStrategy();

            var perItem = new FlatBtn("为选中项指定来源…", BtnKind.Outline, 150) { Margin = new Padding(0, 0, 8, 0) };
            perItem.Click += (s, e) => ChooseForSelected();

            bar.Controls.Add(_strategy);
            bar.Controls.Add(perItem);

            card.Body.Controls.Add(_conflictList);
            card.Body.Controls.Add(PageKit.Spacer(8));
            card.Body.Controls.Add(bar);
            return card;
        }

        private void ApplyBulkStrategy()
        {
            if (_plan == null) { Status("请先点「预览合并」扫描来源"); return; }

            int n = _strategy.SelectedIndex;
            foreach (var c in _plan.Conflicts)
            {
                var d = _plan.Decisions.FirstOrDefault(x => string.Equals(x.RelPath, c.RelPath, StringComparison.OrdinalIgnoreCase));
                if (d == null || d.OwnerIndexes.Count == 0) continue;

                if (n == 0)
                {
                    _choices.Remove(c.RelPath);                       // 回到默认优先级
                }
                else if (n == 1)
                {
                    _choices[c.RelPath] = d.OwnerIndexes.OrderBy(i => _sources[i].Priority).First();
                }
                else if (n == 2)
                {
                    _choices[c.RelPath] = d.OwnerIndexes.OrderByDescending(i => _sources[i].Priority).First();
                }
                else if (n == 3)
                {
                    _choices[c.RelPath] = -1;
                }
            }
            PackMerger.ApplyConflictChoices(_plan, _choices);
            RefreshConflicts();
            Status("冲突策略已应用（" + _strategy.Text + "）");
        }

        private void ChooseForSelected()
        {
            if (_plan == null) { Status("请先点「预览合并」"); return; }
            if (_conflictList.SelectedIndices.Count == 0) { Status("先在上面选中一个冲突文件"); return; }

            int idx = _conflictList.SelectedIndices[0];
            var real = RealConflicts();
            if (idx < 0 || idx >= real.Count) return;

            var c = real[idx];
            var d = _plan.Decisions.FirstOrDefault(x => string.Equals(x.RelPath, c.RelPath, StringComparison.OrdinalIgnoreCase));
            if (d == null) return;

            using (var dlg = new SourcePickerForm(c, d, _sources))
            {
                if (dlg.ShowDialog(_view) != DialogResult.OK) return;
                if (dlg.ExcludeChosen) _choices[c.RelPath] = -1;
                else _choices[c.RelPath] = dlg.ChosenIndex;

                PackMerger.ApplyConflictChoices(_plan, _choices);
                RefreshConflicts();
                Status("已为 " + c.RelPath + " 指定：" + (dlg.ExcludeChosen ? "排除" : _sources[dlg.ChosenIndex].Label));
            }
        }

        private List<MergeConflict> RealConflicts()
        {
            if (_plan == null) return new List<MergeConflict>();
            return _plan.Conflicts.Where(c => !c.SameContent).ToList();
        }

        private void RefreshConflicts()
        {
            var real = RealConflicts();

            _conflictList.BeginUpdate();
            _conflictList.Items.Clear();
            foreach (var c in real)
            {
                var d = _plan.Decisions.FirstOrDefault(x => string.Equals(x.RelPath, c.RelPath, StringComparison.OrdinalIgnoreCase));
                string keep;
                string state;
                if (d != null && d.Excluded)
                {
                    keep = "（已排除）";
                    state = "排除";
                }
                else
                {
                    int wi = d != null ? d.WinnerIndex : 0;
                    if (wi < 0 || wi >= _sources.Count) wi = 0;
                    keep = _sources.Count > 0 ? _sources[wi].Label : "";
                    state = "保留";
                }

                var item = new ListViewItem(c.RelPath);
                item.SubItems.Add(state);
                item.SubItems.Add(keep);
                item.SubItems.Add(string.Join(" / ", c.Owners.ToArray()));
                if (state == "排除") item.ForeColor = Theme.Warn;
                _conflictList.Items.Add(item);
            }
            _conflictList.EndUpdate();

            int same = _plan != null ? _plan.Conflicts.Count - real.Count : 0;
            if (_summary != null && _plan != null)
            {
                _summary.Text = "同名 " + _plan.Conflicts.Count
                                + "，其中内容相同自动合并 " + same
                                + "，需要决定 " + real.Count;
            }
        }

        /* ------------------------------------------------------------ */
        /* 执行                                                                */
        /* ------------------------------------------------------------ */

        private Card BuildActionCard()
        {
            var card = new Card { Title = "④ 预览与执行", Hint = "先预览，确认无误再合并", Height = 300, Margin = new Padding(0, 0, 0, 12) };

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _planBtn = new FlatBtn("① 预览合并", BtnKind.Normal, 116) { Margin = new Padding(0, 0, 8, 0) };
            _runBtn = new FlatBtn("② 开始合并", BtnKind.Primary, 116) { Margin = new Padding(0, 0, 8, 0), Enabled = false };
            _progress = new ProgressBar { Height = 16, Width = 220, Margin = new Padding(8, 8, 0, 0) };
            _summary = new Label { AutoSize = true, ForeColor = Theme.FgDim, Font = Theme.F(8.5f), Margin = new Padding(12, 9, 0, 0), BackColor = Color.Transparent };
            bar.Controls.Add(_planBtn);
            bar.Controls.Add(_runBtn);
            bar.Controls.Add(_progress);
            bar.Controls.Add(_summary);

            _planBtn.Click += (s, e) => DoPlan();
            _runBtn.Click += (s, e) => DoRun();

            _log = Ui.LogBox();

            card.Body.Controls.Add(_log);
            card.Body.Controls.Add(PageKit.Spacer(8));
            card.Body.Controls.Add(bar);
            return card;
        }

        private void DoPlan()
        {
            if (_sources.Count < 2) { PageKit.Error(_view, "至少需要 2 个来源资源包才能合并。"); return; }

            _planBtn.Enabled = false;
            _log.Clear();
            _choices.Clear();
            _strategy.SelectedIndex = 0;
            if (_view != null) _view.UseWaitCursor = true;

            try
            {
                var plan = PackMerger.Plan(_sources, t => Ui.AppendLog(_log, t));
                _plan = plan;

                foreach (string n in plan.Notes) Ui.AppendLog(_log, "· " + n);
                Ui.AppendLog(_log, "");
                Ui.AppendLog(_log, "== 合并计划 ==");
                Ui.AppendLog(_log, plan.Describe());

                /* 来源被跳过的（损坏 / 不是资源包），提示出来 */
                var bad = _sources.Where(s => !s.Enabled).ToList();
                foreach (var b in bad)
                    Ui.AppendLog(_log, "跳过：" + b.Label + " —— "
                        + (b.Analysis != null && !string.IsNullOrEmpty(b.Analysis.Error) ? b.Analysis.Error : "不可用"));

                RefreshSources();
                RefreshConflicts();

                int usable = _sources.Count(s => s.Enabled);
                _runBtn.Enabled = usable >= 2;
                if (usable < 2) Ui.AppendLog(_log, "可用的来源不足 2 个，无法合并。");

                Status("预览完成：" + plan.Describe());
            }
            catch (Exception ex)
            {
                PageKit.Error(_view, "预览失败：" + ex.Message);
                Status("预览失败：" + ex.Message);
            }
            finally
            {
                _planBtn.Enabled = true;
                if (_view != null) _view.UseWaitCursor = false;
            }
        }

        private void DoRun()
        {
            if (_plan == null) { PageKit.Error(_view, "请先点「预览合并」。"); return; }

            string outPath = _outBox.Text;
            string problem = StagedWriter.CheckOutput(_sources[0].Path, outPath);
            if (problem != null) { PageKit.Error(_view, problem); return; }

            if (!PageKit.Confirm(_view,
                "即将合并 " + _sources.Count(s => s.Enabled) + " 个来源。\n\n"
                + _plan.Describe() + "\n\n输出到：\n" + outPath
                + "\n\n继续吗？")) return;

            _plan.Mode = _modeCombo.SelectedIndex == 1 ? StageOutput.Zip : StageOutput.Folder;
            _plan.OutputPath = outPath;
            _plan.NewDescription = string.IsNullOrWhiteSpace(_descBox.Text) ? null : _descBox.Text.Trim();

            var fmt = _formatCombo.SelectedItem as ComboItem;
            _plan.TargetFormat = fmt != null ? (int?)fmt.Format : null;

            PackMerger.ApplyConflictChoices(_plan, _choices);

            _runBtn.Enabled = false;
            _progress.Value = 0;
            if (_view != null) _view.UseWaitCursor = true;

            try
            {
                var report = PackMerger.Execute(_plan, (i, total, path) =>
                {
                    if (total <= 0) return;
                    int pct = (int)(i * 100L / total);
                    if (pct > 100) pct = 100;
                    try { _progress.Value = pct; } catch { }

                    if (i % 50 == 0 || i == total)
                        Ui.AppendLog(_log, "[" + i + "/" + total + "] " + path);
                });

                Ui.AppendLog(_log, "");
                Ui.AppendLog(_log, "== 结果 ==");
                foreach (string n in report.Notes) Ui.AppendLog(_log, "· " + n);

                if (report.Ok)
                {
                    Ui.AppendLog(_log, "成功：" + report.Describe());
                    Ui.AppendLog(_log, "输出：" + report.OutputPath);
                    Status("合并完成：" + report.Describe());
                    MessageBox.Show(_view,
                        "合并完成。\n\n" + report.Describe() + "\n\n" + report.OutputPath,
                        "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Ui.AppendLog(_log, "失败：" + report.Error);
                    Status("合并失败：" + report.Error);
                    PageKit.Error(_view, "合并失败：\n\n" + report.Error);
                }
            }
            catch (Exception ex)
            {
                Ui.AppendLog(_log, "异常：" + ex.Message);
                Status("合并失败：" + ex.Message);
                PageKit.Error(_view, ex.Message);
            }
            finally
            {
                _runBtn.Enabled = true;
                _progress.Value = 0;
                if (_view != null) _view.UseWaitCursor = false;
            }
        }
    }

    /* ================================================================== */
    /* 冲突来源选择对话框                                                   */
    /* ================================================================== */

    internal sealed class SourcePickerForm : Form
    {
        public int ChosenIndex = -1;
        public bool ExcludeChosen;

        private ListBox _list;

        public SourcePickerForm(MergeConflict c, MergeDecision d, List<MergeSource> sources)
        {
            Text = "选择保留哪一份";
            ClientSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.BgBase;
            ForeColor = Theme.Fg;
            Font = Theme.F(9f);

            var head = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(9f),
                Padding = new Padding(16, 12, 16, 0),
                Text = c.RelPath + "\r\n以下来源都包含这个文件，选择要保留的版本：",
                BackColor = Color.Transparent,
            };
            Controls.Add(head);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.Transparent };
            var ok = new FlatBtn("保留这一份", BtnKind.Primary, 110) { Dock = DockStyle.Right, Margin = new Padding(0, 0, 16, 0) };
            var excl = new FlatBtn("排除该文件", BtnKind.Danger, 100) { Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
            var cancel = new FlatBtn("取消", BtnKind.Ghost, 74) { Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
            bottom.Controls.Add(ok);
            bottom.Controls.Add(excl);
            bottom.Controls.Add(cancel);
            Controls.Add(bottom);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgInput,
                ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.F(9f),
                IntegralHeight = false,
            };
            foreach (int oi in d.OwnerIndexes.OrderBy(i => sources[i].Priority))
                _list.Items.Add("优先 " + (sources[oi].Priority + 1) + "　" + sources[oi].Label);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            Controls.Add(_list);
            _list.BringToFront();

            ok.Click += (s, e) =>
            {
                int sel = _list.SelectedIndex;
                if (sel < 0) { MessageBox.Show(this, "先选一个来源。"); return; }
                var ordered = d.OwnerIndexes.OrderBy(i => sources[i].Priority).ToList();
                ChosenIndex = ordered[sel];
                ExcludeChosen = false;
                DialogResult = DialogResult.OK;
                Close();
            };
            excl.Click += (s, e) =>
            {
                ExcludeChosen = true;
                ChosenIndex = -1;
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }
    }
}
