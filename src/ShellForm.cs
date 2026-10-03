using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace McPackStudio
{
    /// <summary>
    /// 应用外壳。结构：
    ///   标题栏
    ///   顶部导航（主模块 + 主页）
    ///   子模块条（只在模块内显示）
    ///   内容区
    ///   状态栏
    /// 没有侧边栏 —— 模块切换靠顶部导航，子模块切换靠顶部子模块条。
    /// </summary>
    internal sealed class ShellForm : Form
    {
        private List<MainModule> _modules;

        private TopNav _topNav;
        private FlowLayoutPanel _stack;
        private Panel _subStrip;
        private Panel _contentHost;
        private Label _statusText;
        private Label _statusRight;
        private Panel _statusBar;

        private readonly Dictionary<string, Control> _views = new Dictionary<string, Control>();
        private readonly HomePage _home = new HomePage();
        private readonly PackLandingPage _packLanding = new PackLandingPage();

        private string _currentModuleId;
        private SubModule _currentSub;
        private bool _packLandingReady;

        public ShellForm()
        {
            BuildUi();
            _modules = UiRegistry.Build();
            _topNav.SetModules(_modules);

            /* ---- 主页：只列主模块 ---- */
            var homeView = _home.CreateView(_modules);
            _home.ModuleRequested += (s, moduleId) => EnterModule(moduleId);
            _home.StatusChanged += (s, text) => Status(text);
            _views["__home__"] = homeView;

            /* ---- 资源包落地页 ---- */
            _packLanding.SubRequested += (s, subId) => NavigateTo("pack", subId);
            _packLanding.ImportRequested += (s, isZip) => StartImport(isZip);
            _packLanding.StatusChanged += (s, text) => Status(text);

            /* ---- 顶部导航 ---- */
            _topNav.ModuleClicked += (s, moduleId) =>
            {
                if (moduleId == null) ShowHome();
                else EnterModule(moduleId);
            };
            // 已经在某个模块里时再点一次 → 回到它的落地页
            _topNav.SubClicked += (s, moduleId) =>
            {
                if (moduleId == "pack") ShowPackLanding();
                else EnterModule(moduleId);
            };

            // 优先回到上次停留的子模块，否则停在主页
            string savedModule, savedSub;
            AppSettings.LoadSelection(out savedModule, out savedSub);

            if (!string.IsNullOrEmpty(savedModule) && _modules.Any(m => m.Id == savedModule))
            {
                if (string.IsNullOrEmpty(savedSub)) EnterModule(savedModule);
                else NavigateTo(savedModule, savedSub);
            }
            else
            {
                ShowHome();
            }
        }

        /* ============================================================ */
        /* 构建                                                          */
        /* ============================================================ */

        private void BuildUi()
        {
            Text = Settings.AppName + "  ·  " + Settings.AppTagline;
            ClientSize = new Size(1240, 820);
            MinimumSize = new Size(1000, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.BgBase;
            ForeColor = Theme.Fg;
            Font = Theme.F(9f);
            DoubleBuffered = true;
            AllowDrop = true;

            /* 让系统把标题栏、滚动条等原生元素也按深色绘制，
               否则深色界面上会露出白色滚动条和浅色标题栏 */
            DarkMode.Apply(this);

            /* 任何控件被加进来时都补一次深色主题。
               滚动条是延迟创建的子窗口，不主动套用就会保持系统浅色。 */
            ControlAdded += (s, e) =>
            {
                var c = e.Control;
                if (c == null || !c.IsHandleCreated) return;
                DarkMode.ApplyThemeToTree(c.Handle, 0);
            };

            DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (s, e) =>
            {
                var files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                OpenSourceFile(files[0]);
            };

            /*
             * 顶部三块（标题栏 / 顶部导航 / 子模块条）放进一个自上而下的流式容器。
             * 用 FlowLayoutPanel 而不是 TableLayoutPanel —— 后者会按内容撑高行，
             * 导致整体高度不受控。
             *
             * 注意：这里刻意不给这些区块设 Dock，全部由 LayoutRegions() 手动定位。
             * 之前用 Dock=Fill 的内容区时，WinForms 按控件 z 序反向停靠，
             * 内容区会抢在 Dock=Top 的外壳之前占满整个窗口，
             * 结果标题栏和顶部导航被压在下面看不见、主页卡片也被裁掉一半。
             * 手动算坐标以后跟 z 序彻底无关，怎么都不会再错位。
             */
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false,
                AutoScroll = false,
                BackColor = Theme.BgVoid,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Height = TitleBarH + TopNavH + SubStripH,
            };
            // FlowLayoutPanel 不会撑开子控件的宽度，自己同步
            stack.SizeChanged += (s, e) => SyncStackWidths();

            var titleBar = BuildTitleBar();
            titleBar.Height = TitleBarH;
            titleBar.Margin = new Padding(0);
            stack.Controls.Add(titleBar);

            _topNav = new TopNav();
            _topNav.Height = TopNavH;
            _topNav.Margin = new Padding(0);
            stack.Controls.Add(_topNav);

            _subStrip = new Panel { BackColor = Theme.BgVoid, Height = SubStripH, Margin = new Padding(0) };
            _subStrip.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line, 1))
                    e.Graphics.DrawLine(pen, 0, _subStrip.Height - 1, _subStrip.Width, _subStrip.Height - 1);
            };
            stack.Controls.Add(_subStrip);

            _stack = stack;
            Controls.Add(stack);

            /* 内容区 */
            _contentHost = new Panel { BackColor = Theme.BgBase };
            _contentHost.Resize += (s, e) => LayoutCurrentPage();
            Controls.Add(_contentHost);

            /* 状态栏 */
            _statusBar = (Panel)BuildStatusBar();
            Controls.Add(_statusBar);

            LayoutRegions();
        }

        /* ============================================================ */
        /* 区域排版：顶部外壳 / 内容区 / 状态栏                            */
        /* ============================================================ */

        private const int TitleBarH = 46;      // 品牌条：窗口按钮已交给系统标题栏，不需要 54
        private const int TopNavH = 50;
        private const int SubStripH = 46;      // 固定高度，切换模块时不再抖动
        private const int StatusBarH = 28;

        /// <summary>
        /// 三块区域的坐标全部在这里算死：外壳贴顶、状态栏贴底、内容区吃中间剩余部分。
        /// 不依赖 Dock，也不依赖控件 z 序，所以不会出现互相遮挡。
        /// </summary>
        private void LayoutRegions()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            int header = TitleBarH + TopNavH + (_subStrip != null ? _subStrip.Height : 0);
            if (_stack != null)
            {
                _stack.Bounds = new Rectangle(0, 0, w, header);
                SyncStackWidths();
            }

            if (_statusBar != null)
                _statusBar.Bounds = new Rectangle(0, h - StatusBarH, w, StatusBarH);

            if (_contentHost != null)
            {
                int top = header;
                int bottom = h - StatusBarH;
                _contentHost.Bounds = new Rectangle(0, top, w, Math.Max(0, bottom - top));
            }

            /* Bounds 改了但 Dock 属性还留着，强制重排一次让子控件跟上，
               这样 _contentHost.ClientSize 立刻就是最终值。 */
            PerformLayout();

            /* 页面尺寸一变，原生滚动条就可能出现/消失。它出现之后会按系统
               浅色画一遍，必须等这次消息处理完再补套一次深色主题，
               否则就会留下一条白杠。限制次数，避免每帧都递归枚举子窗口。 */
            if (_darkRefreshes < 12)
            {
                _darkRefreshes++;
                DarkMode.ApplyDeferred(this, 3);
            }
        }

        private int _darkRefreshes;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRegions();
        }

        /// <summary>
        /// 品牌条。窗口按钮交给系统标题栏，这里只放图标和名称，
        /// 所以不需要自绘的 − □ ✕，也不需要拖动/P-Invoke（那些都会和系统栏重复）。
        /// </summary>
        private Control BuildTitleBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.BgVoid };
            bar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line, 1))
                    e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(18, 9, 28, 28);
                using (var b = new LinearGradientBrush(r, Theme.Accent, Theme.Accent2, 45f))
                using (var path = Card.Rounded(r, 7))
                    g.FillPath(b, path);
                using (var f = Theme.F(12f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("M", f, b, r, sf);
                }
            };

            bar.Controls.Add(new Label
            {
                Text = Settings.AppName,
                Font = Theme.F(12.5f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                Location = new Point(56, 7),
                BackColor = Color.Transparent,
            });
            bar.Controls.Add(new Label
            {
                Text = Settings.AppTagline + "   ·   v" + Settings.AppVersion,
                Font = Theme.F(8.5f),
                ForeColor = Theme.FgFaint,
                AutoSize = true,
                Location = new Point(58, 27),
                BackColor = Color.Transparent,
            });

            return bar;
        }

        private Control BuildStatusBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.BgVoid };
            bar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Line, 1))
                    e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
            };

            _statusText = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(8.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 0, 0),
                Text = "就绪",
                BackColor = Color.Transparent,
            };
            bar.Controls.Add(_statusText);

            _statusRight = new Label
            {
                Dock = DockStyle.Right,
                Width = 320,
                ForeColor = Theme.FgFaint,
                Font = Theme.F(8.5f),
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 18, 0),
                BackColor = Color.Transparent,
            };
            bar.Controls.Add(_statusRight);

            return bar;
        }

        /* ============================================================ */
        /* 页面切换                                                      */
        /* ============================================================ */

        private void LayoutCurrentPage()
        {
            LayoutRegions();                                    // 先保证内容区尺寸是最新的
            if (_currentSub != null) return;                    // 子模块页自己用 Dock 排版
            if (_topNav != null && _topNav.HomeSelected) _home.Layout(_contentHost.ClientSize);
            else if (_packLandingReady) _packLanding.Layout(_contentHost.ClientSize);
        }

        private void ShowHome()
        {
            if (_currentSub != null && _currentSub.Page != null) _currentSub.Page.OnLeave();
            _currentSub = null;
            _currentModuleId = null;

            _topNav.SelectHome();
            BuildSubStrip(null, null, "主页");
            ShowView("__home__");
            _home.Layout(_contentHost.ClientSize);

            Status("主页 —— 选一个模块进入");
            UpdateStatusRight();
            AppSettings.SaveSelection("", "");
        }

        /// <summary>进入某个模块；没有指定子模块时进它的落地页</summary>
        private void EnterModule(string moduleId)
        {
            var mod = _modules.FirstOrDefault(m => m.Id == moduleId);
            if (mod == null || mod.Subs.Count == 0) return;

            _topNav.SelectModule(moduleId);
            _currentModuleId = moduleId;

            // 资源包子模块多，有专门落地页；其它模块直接进第一个子模块
            if (moduleId == "pack")
            {
                ShowPackLanding();
                return;
            }
            NavigateTo(moduleId, mod.Subs[0].Id);
        }

        private void NavigateTo(string moduleId, string subId)
        {
            var mod = _modules.FirstOrDefault(m => m.Id == moduleId);
            if (mod == null) return;
            var sub = mod.Subs.FirstOrDefault(s => s.Id == subId);
            if (sub == null) return;

            if (_currentSub != null && _currentSub.Page != null && _currentSub != sub)
                _currentSub.Page.OnLeave();

            _currentSub = sub;
            _currentModuleId = moduleId;
            _topNav.SelectModule(moduleId);

            BuildSubStrip(mod, sub, sub.Title);

            string key = moduleId + "/" + subId;
            Control view;
            if (!_views.TryGetValue(key, out view))
            {
                view = sub.Page.CreateView();
                view.Dock = DockStyle.Fill;
                _views[key] = view;
            }
            ShowView(key);

            sub.Page.OnEnter();
            Status("位置：" + mod.Title + " · " + sub.Title);
            UpdateStatusRight();
            AppSettings.SaveSelection(moduleId, subId);
        }

        /// <summary>「资源包」模块的落地页：列出它下面的全部子模块</summary>
        private void ShowPackLanding()
        {
            if (_currentSub != null && _currentSub.Page != null) _currentSub.Page.OnLeave();
            _currentSub = null;
            _currentModuleId = "pack";

            if (!_packLandingReady)
            {
                var v = _packLanding.CreateView();
                v.Dock = DockStyle.Fill;
                _views["__packhome__"] = v;
                _packLandingReady = true;
            }

            _topNav.SelectModule("pack");
            var mod = _modules.FirstOrDefault(m => m.Id == "pack");
            BuildSubStrip(mod, null, mod != null ? mod.Title : "");

            ShowView("__packhome__");
            _packLanding.Layout(_contentHost.ClientSize);

            Status("资源包 —— 选一张卡片开始，或用上面的子模块条切换");
            UpdateStatusRight();
            AppSettings.SaveSelection("pack", "");
        }

        /// <summary>
        /// 子模块条。左侧显示当前页面名称（PageTitle），右侧是子模块按钮。
        ///
        /// 之所以把页面名放这里：内容区里的页面控件会整块重绘，
        /// 页面内部再叠一个 header 很容易被盖掉；放在这一条上永远不会被覆盖。
        /// </summary>
        private void BuildSubStrip(MainModule mod, SubModule current, string pageTitle)
        {
            _subStrip.SuspendLayout();
            _subStrip.Controls.Clear();

            const int pad = 18;
            const int gap = 6;

            /* 左侧页面名 */
            var title = new Label
            {
                Text = pageTitle ?? "",
                Font = Theme.F(10.5f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(pad, 12),
            };
            _subStrip.Controls.Add(title);

            int startX = pad + title.PreferredWidth + 20;

            if (mod == null || mod.Subs.Count == 0)
            {
                _subStrip.ResumeLayout();
                return;
            }

            int avail = Math.Max(200, _subStrip.ClientSize.Width - startX - pad);

            int x = startX;
            int y = 10;
            int rowH = 30;

            foreach (var s in mod.Subs)
            {
                int w = TextRenderer.MeasureText(s.Title, Theme.F(8.8f)).Width + 26;
                if (w > 200) w = 200;

                if (x > startX && x + w > startX + avail)
                {
                    x = startX;
                    y += rowH;
                }

                var btn = new FlatBtn(s.Title, s == current ? BtnKind.Primary : BtnKind.Ghost, w)
                {
                    Height = 26,
                    Location = new Point(x, y),
                    Font = Theme.F(8.8f),
                };
                var captured = s;
                btn.Click += (a, b) => NavigateTo(mod.Id, captured.Id);
                new ToolTip().SetToolTip(btn, s.Hint);
                _subStrip.Controls.Add(btn);

                x += w + gap;
            }

            /* 子模块条高度固定；如果一行放不下就换行，放不下的行会被裁掉，
               但高度不会跟着变 —— 高度一变，下面的内容区就得跟着重排，
               切换模块时整页会跳一下。 */
            _subStrip.ResumeLayout();
        }

        /// <summary>FlowLayoutPanel 不会把子控件横向撑满，这里手动同步</summary>
        private void SyncStackWidths()
        {
            if (_stack == null) return;
            int w = _stack.ClientSize.Width - _stack.Padding.Horizontal;
            if (w < 0) w = 0;
            foreach (Control c in _stack.Controls)
            {
                if (c.Width != w) c.Width = w;
            }
        }

        private void ShowView(string key)
        {
            Control view;
            if (!_views.TryGetValue(key, out view)) return;

            _contentHost.SuspendLayout();
            _contentHost.Controls.Clear();
            _contentHost.Controls.Add(view);
            _contentHost.ResumeLayout();
        }

        private void UpdateStatusRight()
        {
            int subCount = 0;
            if (_modules != null) foreach (var m in _modules) subCount += m.Subs.Count;
            int moduleCount = _modules != null ? _modules.Count : 0;
            _statusRight.Text = moduleCount + " 个模块 / " + subCount + " 个子模块   ·   .NET " + Environment.Version;
        }

        /// <summary>
        /// 打开界面并直接进 NBT 编辑器载入指定文件。
        /// 命令行长这样：MCPackStudio.exe --nbt "level.dat"
        /// </summary>
        public void OpenNbtFile(string path)
        {
            NavigateTo("nbt", "nbt.edit");

            var page = FindNbtPage();
            if (page == null) return;
            page.OpenFile(path);
        }

        private NbtPage FindNbtPage()
        {
            if (_modules == null) return null;
            foreach (var m in _modules)
                foreach (var s in m.Subs)
                {
                    var page = s.Page as NbtPage;
                    if (page != null) return page;
                }
            return null;
        }

        public void Status(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Status), text); return; }
            if (_statusText != null) _statusText.Text = text;
        }

        /* ============================================================ */
        /* 外部入口                                                      */
        /* ============================================================ */

        /// <summary>打开导入对话框，成功后切到转换页并预填</summary>
        private void StartImport(bool isZip)
        {
            string path = null;
            if (isZip)
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "导入资源包 ZIP";
                    dlg.Filter = "Minecraft 资源包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.FileName;
                }
            }
            else
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择资源包文件夹（应包含 pack.mcmeta）";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.SelectedPath;
                }
            }
            if (string.IsNullOrEmpty(path)) return;

            NavigateTo("pack", "pack.convert");

            var page = _currentSub != null ? _currentSub.Page as ConvertPage : null;
            if (page != null) page.PreloadSource(path);
        }

        /// <summary>启动参数 / 拖放：打开这个资源包并切到转换页</summary>
        public void OpenSourceFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path) && !File.Exists(path)) return;

            NavigateTo("pack", "pack.convert");

            var page = _currentSub != null ? _currentSub.Page as ConvertPage : null;
            if (page != null) page.PreloadSource(path);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            /* 区域坐标由 LayoutRegions() 负责，这里只需要在窗口真正显示后
               再算一遍，确保用的是最终的客户区尺寸。 */
            LayoutRegions();
            LayoutCurrentPage();

            UpdateStatusRight();
            MaybeDumpLayout();

            if (Program.LayoutDumpFile != null && _dumpTimer == null)
            {
                // 窗口尺寸变化后延迟重写一次，方便核对各种窗口大小下的排版
                _dumpTimer = new Timer { Interval = 400 };
                _dumpTimer.Tick += (s, a) =>
                {
                    _dumpTimer.Stop();
                    LayoutRegions();
                    LayoutCurrentPage();
                    MaybeDumpLayout();
                };
            }
        }

        private Timer _dumpTimer;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_dumpTimer != null && !_dumpTimer.Enabled) _dumpTimer.Start();
        }

        /// <summary>
        /// 调试用：把客户区依次设成几个常见尺寸，每个尺寸写一份布局快照。
        /// --shot-sizes &lt;前缀&gt; 触发，跑完直接退出。
        /// </summary>
        public void RunSizeProbe(string prefix)
        {
            var sizes = new Size[]
            {
                new Size(1240, 820),   // 默认
                new Size(1000, 660),   // 最小
                new Size(1560, 880),   // 放大
                new Size(1180, 700),   // 宽扁
            };

            var sb = new System.Text.StringBuilder();
            foreach (var s in sizes)
            {
                SetClientSizeCore(s.Width, s.Height);
                LayoutRegions();
                LayoutCurrentPage();
                PerformLayout();
                Application.DoEvents();

                sb.AppendLine("==== client " + ClientSize.Width + "x" + ClientSize.Height + " ====");
                Dump(this, this, sb, 0);
                sb.AppendLine();
            }

            try { File.WriteAllText(prefix, sb.ToString(), new System.Text.UTF8Encoding(false)); }
            catch { }
        }

        /// <summary>
        /// 调试用：依次给界面套用几个候选的深色主题名，各截一张图。
        /// 用来实测哪个主题名真能把系统滚动条压成深色。
        /// --theme-test &lt;目录&gt; 触发。
        /// </summary>
        public void RunThemeTest(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }

            /* 页面上有原生滚动条（多行 TextBox 的竖向滚动条）才有意义 */
            NavigateTo("settings", "settings.about");
            for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(40); }

            var variants = new[]
            {
                "edit=space",      // 跳过 EDIT，套空主题
                "edit=theme",      // 跳过 EDIT，套工作主题
                "edit=skip",       // 连 EDIT 的子窗口一起跳过，保持系统默认
                "theme=DarkMode_Explorer,edit=space",
            };

            foreach (string v in variants)
            {
                DarkMode.EditMode = v;
                DarkMode.ApplyThemeToTree(Handle, 0);
                LayoutRegions();
                LayoutCurrentPage();
                Invalidate(true);
                for (int i = 0; i < 16; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }

                CaptureTo(Path.Combine(dir, v.Replace("=", "-").Replace(",", "_") + ".png"));
            }

            DarkMode.EditMode = "edit=space";
        }

        /// <summary>切到截图用的那个「有滚动条」的页面，不重复截整轮</summary>
        private void RunShotSequenceNavOnly()
        {
            NavigateTo("settings", "settings.about");
            for (int i = 0; i < 12; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(40);
            }
        }

        /// <summary>
        /// 调试用：把 FlatSelect 单独放到一个新窗口里，用来确认展开列表能不能正常工作
        /// （不受主界面坐标换算干扰）。--select-test &lt;目录&gt;
        /// </summary>
        public void RunSelectTest(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }

            var f = new Form
            {
                Text = "FlatSelect 单测",
                ClientSize = new Size(520, 260),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Theme.BgBase,
            };

            var info = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ForeColor = Theme.FgMuted,
                Font = Theme.F(9f),
                Text = "初始状态",
                Padding = new Padding(10, 0, 0, 0),
            };

            var sel = new FlatSelect { Left = 30, Top = 40, Width = 360 };
            sel.DropDownWidth = 520;
            sel.SelectedIndexChanged += (s, e) =>
            {
                var it = sel.SelectedItem as ComboItem;
                info.Text = "已选中 index=" + sel.SelectedIndex + "  format=" + (it == null ? -1 : it.Format);
            };

            foreach (var v in Settings.DisplayVersions().OrderByDescending(x => x.Format).Take(12))
            {
                sel.Items.Add(new ComboItem
                {
                    Format = v.Format,
                    Text = "pack_format " + v.Format + "   " + Settings.Describe(v),
                });
            }
            if (sel.Items.Count > 0) sel.SelectedIndex = 0;

            f.Controls.Add(info);
            f.Controls.Add(sel);
            f.Show();

            var steps = new List<KeyValuePair<string, Action>>();
            steps.Add(new KeyValuePair<string, Action>("01-initial", () => { }));
            steps.Add(new KeyValuePair<string, Action>("02-dropdown-open", () => sel.OpenDropDownForTest()));
            steps.Add(new KeyValuePair<string, Action>("03-select-4th", () => sel.SetIndexForTest(3)));
            steps.Add(new KeyValuePair<string, Action>("04-closed", () => sel.CloseDropDownForTest()));

            foreach (var step in steps)
            {
                Application.DoEvents();
                step.Value();
                for (int i = 0; i < 14; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(40);
                }
                CaptureWindow(f, Path.Combine(dir, step.Key + ".png"));
            }

            f.Close();
        }

        /// <summary>截一个窗口（可以用来看自绘控件的实际渲染）</summary>
        private static void CaptureWindow(Form f, string file)
        {
            try
            {
                var r = new Rectangle(f.Left, f.Top, f.Width, f.Height);
                using (var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height)))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 调试用：依次切到每个页面并截图，用来一次性核对全部排版。
        /// --shots &lt;目录&gt; 触发。
        /// </summary>
        public void RunShotSequence(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }

            var steps = new List<KeyValuePair<string, Action>>();
            steps.Add(new KeyValuePair<string, Action>("01-home", ShowHome));
            steps.Add(new KeyValuePair<string, Action>("02-pack", ShowPackLanding));
            steps.Add(new KeyValuePair<string, Action>("03-pack-convert", () => NavigateTo("pack", "pack.convert")));
            steps.Add(new KeyValuePair<string, Action>("04-pack-batch", () => NavigateTo("pack", "pack.batch")));
            steps.Add(new KeyValuePair<string, Action>("05-pack-merge", () => NavigateTo("pack", "pack.merge")));
            steps.Add(new KeyValuePair<string, Action>("06-tools-version", () => NavigateTo("tools", "tools.version")));
            steps.Add(new KeyValuePair<string, Action>("07-tools-icon", () => NavigateTo("tools", "tools.icon")));
            /* NBT 编辑器截图前先塞一个样本文件，空树看不出排版对不对 */
            steps.Add(new KeyValuePair<string, Action>("08-nbt-edit", () =>
            {
                NavigateTo("nbt", "nbt.edit");
                string sample = FindSampleNbt();
                if (sample != null) OpenNbtFile(sample);
            }));
            steps.Add(new KeyValuePair<string, Action>("09-nbt-snbt", () => NavigateTo("nbt", "nbt.snbt")));
            steps.Add(new KeyValuePair<string, Action>("10-settings-rules", () => NavigateTo("settings", "settings.rules")));
            steps.Add(new KeyValuePair<string, Action>("11-settings-about", () => NavigateTo("settings", "settings.about")));

            /* 需要先打开资源包才有内容的页面。
               用仓库里的 testpack-1.12，看的是真实数据下的排版。 */
            string pack = FindDemoPack();
            if (!string.IsNullOrEmpty(pack))
            {
                steps.Add(new KeyValuePair<string, Action>("10-browse", () =>
                {
                    NavigateTo("pack", "pack.browse");
                    ApplyDemoPack(pack);
                }));
                steps.Add(new KeyValuePair<string, Action>("11-meta", () =>
                {
                    NavigateTo("pack", "pack.meta");
                    ApplyDemoPack(pack);
                }));
                steps.Add(new KeyValuePair<string, Action>("12-rename", () =>
                {
                    NavigateTo("pack", "pack.rename");
                    ApplyDemoPack(pack);
                }));
                steps.Add(new KeyValuePair<string, Action>("13-new", () => NavigateTo("pack", "pack.new")));
            }

            foreach (var step in steps)
            {
                string name = step.Key;
                Application.DoEvents();
                step.Value();
                for (int i = 0; i < 12; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(40);
                }
                CaptureTo(Path.Combine(dir, name + ".png"));
            }
        }

        /// <summary>找仓库里自带的 NBT 样本（截图与手工验证都用它）</summary>
        private static string FindSampleNbt()
        {
            try
            {
                string here = AppDomain.CurrentDomain.BaseDirectory;
                /* exe 在 release 下，样本在仓库根的 testnbt 里 */
                string[] candidates =
                {
                    Path.Combine(here, "..", "testnbt", "sample_java_level.dat"),
                    Path.Combine(here, "testnbt", "sample_java_level.dat"),
                    Path.Combine(Directory.GetCurrentDirectory(), "testnbt", "sample_java_level.dat"),
                };
                foreach (string c in candidates)
                {
                    string full = Path.GetFullPath(c);
                    if (File.Exists(full)) return full;
                }
            }
            catch { }
            return null;
        }

        /// <summary>把示例资源包喂给当前页面（各页面自己认领）</summary>
        private void ApplyDemoPack(string path)
        {
            var cur = _currentSub;
            if (cur == null || cur.Page == null) return;

            var conv = cur.Page as ConvertPage;
            if (conv != null) { conv.PreloadSource(path); return; }

            var browse = cur.Page as PackBrowsePage;
            if (browse != null) { browse.PreloadSource(path); return; }

            var meta = cur.Page as PackMetaPage;
            if (meta != null) { meta.PreloadSource(path); return; }

            var rename = cur.Page as PackRenamePage;
            if (rename != null) { rename.PreloadSource(path); return; }
        }

        /// <summary>找一个示例资源包：优先仓库里的 testpack-1.12</summary>
        private static string FindDemoPack()
        {
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(root); i++)
                {
                    string p = Path.Combine(root, "testpack-1.12");
                    if (Directory.Exists(p) && File.Exists(Path.Combine(p, "pack.mcmeta"))) return p;
                    root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
                }
            }
            catch { }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

        /// <summary>PW_RENDERFULLCONTENT：让 DWM 把整窗内容（含自绘控件）渲染到位图</summary>
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        /// <summary>
        /// 把窗口当前画面存成 PNG。
        ///
        /// 先用 PrintWindow 让窗口自己渲染一遍：它不挑前台焦点，别的窗口挡住也照样截得到，
        /// 这正是「抓屏幕」做不到的（抓屏幕必须先 SetForegroundWindow，
        /// 一旦被系统拒绝焦点，截出来的就是压在它上面的别的窗口）。
        /// PrintWindow 失败（个别系统 / 远程桌面下会返回 false）再退回抓屏幕。
        /// </summary>
        private void CaptureTo(string file)
        {
            try
            {
                int w = Math.Max(1, Width);
                int h = Math.Max(1, Height);

                /* 滚动条是延迟创建的系统子窗口，深色主题也是延迟补套的。
                   截图前再强制套一次并等它画完，否则截到的是还没上色的那一帧。 */
                DarkMode.ApplyThemeToTree(Handle, 0);
                for (int i = 0; i < 10; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(30);
                }

                using (var bmp = new Bitmap(w, h))
                {
                    bool captured = false;
                    try
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            IntPtr hdc = g.GetHdc();
                            try { captured = PrintWindow(Handle, hdc, PW_RENDERFULLCONTENT); }
                            finally { g.ReleaseHdc(hdc); }
                        }
                    }
                    catch { captured = false; }

                    if (!captured)
                    {
                        SetForegroundWindow(Handle);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(260);
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(Left, Top, 0, 0, new Size(w, h));
                    }

                    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        /// <summary>
        /// 调试用：--layout &lt;文件&gt; 启动时把控件实际布局写成文本，便于排查排版问题。
        /// </summary>
        private void MaybeDumpLayout()
        {
            string file = Program.LayoutDumpFile;
            if (string.IsNullOrEmpty(file)) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Form client: " + ClientSize.Width + "x" + ClientSize.Height);
                Dump(this, this, sb, 0);
                File.WriteAllText(file, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch
            {
                // 调试功能失败不影响使用
            }
        }

        private static void Dump(Control root, Control c, System.Text.StringBuilder sb, int depth)
        {
            if (depth > 4) return;
            string indent = new string(' ', depth * 2);
            string text = c.Text;
            if (!string.IsNullOrEmpty(text) && text.Length > 26) text = text.Substring(0, 26) + "…";
            text = text.Replace("\r", " ").Replace("\n", " ");

            Point origin = root.PointToScreen(Point.Empty);
            Point self = c.PointToScreen(Point.Empty);

            sb.AppendLine(indent + c.GetType().Name
                + "  bounds=" + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height
                + "  abs=" + (self.X - origin.X) + "," + (self.Y - origin.Y)
                + "  dock=" + c.Dock
                + (text.Length > 0 ? "  text=[" + text + "]" : ""));

            foreach (Control child in c.Controls)
            {
                Dump(root, child, sb, depth + 1);
            }
        }
    }
}
