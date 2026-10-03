# NBT 模块移植 · 交接文档

> **任务**：把 NBTExplorer CPE 移植进 mcpack-studio（MCPack Studio）
> **分支**：`feat/nbt-editor`（已推送，HEAD `5fb864f`）
> **基线**：`origin/main` = `bbf3ac9`（含「统一深色主题：自绘原生控件 + 漏白自检」那次改动）
> **状态**：功能完成、编译零错误、自测 314 项全通过、深色主题漏白自检通过
> **文档位置**：以仓库内本文件为准，桌面副本仅供离线查看

---

## 0. 一句话结论

NBT 的**数据能力**（读写、编辑、查找、格式转换）已用 C# 重写进本项目并接入现有模块系统：
新增 `NBT` 主模块（2 个子模块）、7 个源文件约 5800 行、5 个测试样本、82 项新自测断言。
**没有**引用 NBTExplorer 的任何 DLL 或源码 —— 桌面上那份 `NBTExplorer CPE` 下的 exe/dll 仍然只是参考物。

---

## 1. 交付物清单

### 1.1 分支与提交

| 提交 | 说明 |
| --- | --- |
| `88e70e3` | 移植 NBTExplorer：新增 NBT 模块（编辑器 + SNBT/JSON 互转）—— 主体 |
| `497b3a8` | 合并 `origin/main`（你朋友的「统一深色主题」） |
| `5fb864f` | NBT 模块适配深色主题（原生下拉框换 `FlatSelect`、多行框换无滚动条 `RichTextBox`） |

相对 `origin/main`：**20 个文件，+6594 / −20 行**。
开 PR：`https://github.com/acgmnc/mcpack-studio/pull/new/feat/nbt-editor`

### 1.2 新增文件

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `src/NbtTag.cs` | 929 | 13 种标签类型的数据模型、深拷贝/深比较、遍历与路径、文字与数字转换 |
| `src/NbtIo.cs` | 1150 | 二进制读写：大端/小端、gzip/zlib/裸 deflate/不压缩、基岩版 8 字节头、原子写 |
| `src/NbtSnbt.cs` | 507 | SNBT（Minecraft 文本 NBT）解析与格式化、JSON 导出 |
| `src/NbtEdit.cs` | 448 | 查找规则与搜索、快照式撤销栈、编辑操作（重名/粘贴/移动） |
| `src/NbtPage.cs` | 1384 | 「NBT 编辑器」页：树形浏览、工具栏、详情面板、快捷键 |
| `src/NbtSnbtPage.cs` | 343 | 「SNBT 与 JSON」页：文件 ↔ 文本 ↔ 文件 |
| `src/NbtDialogs.cs` | 1028 | 6 个弹窗：改值 / 十六进制 / 添加标签 / 查找替换 / 另存为 / 文本 |
| `THIRD-PARTY-NOTICES.md` | 49 | NBTExplorer、Substrate 的 MIT 署名与移植范围声明 |
| `testnbt/` | 5 个文件 | 合成样本：Java（gzip+大端）、基岩版（小端+头）、`.snbt`、`.json`、readme |

### 1.3 改动的既有文件

| 文件 | 改动 |
| --- | --- |
| `src/Modules.cs` | 登记 `NBT` 主模块（`nbt.edit` / `nbt.snbt` 两个子模块） |
| `src/HomePage.cs` | 补一个「标签树」矢量图标，`GlyphFor` 认识 `nbt` |
| `src/TopNav.cs` | 同上（顶部导航的图标映射） |
| `src/Program.cs` | 新增 `--nbt <文件>`：开界面直接载入 NBT |
| `src/ShellForm.cs` | `OpenNbtFile` / `FindNbtPage`；截图步骤加 NBT 两页与样本预载；**`CaptureTo` 改用 `PrintWindow`** |
| `src/SelfTest.cs` | 新增第 15、16 节共 82 项断言 |
| `README.md` | 模块表、功能说明、代码结构、`--nbt` 用法、断言数量、许可署名 |

---

## 2. 功能范围

### 2.1 已实现（对齐 NBTExplorer 的对应能力）

