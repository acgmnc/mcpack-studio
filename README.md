# MCPack Studio · Minecraft 资源包工具箱

把 Minecraft 资源包转换到任意版本，并迁移跨越 1.13「扁平化」时改动过的资源路径与语言文件。

原生 C# / WinForms，单个 exe，**不需要安装任何运行时**（Windows 自带 .NET Framework 4.8）。

---

## 界面结构

**没有侧边栏**。导航分两处：

- **顶部导航条** —— 横向排开所有主模块（`主页 | 资源包 | 工具箱 | NBT | 设置与帮助`），
  当前模块高亮；已经在某个模块里时再点它一次会回到该模块的落地页
- **子模块条** —— 进入模块后出现在导航条下方，是该模块的子模块（自动换行）

内容区因此拿到全部宽度。

```
┌ 标题栏 ────────────────────────────────────────┐
├ 主页 | 资源包 | 工具箱 | 设置与帮助 ───────────┤  ← 顶部导航
├ [版本转换][批量转换][合并资源包][内容浏览]… ───┤  ← 子模块条（进模块后出现）
│                                                │
│                  内容区                         │
│                                                │
└ 状态栏：位置：资源包 · 版本转换 ────────────────┘
```

### 主页：只列主模块

启动先到主页，一屏列出所有主模块。卡片下方的小标签直接显示该模块包含哪些子模块，
数量多的时候末尾折成 `+N`：

```
MCPack Studio    Minecraft 资源包工具箱
──────────────────────────────────────────────────────────────────
┌─ 资源包 ────────────┐ ┌─ 工具箱 ───────────┐ ┌─ 设置与帮助 ───────┐
│ ▣  资源包   [进入]  │ │ ⚒  工具箱  [进入]  │ │ ⚙  设置与帮助[进入]│
│ 转换、合并、浏览…   │ │ 不依赖具体资源包的 │ │ 转换规则说明、程序 │
│ [版本转换][+6]      │ │ [版本对照表][+1]   │ │ [转换规则说明][+1] │
│ 7 个子模块 · 2 已实现│ │ 2 个子模块 · 1 已实现│ │ 2 个子模块 · 2 已实现│
└─────────────────────┘ └────────────────────┘ └────────────────────┘
```

点卡片或「进入」都能进模块。列数与卡片高度按窗口尺寸自适应：窗口宽就排 3 列，
窄了自动降到 2 列或 1 列；卡片整块垂直居中，不会出现「上面挤一堆、下面一大片空白」。
模块数不是列数的整数倍时，末尾补一个虚线的「空位」卡片占满整排，
以后加主模块会自动填上 —— 但不会为了凑数就永远占着下面一整排空位。

### 进入模块后

以「资源包」为例，点进去看到的是该模块的操作卡片（子模块条同时出现）：

```
资源包
┌─ 版本转换 ───────┐ ┌─ 合并资源包 ─────┐ ┌─ 浏览资源包 ─────┐
│        ⇄         │ │         ⊕          │ │         ▤          │
│   [开始转换][批量转换]                    │ │  [内容浏览][pack.mcmeta] │
└──────────────────┘ └────────────────────┘ └────────────────────┘
┌─ 导入文件夹 ─────┐ ┌─ 导入 ZIP ────────┐ ┌─ 新建资源包 ─────┐
└──────────────────┘ └────────────────────┘ └────────────────────┘
```

两种方式都能到子模块：点操作卡片，或点子模块条。

### 完整模块表

```
资源包                                    工具箱              设置与帮助
├ 版本转换    ✓  选源包 → 选目标版本        ├ 版本对照表 ✓      ├ 转换规则说明 ✓
├ 批量转换    ✓  多个包一次转换            └ 图标与占位图 ✓    └ 关于与自测  ✓
├ 合并资源包  ✓  多包融合、冲突处理
├ 内容浏览    ✓  文件树 / 按类型 / 预览
├ pack.mcmeta ✓  描述、图标与版本声明
├ 批量重命名  ✓  按规则改名，支持正则
└ 新建资源包  ✓  生成标准目录结构

NBT（移植自 NBTExplorer）
├ NBT 编辑器   ✓  树形浏览 / 增删改 / 查找替换 / 另存
└ SNBT 与 JSON ✓  NBT 与文本互转、导出 JSON
```

