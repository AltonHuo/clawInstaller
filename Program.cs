using System;
using System.Linq;
using System.Windows.Forms;

namespace OpenClawInstaller
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationConfiguration.Initialize();

            // 支持三种模式启动:
            //   默认 (无参数):  Launcher 模式 — 直接启动 Gateway + WebView2
            //   --install:      Installer 模式 — 原有的在线安装器界面
            //   --uninstall:    Uninstall 模式 — 一键卸载界面
            bool isInstallerMode = args.Any(a =>
                a.Equals("--install", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-i", StringComparison.OrdinalIgnoreCase));

            bool isUninstallMode = args.Any(a =>
                a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-u", StringComparison.OrdinalIgnoreCase));

            if (isUninstallMode)
            {
                Application.Run(new UninstallForm());
            }
            else if (isInstallerMode)
            {
                Application.Run(new MainForm());
            }
            else
            {
                Application.Run(new LauncherForm());
            }
        }
    }
}