| NBTExplorer | 本项目位置 |
| --- | --- |
| Open / Open Folder / Refresh / Save / Save As | ①卡片：选文件、选文件夹（列出目录里的 NBT 文件）、新建空文件、重新载入；保存 / 另存为（可选压缩方式与大小端） |
| 树形浏览（按类型区分） | ②卡片左树，13 种类型各自配色；复合标签与列表**按需展开**，大文件不会一次性铺开 |
| Edit Value / Edit String | 改值弹窗（按类型给不同输入；数组逗号分隔；字符串多行） |
| Add * Tag（13 种） | 添加子标签弹窗；列表会自动按元素类型定型 |
| Rename / Delete | 工具栏 + `F2` / `Delete`；根标签也能改名（会同步写文件的根名） |
| Cut / Copy / Paste | 工具栏 + `Ctrl+X/C/V`；粘到列表会校验元素类型；重名自动加 `_2` 后缀 |
| Move Up / Move Down | 工具栏；顺序变化同样可撤销 |
| Find / Find Next / Replace | 名称（包含/通配符/正则/相等）× 类型 × 值（等于/不等/大于/小于/包含/开头），结果双击定位，支持全部替换 |
| Hex Editor | 字节数组十六进制编辑 + ASCII 预览 |
| SnapshotList 撤销 | 快照式撤销/重做 32 步；节点数 > 40 万自动关闭并在界面说明 |
| NBTUtil --print / --print-json | 导出 SNBT / JSON；**并且能反向**：文本改完存回 `.dat` |

### 2.2 在原版之上补充的

- **基岩版支持**：小端字节序 + `level.dat` 的 8 字节头（版本号 + 负载长度）识别与写回
- **压缩自动判定**：gzip / zlib / 裸 deflate / 不压缩；大小端按「大端 → 小端 → 跳 8 字节头 + 小端」顺序试，
  要求整段读干净，**宁可不加载也不猜错**（猜错会把存档写坏）
- `--nbt <文件>` 命令行入口、`testnbt/` 样本、82 项自测断言

### 2.3 明确没做（有意为之）

- 区域文件（`.mca`）与区块级编辑（NBTExplorer 的 Chunk Finder）—— 那是存档工具方向，与资源包工具箱关系不大
- 多文件标签页 / 最近文件列表 —— 现有外壳是「一个页面一个任务」，没做多文档
- Mac 版界面 —— 上游那套 Cocoa 代码与本项目无关

---

## 3. 架构：NBT 模块是怎么接进现有程序的

### 3.1 分层（依赖单向，下层不认识上层）

    页面层  NbtPage / NbtSnbtPage   只做「控件 ↔ 模型」搬运，不碰字节
    弹窗层  NbtDialogs              值编辑、查找替换、另存为选项
    编辑层  NbtEdit                 查找规则、撤销栈、编辑操作
    文本层  NbtSnbt                 SNBT 解析/格式化、JSON 导出
    格式层  NbtIo                   二进制读写、压缩、字节序
    模型层  NbtTag                  标签类型与树结构（不认识上面任何一层）

### 3.2 数据流

    文件 --NbtIo.Load--> NbtDocument { Root, RootName, Compression, LittleEndian, HasBedrockHeader }
                              |
                              +-- NbtPage：把 Root 铺成 TreeView（TreeNode.Tag 直接指向 NbtTag）
                              +-- 任何改动：_history.Push(Root 深拷贝) -> 改标签 -> 局部刷新或整树重建
                              +-- NbtIo.Save(doc, path, compression, littleEndian, bedrockHeader) --> 文件

要点：**树节点不存副本**，`TreeNode.Tag` 就是 `NbtTag` 本身，所以编辑不需要回写；
父容器与序号现场算（`NbtPage.SpotOf`），不维护容易失同步的索引表。

### 3.3 关键接口

    // 格式层（src/NbtIo.cs）—— 唯一碰文件的入口
    internal enum NbtCompression { None, GZip, Zlib, Deflate }

    internal sealed class NbtDocument
    {
        public string Path; public NbtCompound Root; public string RootName;
        public NbtCompression Compression; public bool LittleEndian;
        public bool HasBedrockHeader; public int BedrockVersion;      // 默认 9
        public long FileBytes; public long PayloadBytes;
        public string SourceFormat();      // "gzip · 大端" / "无压缩 · 小端 · 基岩版头"
    }

    internal static class NbtIo
    {
        public static NbtDocument Load(string path);                    // 失败抛 IOException（中文消息）
        public static void Save(NbtDocument doc, string path,
                                NbtCompression compression, bool littleEndian, bool bedrockHeader);
        public static byte[] Serialize(NbtCompound root, string rootName, bool littleEndian);
        public static NbtCompound Deserialize(byte[] data, int offset, int count,
                                              bool littleEndian, out string rootName);
        public static string DescribeCompression(NbtCompression c);
    }

    // 模块契约（src/Modules.cs，早就有，NBT 页只是实现它）
    internal interface IModulePage
    {
        string Title { get; }   string Subtitle { get; }
        Control CreateView();   void OnEnter();   void OnLeave();
    }