**13 个子模块全部已实现**。

一共 **4 个主模块 / 13 个子模块**。状态栏始终显示当前位置（如「位置：资源包 · 版本转换」）。

程序会记住你上次停留的子模块，下次打开直接回到那里
（想每次从主页开始就删掉 `%APPDATA%\MCPackStudio\settings.ini`）。

---

## 已实现的功能

### 资源包 · 版本转换

三步式：**分析 → 预览 → 转换**。详见下面「版本转换能做什么」。

### 资源包 · 批量转换

选一个文件夹，自动扫描里面的资源包（顶层的 `.zip` 与直接含 `pack.mcmeta` 的一级子文件夹），
一次全部转到同一目标版本，逐条给出成功/失败结果。

### 工具箱 · 版本对照表

97 条 `pack_format` 记录，可输入版本号或格式号实时过滤。

### NBT · NBT 编辑器

把 NBTExplorer 的能力搬了进来（MIT 许可，详见「数据来源与许可」）。打开 `.dat` / `.nbt` / `level.dat` 后：

- **树形浏览**：13 种标签类型按类型着色，复合标签与列表按需展开（大文件不会一次性铺开）
- **编辑**：添加子标签、改名、改值、删除、剪切/复制/粘贴、上移/下移，支持撤销/重做
- **查找替换**：按名称（包含 / 通配符 / 正则 / 相等）、类型、值（等于、不等、大于、小于、包含、开头）组合筛选，结果双击定位
- **数组与十六进制**：数组直接填逗号分隔的数字；字节数组另有十六进制编辑器
- **格式识别**：gzip / zlib / 裸 deflate / 不压缩自动判断，基岩版 level.dat 的「8 字节头 + 小端」也认
- **导出**：SNBT 文本、JSON；SNBT 还能导入回树里

### NBT · SNBT 与 JSON

三张卡片：**打开文件 → 文本 → 存回去**。把 `.dat` 变成可读的 SNBT 文本，改完再写回文件，
往返无损（JSON 导出会丢超过 2^53 的长整数精度，界面上有提示）。

### 设置与帮助 · 转换规则说明

把「会做哪些迁移、不会做哪些、以及为什么」写进界面，避免误以为它是万能转换器。

### 设置与帮助 · 关于与自测

版本信息、数据来源，以及**一键运行内置自测**（314 项断言，在临时目录里跑完整流程，不动你的文件）。

---

## 版本转换能做什么

Minecraft 在 1.13 做了一次「扁平化」（Flattening），把大量资源路径改掉了。旧资源包直接用在新版本上会**贴图丢失、语言显示成键名**。这个工具负责：

| 变动 | 转换前 | 转换后 |
| --- | --- | --- |
| 方块纹理目录 | `textures/blocks/` | `textures/block/` |
| 物品纹理目录 | `textures/items/` | `textures/item/` |
| 语言文件地区码 | `en_US.lang` | `en_us.json` |
| 语言文件格式 | `block.stone=Stone` | `{"block.minecraft.stone": "Stone"}` |
| 语言键名 | `block.stone`、`tile.chest` | `block.minecraft.stone`、`block.minecraft.chest` |
| 版本声明 | `"pack_format": 3` | `"pack_format": 15`（按目标版本） |

以上每一项都会在转换前**逐文件列出**给你确认，可以取消勾选排除任意文件。

### 双向转换

反过来也支持：把 1.20 的资源包转回 1.12 时，`textures/block/` 会退回 `textures/blocks/`，
语言文件退回 `en_US.lang`（旧版客户端不认 `.json`）。

### 1.21.9+ 的新格式

从 pack_format 69 起，Minecraft 改用 `min_format` / `max_format` 声明版本兼容范围。
如果源文件已经用了这套新格式，转换时会**保留**它并同步更新；如果源文件是老的
`pack_format` 单数字写法，就只更新那一个数字，不会凭空塞进 `min_format`。

