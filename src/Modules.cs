using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace McPackStudio
{
    /* ================================================================== */
    /* 模块契约                                                            */
    /* ================================================================== */

    /// <summary>
    /// 一个「子模块」页面。每个子模块只负责自己那块内容区域。
    /// 以后加新功能 = 新增一个 Control + 在 UiRegistry 里登记一条。
    /// </summary>
    internal interface IModulePage
    {
        /// <summary>页面标题（显示在子模块条与面包屑上）</summary>
        string Title { get; }

        /// <summary>一句话说明，显示在内容区顶部</summary>
        string Subtitle { get; }

        /// <summary>创建页面控件（只会被调用一次，之后复用）</summary>
        Control CreateView();

        /// <summary>每次切换到本页时调用，可用来刷新数据</summary>
        void OnEnter();

        /// <summary>离开本页时调用（可选清理）</summary>
        void OnLeave();
    }

    /// <summary>子模块登记项</summary>
    internal sealed class SubModule
    {
        public string Id;
        public string Title;
        public string Hint;
        public IModulePage Page;
    }

    /// <summary>主模块登记项</summary>
    internal sealed class MainModule
    {
        public string Id;
        public string Title;
        public string Icon;
        public string Hint;
        public List<SubModule> Subs = new List<SubModule>();
    }

    /// <summary>
    /// 「即将支持」占位页。让还没实现的模块也能点进去、看到规划，而不是灰掉。
    /// </summary>
    internal sealed class PlannedPage : IModulePage
    {
        private readonly string _title;
        private readonly string _subtitle;
        private readonly string[] _items;

        public PlannedPage(string title, string subtitle, string[] items)
        {
            _title = title;
            _subtitle = subtitle;
            _items = items ?? new string[0];
        }

        public string Title { get { return _title; } }
        public string Subtitle { get { return _subtitle; } }
        public void OnEnter() { }
        public void OnLeave() { }

        public Control CreateView()
        {
            var host = PageKit.MakeHost();

            var card = new Panel
            {
                Width = 560,
                Height = 240 + _items.Length * 26,
                BackColor = Theme.BgPanel,
                Location = new Point(28, 22),
            };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Line, 1))
                {
                    g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
                using (var accent = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(accent, 0, 0, 3, card.Height);
                }
            };
            host.Content.Controls.Add(card);

            var badge = new Label
            {
                Text = "  规划中  ",
                Font = new Font(Theme.UiFont, 8.5f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                BackColor = Theme.AccentSoft,
                AutoSize = true,
                Location = new Point(24, 22),
            };
            card.Controls.Add(badge);

            var title = new Label
            {
                Text = _title,
                Font = new Font(Theme.UiFont, 15f, FontStyle.Bold),
                ForeColor = Theme.Fg,
                AutoSize = true,
                Location = new Point(22, 52),
            };
            card.Controls.Add(title);

            var sub = new Label
            {
                Text = _subtitle,
                Font = new Font(Theme.UiFont, 9.5f),
                ForeColor = Theme.FgMuted,
                AutoSize = false,
                Size = new Size(card.Width - 60, 40),
                Location = new Point(24, 84),
            };
            card.Controls.Add(sub);

            var line = new Panel
            {
                Height = 1,
                Width = card.Width - 48,
                BackColor = Theme.LineSoft,
                Location = new Point(24, 126),
            };
            card.Controls.Add(line);

            var head = new Label
            {
                Text = "这个模块计划包含：",
                Font = new Font(Theme.UiFont, 9f, FontStyle.Bold),
                ForeColor = Theme.FgMuted,
                AutoSize = true,
                Location = new Point(24, 142),
            };
            card.Controls.Add(head);

            int y = 168;
            foreach (string item in _items)
            {
                var row = new Label
                {
                    Text = "·  " + item,
                    Font = new Font(Theme.UiFont, 9f),
                    ForeColor = Theme.FgDim,
                    AutoSize = false,
                    Size = new Size(card.Width - 60, 22),
                    Location = new Point(30, y),
                };
                card.Controls.Add(row);
                y += 26;
            }

            var note = new Label
            {
                Text = "界面已经为它留好位置，实现后即可直接用。",
                Font = new Font(Theme.UiFont, 8.5f),
                ForeColor = Theme.FgFaint,
                AutoSize = true,
                Location = new Point(24, y + 10),
            };
            card.Controls.Add(note);

            return host;
        }
    }

    /* ================================================================== */
    /* 模块登记表 —— 以后加功能改这里一处即可                                */
    /* ================================================================== */

    internal static class UiRegistry
    {
        public static List<MainModule> Build()
        {
            var list = new List<MainModule>();

            /* ============================================================
             * 1. 资源包 —— 一切「作用在某个资源包上」的功能都收在这里
             * ============================================================ */
            var pack = new MainModule
            {
                Id = "pack",
                Title = "资源包",
                Icon = "▣",
                Hint = "转换、合并、浏览与编辑资源包",
            };

            /* --- 1.1 转换 --- */
            pack.Subs.Add(new SubModule
            {
                Id = "pack.convert",
                Title = "版本转换",
                Hint = "选源包 → 选目标版本 → 预览改动 → 转换",
                Page = new ConvertPage(),
            });
            pack.Subs.Add(new SubModule
            {
                Id = "pack.batch",
                Title = "批量转换",
                Hint = "一次把多个资源包转到同一目标版本",
                Page = new BatchPage(),
            });

            /* --- 1.2 合并 --- */
            pack.Subs.Add(new SubModule
            {
                Id = "pack.merge",
                Title = "合并资源包",
                Hint = "添加来源 → 调整优先级 → 解决冲突 → 合成",
                Page = new PackMergePage(),
            });

            /* --- 1.3 浏览与编辑 --- */
            pack.Subs.Add(new SubModule
            {
                Id = "pack.browse",
                Title = "内容浏览",
                Hint = "文件树与按类型浏览，查看图片、音频与文本",
                Page = new PackBrowsePage(),
            });
            pack.Subs.Add(new SubModule
            {
                Id = "pack.meta",
                Title = "pack.mcmeta",
                Hint = "可视化编辑描述、图标与版本声明",
                Page = new PackMetaPage(),
            });
            pack.Subs.Add(new SubModule
            {
                Id = "pack.rename",
                Title = "批量重命名",
                Hint = "按规则批量改名，支持正则与预览",
                Page = new PackRenamePage(),
            });
            pack.Subs.Add(new SubModule
            {
                Id = "pack.new",
                Title = "新建资源包",
                Hint = "生成标准目录结构与默认图标",
                Page = new PackNewPage(),
            });

            list.Add(pack);

            /* ============================================================
             * 2. 工具箱 —— 与具体资源包无关的独立小工具
             * ============================================================ */
            var tools = new MainModule
            {
                Id = "tools",
                Title = "工具箱",
                Icon = "⚒",
                Hint = "不依赖某个具体资源包的独立工具",
            };
            tools.Subs.Add(new SubModule
            {
                Id = "tools.version",
                Title = "版本对照表",
                Hint = "查询 pack_format 与 Minecraft 版本的对应关系",
                Page = new VersionTablePage(),
            });
            tools.Subs.Add(new SubModule
            {
                Id = "tools.icon",
                Title = "图标与占位图",
                Hint = "生成 pack.png 与纯色 / 透明占位贴图",
                Page = new IconPage(),
            });
            list.Add(tools);

            /* ============================================================
             * 3. NBT —— 移植自 NBTExplorer（MIT，Justin Aquadro）的 NBT 数据能力
             * ============================================================ */
            var nbt = new MainModule
            {
                Id = "nbt",
                Title = "NBT",
                Icon = "❖",
                Hint = "浏览、编辑与转换 Minecraft 的 NBT 数据",
            };
            nbt.Subs.Add(new SubModule
            {
                Id = "nbt.edit",
                Title = "NBT 编辑器",
                Hint = "树形浏览、增删改标签、查找替换、另存为",
                Page = new NbtPage(),
            });
            nbt.Subs.Add(new SubModule
            {
                Id = "nbt.snbt",
                Title = "SNBT 与 JSON",
                Hint = "NBT 与文本互转：SNBT 读写、JSON 导出",
                Page = new NbtSnbtPage(),
            });
            list.Add(nbt);

            /* ============================================================
             * 4. 设置与帮助
             * ============================================================ */
            var settings = new MainModule
            {
                Id = "settings",
                Title = "设置与帮助",
                Icon = "⚙",
                Hint = "转换规则说明、程序信息与自测",
            };
            settings.Subs.Add(new SubModule
            {
                Id = "settings.rules",
                Title = "转换规则说明",
                Hint = "本工具会做哪些迁移、不会做哪些，以及原因",
                Page = new RulesPage(),
            });
            settings.Subs.Add(new SubModule
            {
                Id = "settings.about",
                Title = "关于与自测",
                Hint = "版本信息、数据来源、运行内置自测",
                Page = new AboutPage(),
            });
            list.Add(settings);

            return list;
        }
    }
}
