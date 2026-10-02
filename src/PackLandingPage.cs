using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace McPackStudio
{
    /// <summary>
    /// 「资源包」模块的落地页：进来先看到几张操作卡片，
    /// 点卡片进入对应的子模块；顶部子模块条也能直接切。
    /// </summary>
    internal sealed class PackLandingPage
    {
        /// <summary>请求打开某个子模块（子模块Id）</summary>
        public event EventHandler<string> SubRequested;
        /// <summary>请求导入资源包（true 为 ZIP）</summary>
        public event EventHandler<bool> ImportRequested;
        public event EventHandler<string> StatusChanged;

        private Control _view;
        private Panel _canvas;
        private readonly List<PackActionCard> _cards = new List<PackActionCard>();

        private void Status(string t) { var h = StatusChanged; if (h != null) h(this, t); }

        public Control CreateView()
        {
            if (_view != null) return _view;

            // 与主页同一套：只有画布，不做 AutoScroll / Dock 嵌套
            _canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.BgBase,
            };
            _canvas.Resize += (s, e) => Layout(_canvas.ClientSize);

            BuildCards();

            _view = _canvas;
            return _canvas;
        }

        private PackActionCard Add(string glyph, string title, string desc, string subActions, bool implemented)
        {
            var card = new PackActionCard
            {
                Glyph = glyph,
                CardTitle = implemented ? title : title + "（规划中）",
                CardDesc = desc,
                SubActions = subActions,
            };
            card.Activated += (s, e) => OnCard(title);
            card.SubActivated += (s, which) => OnSub(which);
            _cards.Add(card);
            _canvas.Controls.Add(card);
            return card;
        }

        private void BuildCards()
        {
            Add("convert", "版本转换", "把资源包转换到其它 Minecraft 版本", "开始转换|批量转换", true);
            Add("merge", "合并资源包", "多个资源包融合成一个，处理同名冲突", null, false);
            Add("browse", "浏览资源包", "文件树、按类型浏览、图片预览", "内容浏览|pack.mcmeta", false);
            Add("folder", "导入文件夹", "从本地文件夹导入现有的资源包", null, true);
            Add("download", "导入 ZIP", "从 ZIP 压缩包导入资源包", null, true);
            Add("create", "新建资源包", "从零生成标准目录结构与默认图标", "批量重命名", false);
        }

        private void OnCard(string title)
        {
            switch (title)
            {
                case "版本转换": Go("pack.convert"); break;
                case "合并资源包": Go("pack.merge"); break;
                case "浏览资源包": Go("pack.browse"); break;
                case "新建资源包": Go("pack.new"); break;
                case "导入文件夹":
                    var h1 = ImportRequested;
                    if (h1 != null) h1(this, false);
                    break;
                case "导入 ZIP":
                    var h2 = ImportRequested;
                    if (h2 != null) h2(this, true);
                    break;
            }
        }

        private void OnSub(string which)
        {
            if (which == "批量转换") Go("pack.batch");
            else if (which == "开始转换") Go("pack.convert");
            else if (which == "内容浏览") Go("pack.browse");
            else if (which == "pack.mcmeta") Go("pack.meta");
            else if (which == "批量重命名") Go("pack.rename");
        }

        private void Go(string subId)
        {
            Status("打开「" + subId + "」");
            var h = SubRequested;
            if (h != null) h(this, subId);
        }

        /* ------------------------------------------------------------ */

        public void Layout(Size client)
        {
            if (_canvas == null || _cards.Count == 0) return;

            const int gap = 16;
            const int padX = 26;
            const int padTop = 18;
            const int padBottom = 18;

            int avail = Math.Max(660, _canvas.ClientSize.Width - padX * 2);
            int colW = (avail - gap * 2) / 3;

            int usable = _canvas.ClientSize.Height - padTop - padBottom;
            int cardH = Math.Min(210, Math.Max(120, (usable - gap) / 2));

            for (int i = 0; i < _cards.Count; i++)
            {
                int col = i % 3;
                int row = i / 3;
                _cards[i].Bounds = new Rectangle(padX + col * (colW + gap), padTop + row * (cardH + gap), colW, cardH);
            }
        }
    }
}