---

## 用法

### 图形界面

双击 `MCPackStudio.exe`。左侧选模块，右侧按提示操作。

也可以把资源包文件夹或 ZIP **直接拖进窗口**（或作为命令行参数传入），
会自动切到「版本转换 · 资源包转换」并预填分析。

### 命令行

不开界面就能转换，适合批量处理：

```bat
MCPackStudio.exe --convert MyPack --to 1.20.1 --out MyPack_1.20.1
MCPackStudio.exe --convert MyPack.zip --to 46 --out New.zip --zip
MCPackStudio.exe --analyze MyPack
MCPackStudio.exe --nbt level.dat
MCPackStudio.exe --help
```

| 参数 | 说明 |
| --- | --- |
| `--convert <路径>` | 源资源包（.zip 或含 pack.mcmeta 的文件夹） |
| `--to <版本>` | 目标版本，可填 Minecraft 版本号（`1.20.1`）或 pack_format（`15`、`46`） |
| `--out <路径>` | 输出位置 |
| `--zip` | 输出为 ZIP |
| `--meta-only` | 只改 pack.mcmeta 的版本号，不迁移任何资源文件 |
| `--no-lang` | 迁移语言文件路径但**不**改写键名 |
| `--exclude <相对路径>` | 排除某个文件，可重复 |
| `--analyze` | 只分析不转换 |
| `--report <文件>` | 完整日志写入文件 |
| `--nbt <文件>` | 打开界面并直接进 NBT 编辑器载入这个文件 |

---

## 安全性设计

转换会动你的文件，所以做了这些防护：

- **绝不原地覆盖**：输出路径与输入相同会被拒绝
- **拒绝输出到输入内部**：避免递归复制
- **先写暂存目录再就位**：转换中途失败不会留下半成品；覆盖已有输出目录时会先备份，失败自动还原
- **不会静默覆盖**：如果 `textures/blocks/stone.png` 要改名为 `textures/block/stone.png` 而后者已存在，前者会保持原路径并给出说明
- **ZIP 路径穿越防护**：`../` 之类的条目会被丢弃
- **pack.mcmeta 自检**：写出的 JSON 会重新解析验证，语法错误的原文件会被替换成可用内容
- **描述会追加标记**：`§8[已由 MCPack Studio 转换至 …]`，方便事后追溯；重复转换不会叠加

---

## 已知限制（重要）

**这个工具只做「元数据 + 1.13 扁平化的已知路径迁移」，不是万能的版本降级器。**

Minecraft 各版本之间还有大量零散变动（个别贴图改名、模型格式调整、方块状态重写、
`supported_formats` 语义变化、组件系统重写等），这些**没有权威对照表**，本工具不做猜测性改动 ——
猜错会静默弄坏资源包，比不动更糟。界面里的「转换规则说明」页也写了同样的话。

具体来说：

- **模型 / 方块状态** 只跟随目录迁移，文件内容原样保留。1.8 时代的模型格式与今天差异较大，
  转高版本后可能需要手工调整
- **语言键名只迁移两段式旧键**（`block.stone`）。三段式的（`block.mymod.custom`）判定为
  mod 自定义键，不动
- **反向转换时语言键不会去掉 `minecraft.` 命名空间**。旧版客户端不认的键会被忽略，
  不影响使用，但信息保留得更完整、便于再转回来
- **`--meta-only` 模式**等价于原项目的版本转换：只重写版本号，一个资源文件都不动

转换后建议把输出目录在游戏里实际加载一次。转换报告里会列出所有改动，可以逐项核对。

---

## 从源码构建

需要 Windows 自带的 .NET Framework 编译器（路径见 `build.ps1`），无需 Visual Studio。

```powershell
.\build.ps1
```

产物：

```
release\MCPackStudio.exe      约 130 KB
release\使用说明.md
```

### 自测

