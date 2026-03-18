using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawInstaller
{
    /// <summary>
    /// 管理 OpenClaw Gateway 进程的完整生命周期: 启动、探测就绪、停止。
    /// </summary>
    public class GatewayManager : IDisposable
    {
        private Process? gatewayProcess;
        private readonly string baseDir;
        private readonly string runtimeDir;
        private readonly int port;
        private bool disposed;

        /// <summary>Gateway 输出日志事件</summary>
        public event Action<string>? OnOutput;
        /// <summary>Gateway 错误日志事件</summary>
        public event Action<string>? OnError;
        /// <summary>Gateway 进程退出事件</summary>
        public event Action<int>? OnExited;

        public bool IsRunning => gatewayProcess != null && !gatewayProcess.HasExited;

        /// <param name="baseDir">应用根目录 (exe 所在目录)</param>
        /// <param name="runtimeDir">运行时目录 (包含 nodejs/, git_env/, openclaw_app/ 的目录)</param>
        /// <param name="port">Gateway 监听端口, 默认 3210</param>
        public GatewayManager(string baseDir, string runtimeDir, int port = 3210)
        {
            this.baseDir = Path.GetFullPath(baseDir);
            this.runtimeDir = Path.GetFullPath(runtimeDir);
            this.port = port;
        }

        /// <summary>
        /// 启动 Gateway 进程。
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            string nodejsDir = Path.Combine(runtimeDir, "nodejs");
            string gitCmdDir = Path.Combine(runtimeDir, "git_env", "cmd");
            string appDir = Path.Combine(runtimeDir, "openclaw_app");
            string skillsBinDir = Path.Combine(runtimeDir, "skills_bin");
            string dataDir = Path.Combine(baseDir, "data");

            string nodeExe = Path.Combine(nodejsDir, "node.exe");
            string npxCmd = Path.Combine(nodejsDir, "npx.cmd");

            if (!File.Exists(nodeExe))
                throw new FileNotFoundException($"找不到 Node.js: {nodeExe}\n请确认运行时环境完整。");

            // 构建 PATH: nodejs + git + openclaw bins + skills + 系统 PATH
            var pathParts = new System.Collections.Generic.List<string>
            {
                nodejsDir,
                gitCmdDir,
                Path.Combine(appDir, "node_modules", ".bin")
            };

            // 自动添加所有 skills_bin 子目录
            if (Directory.Exists(skillsBinDir))
            {
                foreach (var skillDir in Directory.GetDirectories(skillsBinDir))
                {
                    pathParts.Add(skillDir);
                    // 也检查常见 bin 子目录
                    string binSubDir = Path.Combine(skillDir, "bin");
                    if (Directory.Exists(binSubDir))
                        pathParts.Add(binSubDir);
                }
            }

            pathParts.Add(Environment.GetEnvironmentVariable("PATH") ?? "");
            string customPath = string.Join(";", pathParts);

            // 查找 gateway 启动脚本
            string gatewayCmd = FindGatewayScript(dataDir);

            ProcessStartInfo psi;

            if (gatewayCmd != null)
            {
                // 使用已有的 gateway.cmd
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{gatewayCmd}\"",
                    WorkingDirectory = appDir,
                };
            }
            else
            {
                // 直接通过 npx 启动
                psi = new ProcessStartInfo
                {
                    FileName = npxCmd,
                    Arguments = "openclaw gateway",
                    WorkingDirectory = appDir,
                };
            }

            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            // 注入环境变量
            psi.EnvironmentVariables["PATH"] = customPath;

            // 便携模式: 将用户数据目录指向本地 data 文件夹
            if (Directory.Exists(dataDir))
            {
                psi.EnvironmentVariables["USERPROFILE"] = dataDir;
                psi.EnvironmentVariables["HOME"] = dataDir;
                psi.EnvironmentVariables["APPDATA"] = Path.Combine(dataDir, "AppData", "Roaming");
                psi.EnvironmentVariables["LOCALAPPDATA"] = Path.Combine(dataDir, "AppData", "Local");
            }

            // GPU 探测
            try
            {
                var checkGpu = Process.Start(new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "-L",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                checkGpu?.WaitForExit();
                psi.EnvironmentVariables["NODE_LLAMA_CPP_BUILD_TYPE"] =
                    checkGpu?.ExitCode == 0 ? "cuda" : "cpu";
            }
            catch
            {
                psi.EnvironmentVariables["NODE_LLAMA_CPP_BUILD_TYPE"] = "cpu";
            }

            gatewayProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            gatewayProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnOutput?.Invoke(e.Data);
            };
            gatewayProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnError?.Invoke(e.Data);
            };
            gatewayProcess.Exited += (s, e) =>
            {
                OnExited?.Invoke(gatewayProcess.ExitCode);
            };

            gatewayProcess.Start();
            gatewayProcess.BeginOutputReadLine();
            gatewayProcess.BeginErrorReadLine();
        }

        /// <summary>
        /// 等待 Gateway HTTP 端口就绪。
        /// </summary>
        public async Task<bool> WaitForReadyAsync(int timeoutSeconds = 60, CancellationToken ct = default)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string url = $"http://localhost:{port}";
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (gatewayProcess == null || gatewayProcess.HasExited)
                    return false;

                try
                {
                    var response = await client.GetAsync(url, ct);
                    if ((int)response.StatusCode < 500)
                        return true;
                }
                catch
                {
                    // 端口尚未就绪, 继续轮询
                }

                await Task.Delay(500, ct);
            }

            return false;
        }

        /// <summary>
        /// 停止 Gateway 进程。
        /// </summary>
        public void Stop()
        {
            if (gatewayProcess == null || gatewayProcess.HasExited) return;

            try
            {
                // 尝试优雅关闭整个进程树
                KillProcessTree(gatewayProcess.Id);
            }
            catch
            {
                try { gatewayProcess.Kill(entireProcessTree: true); } catch { }
            }
            finally
            {
                gatewayProcess.Dispose();
                gatewayProcess = null;
            }
        }

        /// <summary>
        /// 查找已生成的 gateway.cmd 启动脚本。
        /// </summary>
        private string? FindGatewayScript(string dataDir)
        {
            string[] possiblePaths =
            {
                Path.Combine(dataDir, ".openclaw", "gateway.cmd"),
                Path.Combine(dataDir, "gateway.cmd"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// 通过 taskkill 终止进程树 (Windows)。
        /// </summary>
        private static void KillProcessTree(int pid)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/T /F /PID {pid}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }

        ~GatewayManager() => Dispose();
    }
}
