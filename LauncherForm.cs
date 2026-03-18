using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace OpenClawInstaller
{
    /// <summary>
    /// Launcher 模式的主窗口: 嵌入 WebView2 显示 LobeChat, 顶部工具栏管理配置。
    /// </summary>
    public class LauncherForm : Form
    {
        private WebView2 webView = null!;
        private ToolStrip toolbar = null!;
        private StatusStrip statusBar = null!;
        private ToolStripStatusLabel statusLabel = null!;
        private ToolStripProgressBar statusProgress = null!;
        private NotifyIcon trayIcon = null!;

        private GatewayManager? gatewayManager;
        private ConfigManager configManager = null!;
        private readonly string baseDir;
        private CancellationTokenSource? cts;

        // 日志面板 (可折叠)
        private RichTextBox logPanel = null!;
        private SplitContainer splitContainer = null!;
        private bool logPanelVisible = false;

        public LauncherForm()
        {
            baseDir = AppContext.BaseDirectory;
            configManager = new ConfigManager(baseDir);

            Text = "🦞 OpenClaw";
            Size = new Size(1200, 800);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            Icon = LoadAppIcon();

            SetupUI();
            SetupTrayIcon();

            Load += async (s, e) => await StartupSequenceAsync();
            FormClosing += OnFormClosing;
        }

        private Icon? LoadAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }

        private void SetupUI()
        {
            // ============ 工具栏 ============
            toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Padding = new Padding(8, 0, 8, 0),
                RenderMode = ToolStripRenderMode.Professional,
                Renderer = new DarkToolStripRenderer()
            };

            var brandLabel = new ToolStripLabel("🦞 OpenClaw")
            {
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                Padding = new Padding(0, 0, 20, 0)
            };

            var restartBtn = CreateToolButton("🔄 重启服务", async (s, e) => await RestartGatewayAsync());
            var onboardBtn = CreateToolButton("📋 Onboard 向导", (s, e) => RunOnboardInTerminal());
            var logBtn = CreateToolButton("📝 日志", (s, e) => ToggleLogPanel());
            var settingsBtn = CreateToolButton("⚙️ 设置", (s, e) => configManager.ShowSetupDialog());

            toolbar.Items.AddRange(new ToolStripItem[]
            {
                brandLabel,
                new ToolStripSeparator(),
                restartBtn,
                onboardBtn,
                new ToolStripSeparator(),
                logBtn,
                settingsBtn
            });

            // ============ 状态栏 ============
            statusBar = new StatusStrip { BackColor = Color.FromArgb(0, 102, 204) };
            statusLabel = new ToolStripStatusLabel("正在启动...")
            {
                ForeColor = Color.White,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusProgress = new ToolStripProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Visible = true
            };
            statusBar.Items.AddRange(new ToolStripItem[] { statusLabel, statusProgress });

            // ============ WebView2 ============
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(20, 20, 20)
            };

            // ============ 日志面板 ============
            logPanel = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 210, 106),
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };

            // ============ SplitContainer ============
            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel2Collapsed = true, // 日志面板默认隐藏
                SplitterDistance = 500,
                BackColor = Color.FromArgb(40, 40, 40)
            };
            splitContainer.Panel1.Controls.Add(webView);
            splitContainer.Panel2.Controls.Add(logPanel);

            // ============ 组装 ============
            Controls.Add(splitContainer);
            Controls.Add(toolbar);
            Controls.Add(statusBar);
        }

        private void SetupTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "OpenClaw",
                Visible = false
            };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示窗口", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("退出", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        // ====================================================
        // 启动流程
        // ====================================================

        private async Task StartupSequenceAsync()
        {
            cts = new CancellationTokenSource();

            try
            {
                // 1. 检查运行时
                SetStatus("正在检查运行时环境...", true);
                if (!configManager.CheckRuntimeIntegrity(out string error))
                {
                    MessageBox.Show(error, "环境错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                    return;
                }

                // 2. 首次运行配置
                if (configManager.IsFirstRun())
                {
                    SetStatus("等待首次配置...", false);
                    AppendLog("[系统] 检测到首次运行, 弹出配置向导...");

                    // 首次运行需跑 onboard, 提示用户
                    var result = MessageBox.Show(
                        "检测到首次运行 OpenClaw。\n\n需要先运行 Onboard 向导进行初始化配置（设置 API Key 等）。\n\n点击"确定"打开终端运行向导。",
                        "首次配置",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.OK)
                    {
                        RunOnboardInTerminal();
                        MessageBox.Show(
                            "请在弹出的终端中完成 Onboard 配置。\n完成后点击"确定"继续启动 Gateway。",
                            "等待配置完成",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }

                // 3. 启动 Gateway
                SetStatus("正在启动 Gateway...", true);
                AppendLog("[系统] 正在启动 OpenClaw Gateway...");

                int port = configManager.GetGatewayPort();
                gatewayManager = new GatewayManager(baseDir, port);
                gatewayManager.OnOutput += msg => BeginInvoke(() => AppendLog($"[Gateway] {msg}"));
                gatewayManager.OnError += msg => BeginInvoke(() => AppendLog($"[Gateway ERR] {msg}"));
                gatewayManager.OnExited += code => BeginInvoke(() =>
                {
                    AppendLog($"[系统] Gateway 进程已退出, 退出码: {code}");
                    SetStatus("Gateway 已停止", false);
                });

                gatewayManager.Start();
                AppendLog("[系统] Gateway 进程已启动, 等待端口就绪...");

                // 4. 等待就绪
                bool ready = await gatewayManager.WaitForReadyAsync(60, cts.Token);
                if (!ready)
                {
                    AppendLog("[错误] Gateway 启动超时! 请检查日志。");
                    SetStatus("Gateway 启动超时", false);
                    ToggleLogPanel(); // 自动展开日志面板
                    return;
                }

                AppendLog("[系统] Gateway 已就绪!");

                // 5. 加载 WebView2
                SetStatus("正在加载界面...", true);
                await InitWebViewAsync(port);

                SetStatus($"✅ 运行中 — localhost:{port}", false);
            }
            catch (Exception ex)
            {
                AppendLog($"[严重错误] {ex.Message}");
                SetStatus("启动失败", false);
                MessageBox.Show($"启动失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task InitWebViewAsync(int port)
        {
            try
            {
                // 设置 WebView2 用户数据目录到 data 文件夹
                string webviewDataDir = Path.Combine(baseDir, "data", "webview2_data");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: webviewDataDir);

                await webView.EnsureCoreWebView2Async(env);
                webView.Source = new Uri($"http://localhost:{port}");

                AppendLog($"[系统] WebView2 已加载 http://localhost:{port}");
            }
            catch (Exception ex)
            {
                AppendLog($"[WebView2 错误] {ex.Message}");
                AppendLog("[系统] 将使用系统浏览器作为备用方案...");

                // 降级: 使用系统默认浏览器打开
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"http://localhost:{port}",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        // ====================================================
        // 工具栏操作
        // ====================================================

        private async Task RestartGatewayAsync()
        {
            SetStatus("正在重启 Gateway...", true);
            AppendLog("[系统] 正在重启 Gateway...");

            gatewayManager?.Stop();
            await Task.Delay(1000);

            int port = configManager.GetGatewayPort();
            gatewayManager = new GatewayManager(baseDir, port);
            gatewayManager.OnOutput += msg => BeginInvoke(() => AppendLog($"[Gateway] {msg}"));
            gatewayManager.OnError += msg => BeginInvoke(() => AppendLog($"[Gateway ERR] {msg}"));
            gatewayManager.OnExited += code => BeginInvoke(() =>
            {
                AppendLog($"[系统] Gateway 进程已退出, 退出码: {code}");
                SetStatus("Gateway 已停止", false);
            });

            gatewayManager.Start();

            bool ready = await gatewayManager.WaitForReadyAsync(60);
            if (ready)
            {
                webView.Reload();
                SetStatus($"✅ 运行中 — localhost:{port}", false);
                AppendLog("[系统] Gateway 重启成功!");
            }
            else
            {
                SetStatus("重启失败", false);
                AppendLog("[错误] Gateway 重启超时!");
            }
        }

        private void RunOnboardInTerminal()
        {
            string runtimeDir = Path.Combine(baseDir, "runtime");
            string nodejsDir = Path.Combine(runtimeDir, "nodejs");
            string gitCmdDir = Path.Combine(runtimeDir, "git_env", "cmd");
            string appDir = Path.Combine(runtimeDir, "openclaw_app");
            string dataDir = Path.Combine(baseDir, "data");

            // 构建 PATH
            string pathEnv = $"{nodejsDir};{gitCmdDir};{Path.Combine(appDir, "node_modules", ".bin")};%PATH%";

            // 构建环境变量设置命令
            string envSetup = $"set \"PATH={pathEnv}\" && set \"USERPROFILE={dataDir}\" && set \"HOME={dataDir}\" && set \"APPDATA={Path.Combine(dataDir, "AppData", "Roaming")}\" && set \"LOCALAPPDATA={Path.Combine(dataDir, "AppData", "Local")}\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"{envSetup} && cd /d \"{appDir}\" && npx openclaw onboard\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            System.Diagnostics.Process.Start(psi);
            AppendLog("[系统] 已打开 Onboard 向导终端。");
        }

        private void ToggleLogPanel()
        {
            logPanelVisible = !logPanelVisible;
            splitContainer.Panel2Collapsed = !logPanelVisible;
            if (logPanelVisible)
            {
                splitContainer.SplitterDistance = (int)(splitContainer.Height * 0.65);
            }
        }

        // ====================================================
        // 辅助方法
        // ====================================================

        private ToolStripButton CreateToolButton(string text, EventHandler onClick)
        {
            var btn = new ToolStripButton(text)
            {
                ForeColor = Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btn.Click += onClick;
            return btn;
        }

        private ToolStripButton CreateToolButton(string text, Func<object?, EventArgs, Task> onClickAsync)
        {
            var btn = new ToolStripButton(text)
            {
                ForeColor = Color.White,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btn.Click += async (s, e) => await onClickAsync(s, e);
            return btn;
        }

        private void SetStatus(string text, bool showProgress)
        {
            statusLabel.Text = text;
            statusProgress.Visible = showProgress;
        }

        private void AppendLog(string message)
        {
            if (logPanel.InvokeRequired)
            {
                logPanel.BeginInvoke(() => AppendLog(message));
                return;
            }

            logPanel.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            logPanel.ScrollToCaret();
        }

        // ====================================================
        // 窗口关闭 / 托盘
        // ====================================================

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            // 最小化到托盘
            if (e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show(
                    "关闭窗口将同时停止 Gateway 服务。\n\n确定要退出吗？",
                    "退出确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // 清理资源
            cts?.Cancel();
            gatewayManager?.Dispose();
            trayIcon?.Dispose();
        }

        private void RestoreFromTray()
        {
            trayIcon.Visible = false;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
    }

    /// <summary>
    /// 暗色工具栏渲染器。
    /// </summary>
    internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Color.FromArgb(30, 30, 30));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Color.FromArgb(60, 60, 60));
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using var brush = new SolidBrush(Color.FromArgb(60, 60, 60));
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }
    }
}