内置 314 项断言，覆盖版本表、JSON 读写、语言键规则、双向转换、ZIP 输入输出、合并与冲突决策、
重命名冲突、路径安全、损坏文件处理，以及 NBT 的模型、SNBT 往返、查找规则、撤销栈、
二进制读写（大端 / 小端、gzip / zlib / 不压缩、基岩版头、坏数据）等：

```powershell
.\MCPackStudio.exe --selftest selftest.txt
```

界面里的「设置与帮助 · 关于与自测」也能一键跑。

### 排版与配色自检

界面排版或深色配色出问题时，用这几个调试开关把实际状态导出来，不用靠肉眼猜：

```powershell
# 把客户区依次设成默认 / 最小 / 放大 / 宽扁四种尺寸，每种写一份控件布局快照
.\release\MCPackStudio.exe --shot-sizes layout.txt

# 依次切到全部页面（含 NBT 两页）各截一张图 —— 用 PrintWindow 让窗口自己渲染：
# 自绘控件内容正确，窗口不在前台也照样截得到
.\release\MCPackStudio.exe --shots tools\out\pages

# 只导出当前布局（窗口尺寸变化后会自动重写一次）
.\release\MCPackStudio.exe --layout layout.txt

# 扫描截图里「漏白的原生控件」，超标的按行报出来
.\release\MCPackStudio.exe --theme-audit tools\out\pages
```

布局快照里每行是一个控件的 `bounds`（父坐标）、`abs`（相对窗口客户区）和 `dock` 方式，
对不齐时一眼就能看出是谁占了谁的地方。

`--theme-audit` 的判定条件是「够亮 + 够灰 + 横向连续 40px 以上」三条同时满足 ——
浅色文字的笔画不会形成 40px 连续段，所以只有真正的白色控件区域会被报出来。

---

## 代码结构

```
src/
├── Settings.cs        版本对照表（97 条 pack_format → 版本范围）
├── AppSettings.cs     本地设置（记住上次停留的子模块）
├── Json.cs            极简 JSON 解析与保序输出（不依赖第三方库）
├── ZipIo.cs           ZIP 读写 + 路径穿越防护
├── PackAnalyzer.cs    资源包识别与统计、pack.mcmeta 解析
├── MigrationRules.cs  迁移规则与语言键规则
├── Converter.cs       转换规划、冲突检测、执行、报告
│
├── Theme.cs           统一配色与字体
├── DarkControls.cs    自绘深色控件：PageTabs / FlatSelect / FancyProgress
├── DarkScroll.cs      自绘深色滚动条（DarkScrollBar / DarkScrollHost / WheelRouter）
├── Widgets.cs         共享控件：卡片 / 按钮 / 复选框 / 深色列表 / 报告表 / 工具函数
├── TopNav.cs          顶部导航条（横向模块切换 + 主页入口）
├── Modules.cs         模块契约 IModulePage + 登记表 UiRegistry + 占位页
├── HomePage.cs        主页：主模块列表（ModuleCard + 子模块标签 + 空位 + 矢量图标集）
├── PackLandingPage.cs 模块落地页：资源包的操作卡片（PackActionCard）
├── ShellForm.cs       应用外壳：标题栏 / 顶部导航 / 子模块条 / 内容区 / 状态栏 + 区域排版 + 排版自检
│
├── ConvertPage.cs     资源包 · 版本转换
├── Pages.cs           批量转换 / 版本对照表 / 转换规则说明 / 关于与自测
│
├── StagedWriter.cs    暂存写入 + 原子替换 + 页面搭建辅助（PageKit）+ 深色模式（DarkMode）
├── ThemeAudit.cs      漏白自检：扫描截图里的白色原生控件区域
├── PackMerge.cs       合并引擎：来源优先级、冲突检测与决策
├── PackMergePage.cs   资源包 · 合并资源包
├── PackBrowsePage.cs  资源包 · 内容浏览（文件树 + 类型筛选 + 预览）
├── PackMetaPage.cs    资源包 · pack.mcmeta（含 § 颜色码游戏内预览）
├── PackRename.cs      重命名引擎：查找替换 / 正则 / 前后缀 / 序号 + 冲突检测
├── PackRenamePage.cs  资源包 · 批量重命名
├── PackScaffold.cs    精简 PNG 编码器 + 图像生成 + 新建资源包骨架
├── PackNewPage.cs     资源包 · 新建资源包
├── IconPage.cs        工具箱 · 图标与占位图
│
├── NbtTag.cs          NBT 标签模型（13 种类型）+ 遍历 + 文本 / 数字转换
├── NbtIo.cs           NBT 二进制读写：大小端、gzip / zlib / deflate、基岩版头、原子写
├── NbtSnbt.cs         SNBT 读写（Minecraft 的文本 NBT 写法）+ JSON 导出
├── NbtEdit.cs         查找规则与搜索、快照式撤销栈、编辑操作（重名 / 粘贴 / 移动）
├── NbtPage.cs         NBT · NBT 编辑器
├── NbtSnbtPage.cs     NBT · SNBT 与 JSON
├── NbtDialogs.cs      NBT 相关弹窗：改值 / 十六进制 / 添加 / 查找替换 / 另存为 / 文本
│
├── Cli.cs             命令行模式
├── SelfTest.cs        自测
└── Program.cs         入口
```

