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
                && (args[0] == "--convert" || args[0] == "--selftest" || args[0] == "--help");

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
            Console.WriteLine("  MCPackStudio.exe --selftest [输出文件]   运行内置自测");
        }
    }
}