---

## 4. 接手必须先看的 8 条约定

1. **只能用 C# 5 语法**。编译器是 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（版本 4.8.9221.0），
   `build.ps1` 里写死了 `/langversion:5`。禁止：字符串插值、`?.`、表达式体成员、自动属性初始化器、
   `nameof`、模式匹配、`out var`、元组、局部函数；**不依赖任何第三方库**（JSON/ZIP/PNG 都是仓库手写的）。
2. **写文件必须原子**：`NbtIo.Save` 先写同目录临时文件，成功再 `File.Replace`，失败删临时文件。
   别退化成「直接覆盖原文件」。
3. **撤销快照要在改动之前记**。`NbtPage` 里凡是 `_history.Push` 都在赋值之前；
   若先改后发现校验失败，用 `_history.PopLast()` 丢掉这一步（`ApplyDetail` 就是这个写法）。
4. **根名有两份数据**：`doc.RootName`（写文件用）与 `doc.Root.Name`（树上显示）。
   目前改名路径已同步两处，**新写的改根名代码记得两处都改**。
5. **空列表的元素类型按规范写 `TAG_End`**。文件里若声明了别的类型，读进来会保留，再存盘会变成 `End` ——
   这是 NBT 规范本身的不对称，不是 bug。
6. **深色主题**：不要用原生 `ComboBox`（白边框压不住，用 `FlatSelect` + `Ui.StyleSelect`）；
   多行文本不要用 `TextBox`（滚动条滑块是白的，用 `RichTextBox` + `ScrollBars.None` 并关掉 `BorderStyle`）；
   有滚动条的控件记得 `DarkMode.ApplyDeferred(控件, 4)`（滚动条是延迟创建的）。改完界面必须跑 `--theme-audit`。
7. **卡片高度按内容算**。`Card` 是 `Dock=Top` 堆叠，高度写小了内容会被挤到卡片外
   （第一版 ① 卡片 150px 就把信息行挤出去了）；工具栏会换行时高度要留够两行。
8. **树自绘不要盖住展开箭头**：`DrawNode` 里背景矩形必须从 `e.Bounds.Left` 开始，
   从 0 开始会把 `+/-` 一起涂掉。

---

## 5. 验证：怎么复现全部结论

### 5.1 编译

    # 仓库自带脚本（本机执行策略禁止直接跑 .ps1，必须显式绕过）
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

    # 或手动调 csc（与 build.ps1 参数完全一致）
    $csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $ref='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
    $src = Get-ChildItem src\*.cs | ForEach-Object { $_.FullName }
    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /langversion:5 \
        /out:release\MCPackStudio.exe \
        /reference:$ref\System.dll /reference:$ref\System.Core.dll /reference:$ref\System.Drawing.dll \
        /reference:$ref\System.Windows.Forms.dll /reference:$ref\System.IO.Compression.dll \
        /reference:$ref\System.IO.Compression.FileSystem.dll $src

预期：`exit=0`，只有 8 条改动前就存在的警告（IconPage / Converter / MigrationRules / PackMerge / PackMetaPage / Pages）。

### 5.2 自测

    # 先把临时目录指到可写位置（原因见 6.2），否则会在入口就崩
    New-Item -ItemType Directory -Force X:\temp | Out-Null
    cmd /c "set TEMP=X:\temp&& set TMP=X:\temp&& cd /d %CD%\release&& MCPackStudio.exe --selftest selftest.txt"
    Get-Content release\selftest.txt -Encoding UTF8 | Select-String "通过 "

预期：`通过 314 项，失败 0 项`（第 15、16 节是本次新增的 NBT 部分）。

### 5.3 截图 + 深色主题漏白自检

    .\release\MCPackStudio.exe --shots tools\out\pages     # 15 张页面截图，NBT 两页会自动预载 testnbt 样本
    .\release\MCPackStudio.exe --theme-audit tools\out\pages