### 怎么加新功能

**加一个子模块**（进到某个主模块里能看到）：

1. 写一个类实现 `IModulePage`（`CreateView` 返回页面控件，`OnEnter` 里刷新数据）
2. 在 `UiRegistry.Build()` 里往对应主模块的 `Subs` 加一条 `SubModule`

导航、面包屑、子模块条（自动换行）、状态栏、位置记忆都会自动生效，**外壳代码不用动**。

**加一个主模块**（占一个主页卡片）：

在 `UiRegistry.Build()` 里 `new MainModule { ... }` 并 `list.Add(...)` 即可。
主页会自己多出一张卡片，第一排的空位也会自动少一个。

**加一个操作卡片**（资源包落地页上）：

在 `PackLandingPage.BuildCards()` 里调一次 `Add(...)`，再到 `OnCard()` / `OnSub()`
里加分支指向目标子模块。

`PlannedPage` 仍保留在代码里，用于以后加新模块时先占位（界面会显示「规划中」和计划包含的能力，不是死链接）。

### 设计取舍

- **没有侧边栏**：模块切换靠顶部导航条，子模块切换靠它下面的子模块条，
  内容区拿到全部宽度。
- **外壳的三个区域手算坐标，不用 Dock**（`ShellForm.LayoutRegions()`）：
  标题栏+顶部导航+子模块条贴顶、状态栏贴底、内容区吃中间。
  早期版本内容区用 `Dock=Fill`，而 WinForms 是**按控件 z 序反向停靠**的 ——
  内容区会抢在 `Dock=Top` 的外壳之前占满整个窗口，把标题栏和导航条压在下面，
  主页卡片也会被裁掉一半（标题看不见、只剩说明文字）。
  手算坐标以后跟 z 序完全无关，`BringToFront()` 那类调用也一并删掉了。
- **子模块条高度固定 46px**：高度一变下面的内容区就要跟着重排，切换模块时整页会跳一下。
- **主页与落地页的卡片网格自己算行列**：需要按容器尺寸决定列数与卡片高度，
  由各自的 `Layout()` 在 `Resize` 时重排。卡片绘制全部用
  `TextRenderer.MeasureText` 现算纵向位置，不写死坐标 ——
  写死就会出现文字互相盖住或者顶出卡片被裁掉。卡片自带裁剪矩形，
  排不下时不会溢到状态栏上。
- **「资源包」有专门的落地页**（`PackLandingPage`），因为它子模块最多。
  其它模块直接进第一个子模块，少一层点击。
- **规划与执行分离**：`PlanBuilder.Build()` 只读源包、产出一份逐文件的动作清单
  （复制 / 重命名 / 改写 / 排除），界面和命令行都先展示这份清单再执行。
  `ConvertExecutor.Execute()` 负责落地，全程写暂存目录，最后一步才就位。
