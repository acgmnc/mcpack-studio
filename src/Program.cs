using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace McPackStudio
{
    internal static class Program
    {
        /// <summary>调试用：--layout &lt;文件&gt; 会把控件布局写到这个文件</summary>
        public static string LayoutDumpFile;
        /// <summary>调试用：--shot-sizes &lt;文件&gt; 会把几个窗口尺寸的布局快照写到这个文件</summary>
        public static string SizeProbeFile;
        /// <summary>调试用：--shots &lt;目录&gt; 会把每个页面的截图写进这个目录</summary>
        public static string ShotDir;
        /// <summary>--nbt &lt;文件&gt; 会把界面直接开到 NBT 编辑器并载入这个文件</summary>
        public static string NbtPreloadFile;
        /// <summary>调试用：--theme-test &lt;目录&gt; 会实测几个候选深色主题名</summary>
        public static string ThemeTestDir;
        /// <summary>调试用：--select-test &lt;目录&gt; 单独验证 FlatSelect 的展开与选择</summary>
        public static string SelectTestDir;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        private static void Main(string[] args)
        {
            bool consoleMode = args != null && args.Length > 0
                && (args[0] == "--convert" || args[0] == "--selftest" || args[0] == "--help" ||
                    args[0] == "--theme-audit");

            if (consoleMode)
            {
                // winexe 默认没有控制台，这里挂到调用方的控制台上，
                // 这样在 cmd / PowerShell 里跑就能直接看到输出。
                bool attached = false;
                try
                {
                    attached = AttachConsole(ATTACH_PARENT_PROCESS);
                    if (!attached) { AllocConsole(); attached = true; }
                    Console.OutputEncoding = new UTF8Encoding(false);
                }
                catch { }

                int code;
                if (args[0] == "--help")
                {
                    PrintHelp();
                    code = 0;
                }
                else if (args[0] == "--selftest")
                {
                    string outFile = args.Length > 1 ? args[1] : null;
                    code = SelfTest.Run(outFile);
                    Console.WriteLine(code == 0 ? "自测通过" : "自测存在失败项");
                }
                else if (args[0] == "--theme-audit")
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("用法: MCPackStudio.exe --theme-audit <截图目录>");
                        code = 2;
                    }
                    else
                    {
                        string report = ThemeAudit.Scan(args[1]);
                        Console.WriteLine("深色主题漏白自检（亮度 200 且 RGB 接近、连续 40px 以上才算）");
                        Console.WriteLine();
                        Console.Write(report);
                        code = report.Contains("!!") ? 1 : 0;
                    }
                }
                else
                {
                    code = Cli.Convert(args);
                }

                try { if (attached) FreeConsole(); } catch { }
                Environment.Exit(code);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            /*
             * 滚轮路由：鼠标停在列表/文本框这类「自己能滚」的子控件上时，
             * Windows 会把滚轮消息直接发给那个子控件，父滚动容器收不到，
             * 表现为「鼠标移到表格上滚轮就没反应」。
             * 挂个全局过滤器，把没被消耗的滚轮转给最近的滚动容器。
             */
            Application.AddMessageFilter(new WheelRouter());

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show(
                    "程序遇到了未处理的错误：\n\n" + e.Exception.Message + "\n\n" + e.Exception.StackTrace,
                    Settings.AppName + " 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    "程序遇到了严重错误：\n\n" + (ex != null ? ex.Message : "未知错误"),
                    Settings.AppName + " 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // 第一个裸参数（非 -- 开头）当作要预加载的源资源包
            string preload = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--layout" && i + 1 < args.Length)
                    {
                        LayoutDumpFile = args[i + 1];
                        i++;
                        continue;
                    }
                    if (args[i] == "--shot-sizes" && i + 1 < args.Length)
                    {
                        SizeProbeFile = args[i + 1];
                        i++;
                        continue;
                    }
                    if (args[i] == "--shots" && i + 1 < args.Length)
                    {
                        ShotDir = args[i + 1];
                        i++;
                        continue;
                    }
                    if (args[i] == "--nbt" && i + 1 < args.Length)
                    {
                        NbtPreloadFile = args[i + 1];
                        i++;
                        continue;
                    }
                    if (args[i] == "--theme-test" && i + 1 < args.Length)
                    {
                        ThemeTestDir = args[i + 1];
                        i++;
                        continue;
                    }
                    if (args[i] == "--select-test" && i + 1 < args.Length)
                    {
                        SelectTestDir = args[i + 1];
                        i++;
                        continue;
                    }
                    if (!string.IsNullOrEmpty(args[i]) && !args[i].StartsWith("--", StringComparison.Ordinal) && preload == null)
                    {
                        preload = args[i];
                    }
                }
            }

            var shell = new ShellForm();
            if (!string.IsNullOrEmpty(preload))
            {
                string captured = preload;
                shell.Shown += (s, e) => shell.OpenSourceFile(captured);
            }

            if (!string.IsNullOrEmpty(NbtPreloadFile))
            {
                string capturedNbt = NbtPreloadFile;
                shell.Shown += (s, e) => shell.OpenNbtFile(capturedNbt);
            }

            if (!string.IsNullOrEmpty(SizeProbeFile))
            {
                string captured = SizeProbeFile;
                shell.Shown += (s, e) =>
                {
                    shell.RunSizeProbe(captured);
                    shell.Close();
                };
            }

            if (!string.IsNullOrEmpty(ShotDir))
            {
                string captured = ShotDir;
                shell.Shown += (s, e) =>
                {
                    shell.RunShotSequence(captured);
                    shell.Close();
                };
            }

            if (!string.IsNullOrEmpty(ThemeTestDir))
            {
                string captured = ThemeTestDir;
                shell.Shown += (s, e) =>
                {
                    shell.RunThemeTest(captured);
                    shell.Close();
                };
            }

            if (!string.IsNullOrEmpty(SelectTestDir))
            {
                string captured = SelectTestDir;
                shell.Shown += (s, e) =>
                {
                    shell.RunSelectTest(captured);
                    shell.Close();
                };
            }

            Application.Run(shell);
        }

        private static void PrintHelp()
        {
            Console.WriteLine(Settings.AppName + " " + Settings.AppVersion + "  ·  " + Settings.AppTagline);
            Console.WriteLine();
            Console.WriteLine("不带参数运行会打开图形界面。");
            Console.WriteLine();
            Console.WriteLine("命令行用法：");
            Console.WriteLine("  MCPackStudio.exe --convert <源资源包> --to <版本> --out <输出> [选项]");
            Console.WriteLine();
            Console.WriteLine("  --convert <路径>     源资源包（.zip 或包含 pack.mcmeta 的文件夹）");
            Console.WriteLine("  --to <版本>          目标版本，可填 Minecraft 版本号（1.20.1）或 pack_format（15）");
            Console.WriteLine("  --out <路径>         输出位置（文件夹，或配合 --zip 为 .zip 路径）");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Console.WriteLine("  --zip                输出为 ZIP");
            Console.WriteLine("  --meta-only          只改 pack.mcmeta 的版本号，不迁移任何资源文件");
            Console.WriteLine("  --no-lang            迁移语言文件路径但不改写键名");
            Console.WriteLine("  --exclude <相对路径> 排除某个文件（可重复使用）");
            Console.WriteLine("  --analyze            只分析源资源包并打印信息，不转换");
            Console.WriteLine("  --report <文件>      把完整日志写入文件");
            Console.WriteLine("  --quiet              少输出");
            Console.WriteLine();
            Console.WriteLine("示例：");
            Console.WriteLine("  MCPackStudio.exe --convert MyPack --to 1.20.1 --out MyPack_1.20.1");
            Console.WriteLine("  MCPackStudio.exe --convert MyPack.zip --to 46 --out New.zip --zip");
            Console.WriteLine("  MCPackStudio.exe --analyze MyPack");
            Console.WriteLine();
            Console.WriteLine("  MCPackStudio.exe --nbt <文件>            打开界面并直接载入这个 NBT 文件");
            Console.WriteLine();
            Console.WriteLine("  MCPackStudio.exe --selftest [输出文件]   运行内置自测");
            Console.WriteLine("  MCPackStudio.exe --theme-audit <目录>     扫描截图里的过亮区域（深色主题自检）");
            Console.WriteLine();
            Console.WriteLine("界面排版调试：");
            Console.WriteLine("  MCPackStudio.exe --layout <文件>          导出当前控件布局");
            Console.WriteLine("  MCPackStudio.exe --shot-sizes <文件>      导出几种窗口尺寸下的布局");
            Console.WriteLine("  MCPackStudio.exe --shots <目录>           依次给每个页面截图");
        }
    }
}