预期：NBT 两页（`08-nbt-edit.png`、`09-nbt-snbt.png`）显示 `OK`。
注意 `--theme-audit` 的判定是「够亮 + 够灰 + 横向连续 ≥40px」，**只能发现横向白带**；
右侧那种 17px 宽的白色竖滚动条要靠眼睛看（这次就是先发现横条、再顺手改掉竖条的）。

### 5.4 本轮实测结果

| 项目 | 结果 |
| --- | --- |
| 全量编译（39 个源文件） | `exit=0`，无新增警告 |
| `--selftest` | **通过 314 / 失败 0** |
| `--theme-audit` | NBT 两页 OK；唯一 `!!` 是 `11-settings-about.png`（上游遗留，见 7.1） |
| 独立交叉验证 | 另写 harness 覆盖核心 68 项 + 二进制 IO 26 项；子代理那份 139 项（含手工已知字节逐字节对拍、1062 种截断输入、zlib Adler-32 独立校验） |

---

## 6. 环境注意事项（会踩的坑）

### 6.1 执行策略禁止跑脚本

`build.ps1` 直接执行会报 `UnauthorizedAccess`。
绕过：`powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。

### 6.2 临时目录与盘根不可写（会误导你以为代码坏了）

- `%TEMP%` 在本机沙箱下**不可写**，`Directory.CreateDirectory` 抛 `UnauthorizedAccessException`；
  而 `SelfTest.Run` 建临时目录那行**没包 try**，于是整个自测在入口崩掉，只留一句
  「由于 Exception.ToString() 失败，因此无法打印异常字符串」—— 很难看出真因。
  **跑自测前先把 TEMP/TMP 指到可写目录。**
- 版本转换的暂存目录建在**盘根**（`D:\.mcpack_build_<hex>`，为了同卷 `File.Move` 原子替换），
  沙箱不给写盘根时自测第 4~9 节会失败。技巧：`subst X: <可写目录>`，再把 TEMP 指到 `X:\temp`。

### 6.3 控制台中文乱码

`cmd /c` 抓到的输出是 GBK，中文会花。要么先设 `[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)`，
要么直接读 `selftest.txt`（UTF-8）。

### 6.4 GUI 常驻会让命令不返回

外部 `Start-Process` 拉起界面并保持运行，命令会挂到超时。要自动截图就用程序自带的 `--shots`
（它截完自己退出），不要外部拉起再截。

### 6.5 PowerShell 5.1 的 `>` 会写成 UTF-16（导补丁时踩过）

用 `git diff ... > x.patch` 生成补丁会得到一个 **UTF-16** 文件，`git apply` 会报
「No valid patches in input」。要么走 cmd 的重定向（按字节写）：

    cmd /c "git diff --binary origin/main...HEAD > out.patch"

要么用 `.NET` 显式写 UTF-8：

    [System.IO.File]::WriteAllText($p, (git diff --binary origin/main...HEAD | Out-String), (New-Object System.Text.UTF8Encoding($false)))

另外导补丁要带 `--binary`，否则 `testnbt` 里的两个样本文件不会进补丁。

---

## 7. 已知问题与后续建议

### 7.1 上游遗留（不是我引入的，已交叉验证）

**`11-settings-about` 页有一条白线 + 白色竖滚动条**：DataGridView（`ReportGrid`）顶部 1px 白线
（y=240，x=45..1194，共 1149 白像素）与右侧滚动条。
证据：把未改动的 `origin/main` 用 `git worktree` 单独检出编译并审计，**白像素数一模一样**。
`Widgets.cs` 里 `ReportGrid` 已有 `Padding=(0,1,0,0)` 与 `OnPaint` 裁边处理，但白线仍可见，
说明它多半不是 `OnPaint` 画的（来自子窗口），顺着 `DarkMode.ApplyThemeToTree` 查更快。

### 7.2 本次实现的取舍

| 项 | 说明 | 建议 |
| --- | --- | --- |
| zlib 读不校验 Adler-32 | 怕遇到「zlib 流后面还有尾巴」的文件误报，只信 `DeflateStream` 的报错 | 需要严格校验可加开关 |
| 「读干净」容差 4 字节 | 没有带尾部填充的真实样本可测，是估的 | 遇到实例再调 |
| 大文件关闭撤销 | 节点数 > 40 万（`NbtHistory.MaxNodes`）不做快照，界面会说明 | 需要的话改命令式撤销 |
| 空列表元素类型 | 读进来保留、写回去变 `TAG_End`（规范如此） | 无需改，知道即可 |
| `--shots` 步骤编号有重号 | 合并上游后 `10-browse` 与 `10-settings-rules` 并存（文件名不冲突） | 有空重排序号 |
| `SelfTest` 建临时目录未包 try | 见 6.2，受限环境下报错不可读 | 加 try + 明确提示「临时目录不可写」 |

---

## 8. 怎么继续开发

**改已有页面/弹窗**：页面只做搬运，不要在页面里碰字节。要新的编辑动作，先在 `NbtEdit.cs` 加纯逻辑
（输入标签、输出结果），再在 `NbtPage` 接一个按钮 —— 这样逻辑能直接进自测。

**加一个 NBT 子模块**（例如「批量改 NBT」）：

1. 写一个类实现 `IModulePage`（照抄 `NbtSnbtPage`，它最短）
2. 在 `src/Modules.cs` 的 `nbt.Subs` 里 `Add` 一条 `SubModule`
3. 导航、面包屑、状态栏、位置记忆都会自动生效，外壳代码不用动

**加断言**：`SelfTest.Run` 里加 `Section("17. …")` 与一个 `TestXxx()`，用 `Check(名称, 条件, 详情)` /
`Eq(名称, 实际, 期望)`；造树直接抄 `SelfTest.MakeSampleNbt()`（覆盖全部 13 种类型）。

**加测试样本**：`testnbt/` 里的样本是「构造树 → `NbtIo.Save` → `NbtIo.Load` → `ValueEquals` 断言」生成的，
照这个套路写个临时 exe 即可（不必进仓库）。

---

## 9. 验收清单

- [ ] `git log --oneline -3` 能看到三个提交，HEAD = `5fb864f`
- [ ] 按 5.1 编译：`exit=0`，无新增警告
- [ ] 按 5.2 自测：**314 项全通过**
- [ ] 按 5.3 截图与漏白自检：NBT 两页 OK
- [ ] 界面顶部导航多出「NBT」，主页多出一张 NBT 卡片（2 个子模块）
- [ ] `--nbt testnbt\sample_java_level.dat` 直接进编辑器，能看到 45 个标签的树
- [ ] 改一个值 → 状态栏出现「有未保存的修改」→ 撤销能还原 → 另存为写出新文件 → 重新打开内容一致
- [ ] 打开 `testnbt\sample_bedrock_level.dat`：信息行显示「无压缩 · 小端 · 基岩版头」
- [ ] 「SNBT 与 JSON」页：打开 Java 样本 → 文本出现 → 校验通过 → 存为 `.dat` → 再打开内容一致

---

## 附录 A：文件职责一览（改动时按图索骥）

| 想改什么 | 去哪儿 |
| --- | --- |
| 标签类型、值格式、深拷贝/比较 | `NbtTag.cs` |
| 读写字节、压缩、字节序、基岩版头 | `NbtIo.cs` |
| SNBT 语法、JSON 导出 | `NbtSnbt.cs` |
| 查找规则、撤销栈、粘贴/移动规则 | `NbtEdit.cs` |
| 树的外观、工具栏、快捷键、状态刷新 | `NbtPage.cs` |
| 弹窗字段与校验 | `NbtDialogs.cs` |
| 模块注册、子模块标题与提示 | `Modules.cs` |
| 自测断言 | `SelfTest.cs`（第 15、16 节） |

## 附录 B：本轮用到的关键命令

    # 1) 拿仓库并建分支（GitHub 凭据已通过 Git Credential Manager 登录过）
    git clone https://github.com/acgmnc/mcpack-studio
    git checkout -b feat/nbt-editor

    # 2) 编译 + 自测 + 截图 + 漏白自检
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
    cmd /c "set TEMP=X:\temp&& set TMP=X:\temp&& cd /d %CD%\release&& MCPackStudio.exe --selftest selftest.txt"
    .\release\MCPackStudio.exe --shots tools\out\pages
    .\release\MCPackStudio.exe --theme-audit tools\out\pages

    # 3) 验证「上游是不是也有同样问题」（本次就是这样确认 7.1 的）
    git worktree add --detach ..\_scratch\main-check origin/main

    # 4) 提交推送
    git add -A && git commit -F msg.txt && git push -u origin feat/nbt-editor