- **卡片内容用 `Card.Body` 排版**：`Card` 自己画标题栏，内容区是一个带内边距的
  `Panel`，页面里用 `Dock` 排版而不是手算坐标，窗口缩放时不会错位。
  说明文字这类会折行的内容，用 `RulesPage` 里的 `MeasureHeight()` 先量出实际高度
  再决定容器高度，避免写死的高度把文字裁掉。
- **图标全部矢量绘制**，不用字体字形 —— 中文字体里 `⤓`、`⇄` 这类符号经常缺字形或
  被渲染成全角方块。

### 深色主题：哪些控件必须自绘

WinForms 是**混合渲染**的：一部分控件走 GDI+ 自己画（颜色随便改），
另一部分是把 Win32 原生控件套个外壳 —— 边框、滚动条、下拉按钮都由**系统主题引擎**画，
`BackColor`、`FlatStyle` 这些属性根本管不到，`SetWindowTheme` 也不是万能的。

实测结果（`--theme-audit` 量出来的，不是猜的）：

| 控件 | 原生部分 | 处理方式 |
| --- | --- | --- |
| `TabControl` | 页眉整条（含最后一个页签右边的空白） | **换成自绘的 `PageTabs`**。父容器自绘、`DrawItem` 里铺满、`SetWindowTheme` 三种办法都被它自己的 `WM_PAINT` 盖回去 |
| `ComboBox` | 边框 + 下拉按钮是纯白 `RGB(255,255,255)` | **换成自绘的 `FlatSelect`**（展开列表也自绘）。四种主题办法都压不住 |
| `ProgressBar` | 未填充部分是系统浅灰 | **换成自绘的 `FancyProgress`**（顺带做了 UI 线程封送，后台线程推进度不会崩） |
| 各滚动条 | 滚动条槽/滑块 | `SetWindowTheme(hwnd, "DarkMode_Explorer")` —— 实测**只有这个名字有效**，`DarkMode_CFD` / `DarkMode_ItemsView` / `Explorer` 全都无效 |
| 页面级滚动条 | 滑块始终是白的 | **换成自绘的 `DarkScrollHost`**。`AutoScroll` 面板的滚动条槽能套主题，但滑块怎么都不变色（和 `ListView` 的滚动条一样），所以干脆不用 `AutoScroll`：自己测内容高度、自己移动 `Content.Top`，配一条自绘的 `DarkScrollBar`。见下 |
| 多行 `TextBox` 的滚动条 | 滑块始终是白的 | **换成 `RichTextBox` + `ScrollBars.None`**（自带滚轮与键盘滚动）。它的 `EDIT` 窗口套主题会露出浅色滚动条槽，不套又留白滑块，两头都不对 |
| `DataGridView` | 内容区最上沿有一条白色边线 | 用顶部 1px 内边距把它挤出可视范围（`BorderStyle` / `AdvancedBorderStyle` 都关不掉，`OnPaint` 里也盖不住） |

两个关键坑：

1. **套主题的时机**。滚动条是**延迟创建**的（内容超出、窗口变大时才建），
   而且出现后会按系统浅色画一遍 —— 在它出现之前套主题是白费劲。
   所以 `DarkMode.ApplyDeferred()` 会推迟到本次消息处理完、再连试几次。
2. **跳过窗口 ≠ 跳过子窗口**。`TextBox`（`EDIT`）自己不能套深色，
   但它的滚动条子窗口必须套 —— `ApplyThemeToTree` 是分开处理的。

### 自绘滚动条（`DarkScroll.cs`）

滑块压不住的那类滚动条，一律换成自绘。三个部件：

| 类型 | 职责 |
| --- | --- |
| `DarkScrollBar` | 自绘滑块与轨道，接管拖动、点击翻页、悬停高亮 |
| `DarkScrollHost` | 页面宿主。**不用 `AutoScroll`** —— 自己测内容高度、自己移动 `Content.Top` |
| `WheelRouter` | 全局消息过滤器。鼠标停在列表/文本框上时滚轮默认发给那个子控件，父容器收不到，这里把没被消耗的滚轮转给最近的 `DarkScrollHost` |

