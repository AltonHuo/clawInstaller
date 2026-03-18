using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OpenClawInstaller
{
    /// <summary>
    /// 管理 OpenClaw 配置，提供首次运行向导和配置读写。
    /// </summary>
    public class ConfigManager
    {
        private readonly string baseDir;
        private readonly string dataDir;
        private readonly string openclawConfigDir;

        public ConfigManager(string baseDir)
        {
            this.baseDir = Path.GetFullPath(baseDir);
            this.dataDir = Path.Combine(this.baseDir, "data");
            this.openclawConfigDir = Path.Combine(dataDir, ".openclaw");
        }

        /// <summary>
        /// 检测是否为首次运行 (没有 gateway.cmd 说明未执行过 onboard)。
        /// </summary>
        public bool IsFirstRun()
        {
            return !File.Exists(Path.Combine(openclawConfigDir, "gateway.cmd"))
                && !File.Exists(Path.Combine(dataDir, "gateway.cmd"));
        }

        /// <summary>
        /// 检查运行时环境是否完整。
        /// </summary>
        public bool CheckRuntimeIntegrity(out string errorMessage)
        {
            string runtimeDir = Path.Combine(baseDir, "runtime");
            string nodeExe = Path.Combine(runtimeDir, "nodejs", "node.exe");
            string appDir = Path.Combine(runtimeDir, "openclaw_app");
            string packageJson = Path.Combine(appDir, "package.json");

            if (!File.Exists(nodeExe))
            {
                errorMessage = $"找不到 Node.js 运行时:\n{nodeExe}\n\n请确保解压了完整的安装包。";
                return false;
            }

            if (!File.Exists(packageJson))
            {
                errorMessage = $"找不到 OpenClaw 应用:\n{appDir}\n\n请确保解压了完整的安装包。";
                return false;
            }

            errorMessage = "";
            return true;
        }

        /// <summary>
        /// 显示首次配置对话框，返回用户是否确认配置。
        /// </summary>
        public bool ShowSetupDialog()
        {
            using var dialog = new SetupDialog();
            var result = dialog.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.ApiKey))
            {
                // 确保配置目录存在
                Directory.CreateDirectory(openclawConfigDir);
                Directory.CreateDirectory(dataDir);

                // 写入环境变量配置文件 (.env 格式)
                var envPath = Path.Combine(openclawConfigDir, ".env");
                var sb = new StringBuilder();
                sb.AppendLine($"# OpenClaw 配置 - 由 Launcher 自动生成");
                sb.AppendLine($"# 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                switch (dialog.SelectedProvider)
                {
                    case "OpenAI":
                        sb.AppendLine($"OPENAI_API_KEY={dialog.ApiKey}");
                        break;
                    case "DeepSeek":
                        sb.AppendLine($"DEEPSEEK_API_KEY={dialog.ApiKey}");
                        break;
                    case "Anthropic":
                        sb.AppendLine($"ANTHROPIC_API_KEY={dialog.ApiKey}");
                        break;
                    default:
                        sb.AppendLine($"OPENAI_API_KEY={dialog.ApiKey}");
                        break;
                }

                File.WriteAllText(envPath, sb.ToString(), new UTF8Encoding(false));
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取 Gateway 端口号。
        /// </summary>
        public int GetGatewayPort()
        {
            // 默认端口，后续可从配置文件读取
            return 3210;
        }
    }

    /// <summary>
    /// 首次运行配置对话框。
    /// </summary>
    internal class SetupDialog : Form
    {
        private TextBox apiKeyInput = null!;
        private ComboBox providerCombo = null!;

        public string ApiKey => apiKeyInput.Text.Trim();
        public string SelectedProvider => providerCombo.SelectedItem?.ToString() ?? "OpenAI";

        public SetupDialog()
        {
            Text = "🦞 OpenClaw 首次配置";
            Size = new Size(480, 340);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei", 10F);
            BackColor = Color.FromArgb(248, 249, 250);

            SetupUI();
        }

        private void SetupUI()
        {
            // 标题
            var titleLabel = new Label
            {
                Text = "欢迎使用 OpenClaw！",
                Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                AutoSize = true,
                Location = new Point(30, 20)
            };

            var subtitleLabel = new Label
            {
                Text = "请填写以下配置以开始使用：",
                ForeColor = Color.DimGray,
                AutoSize = true,
                Location = new Point(30, 55)
            };

            // 模型厂商选择
            var providerLabel = new Label
            {
                Text = "AI 服务商：",
                AutoSize = true,
                Location = new Point(30, 100)
            };

            providerCombo = new ComboBox
            {
                Location = new Point(140, 97),
                Width = 270,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            providerCombo.Items.AddRange(new[] { "OpenAI", "DeepSeek", "Anthropic" });
            providerCombo.SelectedIndex = 0;

            // API Key 输入
            var apiKeyLabel = new Label
            {
                Text = "API Key：",
                AutoSize = true,
                Location = new Point(30, 145)
            };

            apiKeyInput = new TextBox
            {
                Location = new Point(140, 142),
                Width = 270,
                PlaceholderText = "sk-xxxxxxxxxxxxxxxxxx",
                UseSystemPasswordChar = true
            };

            // 提示
            var tipLabel = new Label
            {
                Text = "💡 提示: 你可以之后在设置中更改此配置。",
                ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei", 8.5F),
                AutoSize = true,
                Location = new Point(30, 185)
            };

            // 按钮
            var confirmBtn = new Button
            {
                Text = "✅ 确认并启动",
                Location = new Point(140, 230),
                Width = 180,
                Height = 40,
                BackColor = Color.FromArgb(0, 102, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            confirmBtn.FlatAppearance.BorderSize = 0;
            confirmBtn.Click += (s, e) => DialogResult = DialogResult.OK;

            var skipBtn = new Button
            {
                Text = "跳过, 稍后配置",
                Location = new Point(330, 230),
                Width = 100,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.DimGray,
                BackColor = Color.FromArgb(248, 249, 250),
                Cursor = Cursors.Hand
            };
            skipBtn.FlatAppearance.BorderSize = 0;
            skipBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[]
            {
                titleLabel, subtitleLabel,
                providerLabel, providerCombo,
                apiKeyLabel, apiKeyInput,
                tipLabel,
                confirmBtn, skipBtn
            });
        }
    }
}
