using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenClawInstaller
{
    /// <summary>
    /// 一键卸载界面，与安装器 MainForm 风格一致。
    /// </summary>
    public class UninstallForm : Form
    {
        private TextBox pathInput;
        private CheckBox deleteDataCheck;
        private Button uninstallBtn;
        private ProgressBar progressBar;
        private RichTextBox console;

        /// <summary>
        /// 创建卸载窗口。
        /// </summary>
        /// <param name="prefilledPath">预填的安装目录路径 (可选)</param>
        public UninstallForm(string prefilledPath = null)
        {
            Text = "OpenClaw 卸载工具";
            Size = new Size(640, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(248, 249, 250);

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            SetupUI(prefilledPath);
        }

        private void SetupUI(string prefilledPath)
        {
            // 顶部标题栏 — 红色系表示卸载操作
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(204, 50, 50)
            };
            Label titleLabel = new Label
            {
                Text = "OpenClaw 卸载工具",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 18F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 15)
            };
            Label subtitleLabel = new Label
            {
                Text = "将删除所有已安装的 OpenClaw 组件",
                ForeColor = Color.FromArgb(255, 200, 200),
                Font = new Font("Microsoft YaHei", 9.5F),
                AutoSize = true,
                Location = new Point(22, 50)
            };
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            Controls.Add(headerPanel);

            int yOffset = 110;

            // 安装目录选择
            Label pathLabel = new Label
            {
                Text = "安装目录:",
                Location = new Point(20, yOffset + 5),
                AutoSize = true,
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            pathInput = new TextBox
            {
                Location = new Point(140, yOffset),
                Width = 360,
                PlaceholderText = "请选择 OpenClaw 的安装路径...",
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei", 10F)
            };
            if (!string.IsNullOrEmpty(prefilledPath))
            {
                pathInput.Text = prefilledPath;
                pathInput.ReadOnly = true;
            }

            Button browseBtn = new Button
            {
                Text = "浏览...",
                Location = new Point(510, yOffset - 2),
                Width = 80,
                Height = 28,
                Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(224, 224, 224),
                FlatStyle = FlatStyle.Flat
            };
            browseBtn.FlatAppearance.BorderSize = 0;
            browseBtn.Click += (s, e) =>
            {
                using var dialog = new FolderBrowserDialog { Description = "选择 OpenClaw 安装目录" };
                if (dialog.ShowDialog() == DialogResult.OK) pathInput.Text = dialog.SelectedPath;
            };

            yOffset += 50;

            // 选项
            deleteDataCheck = new CheckBox
            {
                Text = "同时删除用户数据 (data/ 目录，包含配置和 API Key)",
                Location = new Point(25, yOffset),
                AutoSize = true,
                Cursor = Cursors.Hand,
                ForeColor = Color.DimGray,
                Checked = true
            };

            yOffset += 50;

            // 一键卸载按钮
            uninstallBtn = new Button
            {
                Text = "🗑️ 一键卸载",
                Location = new Point(25, yOffset),
                Width = 565,
                Height = 45,
                Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(204, 50, 50),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            uninstallBtn.FlatAppearance.BorderSize = 0;
            uninstallBtn.Click += async (s, e) => await StartUninstall();

            yOffset += 65;

            // 进度条
            progressBar = new ProgressBar
            {
                Location = new Point(25, yOffset),
                Width = 565,
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };

            yOffset += 25;

            // 日志面板
            Label logLabel = new Label
            {
                Text = "卸载日志:",
                Location = new Point(25, yOffset),
                AutoSize = true,
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            console = new RichTextBox
            {
                Location = new Point(25, yOffset + 25),
                Width = 565,
                Height = 170,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 210, 106),
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.None
            };

            Controls.AddRange(new Control[]
            {
                pathLabel, pathInput, browseBtn,
                deleteDataCheck,
                uninstallBtn,
                progressBar,
                logLabel, console
            });
        }

        private async Task StartUninstall()
        {
            string installDir = pathInput.Text.Trim();

            if (string.IsNullOrEmpty(installDir))
            {
                MessageBox.Show("请先选择安装目录！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(installDir))
            {
                MessageBox.Show("指定的安装目录不存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 简单校验: 检查目录下是否有 OpenClaw 的标志性文件/目录
            bool looksLikeInstallDir =
                Directory.Exists(Path.Combine(installDir, "nodejs")) ||
                Directory.Exists(Path.Combine(installDir, "openclaw_app")) ||
                Directory.Exists(Path.Combine(installDir, "runtime")) ||
                File.Exists(Path.Combine(installDir, "start.ps1"));

            if (!looksLikeInstallDir)
            {
                var checkResult = MessageBox.Show(
                    "该目录看起来不像 OpenClaw 安装目录。\n\n确定要继续卸载吗？",
                    "警告",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (checkResult == DialogResult.No) return;
            }

            // 二次确认
            string dataWarning = deleteDataCheck.Checked
                ? "\n\n⚠️ 用户数据 (data/ 目录) 也将被删除！"
                : "";

            var confirmResult = MessageBox.Show(
                $"确定要卸载以下目录中的 OpenClaw？\n\n{installDir}{dataWarning}",
                "确认卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmResult == DialogResult.No) return;

            // 开始卸载
            uninstallBtn.Enabled = false;
            uninstallBtn.Text = "卸载中，请稍候...";
            uninstallBtn.BackColor = Color.Gray;
            console.Clear();
            progressBar.Value = 0;

            var progress = new Progress<int>(percent => progressBar.Value = percent);
            var logger = new Progress<string>(msg =>
            {
                console.AppendText(msg + Environment.NewLine);
                console.ScrollToCaret();
            });

            try
            {
                var worker = new UninstallWorker(installDir, deleteDataCheck.Checked);
                await worker.RunAsync(progress, logger);

                MessageBox.Show(
                    "OpenClaw 卸载完成！\n\n所有组件已被成功移除。",
                    "卸载完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ((IProgress<string>)logger).Report($"\n[严重错误] 卸载中断: {ex.Message}");
                MessageBox.Show($"卸载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                uninstallBtn.Enabled = true;
                uninstallBtn.Text = "🗑️ 一键卸载";
                uninstallBtn.BackColor = Color.FromArgb(204, 50, 50);
            }
        }
    }
}