`DarkScrollHost` 故意暴露**与 `Panel` 同名的 `Padding` 属性**并转发给内层 `Content`，
所以原来写 `new Panel { AutoScroll = true, Padding = ... }` 的地方只要换成
`PageKit.MakeHost()`，其余代码一行都不用改。

验证方式是 `PrintWindow` 截图后**逐像素取样**滚动条区域：滑块取到 `Theme.LineStrong`、
轨道取到 `Theme.BgPanel`，亮像素 0 个 —— 不靠肉眼判断。

自检用 `--theme-audit`：判定条件是「够亮 + 够灰 + 横向连续 40px 以上」三条同时满足，
浅色文字的笔画不会形成 40px 连续段，所以只有真正的白色控件区域会被报出来。

---

## 数据来源与许可

- 版本对照表数据来自 [Little100/Minecraft-Resourcespack-Editor](https://github.com/Little100/Minecraft-Resourcespack-Editor)
  的 `public/version_map/version_map.json`（GPL-3.0），已内嵌进 `Settings.cs`
- 「版本转换」这一功能的原始实现参考了同项目的 `src-tauri/src/version_converter.rs`
  （其行为是只重写 `pack_format`，本工具在它基础上增加了资源迁移）
- 原项目版权：Copyright (C) Little_100
- NBT 模块移植自 [jaquadro/NBTExplorer](https://github.com/jaquadro/NBTExplorer)（MIT）
  及其依赖的 [Substrate](https://github.com/jaquadro/Substrate)（MIT）——
  树形浏览、增删改标签、查找替换、SNBT / JSON 导出的行为都以它为准
- NBTExplorer 与 Substrate 版权：Copyright (C) Justin Aquadro，MIT 许可证
  （完整条款与移植说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)）

**本项目是上述项目的修改版本（modified version），以 GPL-3.0 发布，完整许可证见 [LICENSE](LICENSE)。**

### 相对上游的改动

按 GPL-3.0 第 5 条 a 款的要求声明：本项目为独立重写的 C# / WinForms 实现，
与原项目的 Tauri（Rust + React）技术栈不同，并非其代码的移植。改动包括：

1. 用 C# 重写全部逻辑，不依赖任何第三方库（ZIP 读写、JSON 解析、PNG 编码均为手写）
2. 版本转换在「只改 `pack_format`」之外，增加了 1.13 扁平化资源重命名与语言文件格式转换
3. 新增批量转换、转换预览（先列出逐文件动作再执行）、暂存目录 + 原子替换的安全写入
4. 新增合并资源包、内容浏览、pack.mcmeta 编辑、批量重命名、新建资源包、图标生成六个功能
5. 重新设计了界面与模块结构，并新增命令行模式与内置自测（314 项断言）
6. 新增 NBT 模块：用 C# 重写了 NBTExplorer 的能力（不引用它的 DLL），并按本项目的深色界面风格重做交互；
   格式支持在原版之上补充了基岩版小端与 8 字节头识别

修订记录见本仓库的提交历史。

## 测试用资源包

仓库里带了两个小资源包，专门用来手工验证各功能（自测不依赖它们，会自己造数据）：

| 目录 | 用途 |
| --- | --- |
| `testpack-1.12/` | `pack_format 3` 的旧版包，含 `textures/blocks/`、`textures/items/`、`en_US.lang`，用来验证版本转换 |
| `testnbt/` | NBT 合成样本：Java 版（gzip + 大端，覆盖全部 13 种标签类型）与基岩版（小端 + 8 字节头）各一份，另有 `sample.snbt` 与 `sample.json`；详见该目录下的 readme |
| `testpack-conflict/` | 与上一个包有 **20 个同名文件**，其中 `stone.json` 与 `pack.mcmeta` 内容不同、另 18 个字节相同，另多一个独有文件 —— 用来验证**合并资源包**的冲突检测（应报"内容不同 2 个、自动合并 18 个"） |

## 贡献

欢迎提 Issue 与 PR。提交前请先：

```powershell
.\MCPackStudio.exe --selftest      # 314 项断言应全部通过
```

