using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenClawInstaller
{
    /// <summary>
    /// 负责卸载 OpenClaw 安装目录下的所有组件。
    /// </summary>
    public class UninstallWorker
    {
        private readonly string installDir;
        private readonly bool deleteUserData;

        /// <summary>
        /// 需要删除的子目录列表（运行时 + 工具）。
        /// </summary>
        private static readonly string[] Directories =
        {
            "nodejs",
            "git_env",
            "openclaw_app",
            "skills_bin",
            ".npm-cache"
        };

        /// <summary>
        /// 需要删除的文件列表（启动脚本等）。
        /// </summary>
        private static readonly string[] Files =
        {
            "start.ps1",
            "点我运行.bat"
        };

        /// <param name="installDir">安装目录路径</param>
        /// <param name="deleteUserData">是否同时删除 data/ 用户数据目录</param>
        public UninstallWorker(string installDir, bool deleteUserData = true)
        {
            this.installDir = Path.GetFullPath(installDir);
            this.deleteUserData = deleteUserData;
        }

        /// <summary>
        /// 执行卸载操作。
        /// </summary>
        public async Task RunAsync(IProgress<int> progress, IProgress<string> logger)
        {
            logger.Report($"开始卸载... 目标目录: {installDir}");

            if (!Directory.Exists(installDir))
            {
                throw new DirectoryNotFoundException($"安装目录不存在: {installDir}");
            }

            // 计算总步数用于进度条
            int totalSteps = Directories.Length + Files.Length + (deleteUserData ? 1 : 0) + 1; // +1 for final cleanup
            int currentStep = 0;

            // 1. 删除子目录
            foreach (string dirName in Directories)
            {
                string dirPath = Path.Combine(installDir, dirName);
                if (Directory.Exists(dirPath))
                {
                    logger.Report($"正在删除: {dirName}/");
                    await Task.Run(() => Utils.RobustDeleteDirectory(dirPath));
                    logger.Report($"  ✓ 已删除 {dirName}/");
                }
                else
                {
                    logger.Report($"  - 跳过 {dirName}/ (不存在)");
                }
                currentStep++;
                progress.Report((int)((double)currentStep / totalSteps * 100));
            }

            // 2. 删除 data/ 用户数据 (可选)
            if (deleteUserData)
            {
                string dataDir = Path.Combine(installDir, "data");
                if (Directory.Exists(dataDir))
                {
                    logger.Report("正在删除用户数据: data/");
                    await Task.Run(() => Utils.RobustDeleteDirectory(dataDir));
                    logger.Report("  ✓ 已删除 data/");
                }
                else
                {
                    logger.Report("  - 跳过 data/ (不存在)");
                }
                currentStep++;
                progress.Report((int)((double)currentStep / totalSteps * 100));
            }

            // 3. 删除启动脚本文件
            foreach (string fileName in Files)
            {
                string filePath = Path.Combine(installDir, fileName);
                if (File.Exists(filePath))
                {
                    logger.Report($"正在删除: {fileName}");
                    File.Delete(filePath);
                    logger.Report($"  ✓ 已删除 {fileName}");
                }
                else
                {
                    logger.Report($"  - 跳过 {fileName} (不存在)");
                }
                currentStep++;
                progress.Report((int)((double)currentStep / totalSteps * 100));
            }

            // 4. 也检查 runtime/ 子布局 (CI 预构建包的目录结构)
            string runtimeDir = Path.Combine(installDir, "runtime");
            if (Directory.Exists(runtimeDir))
            {
                logger.Report("检测到 runtime/ 目录 (CI 预构建布局)，正在删除...");
                await Task.Run(() => Utils.RobustDeleteDirectory(runtimeDir));
                logger.Report("  ✓ 已删除 runtime/");
            }

            // 5. 尝试清理空的安装目录
            currentStep++;
            progress.Report((int)((double)currentStep / totalSteps * 100));

            try
            {
                // 检查安装目录是否已为空（排除安装器本身）
                var remaining = Directory.GetFileSystemEntries(installDir);
                if (remaining.Length == 0)
                {
                    Directory.Delete(installDir);
                    logger.Report("安装目录已清空并删除。");
                }
                else
                {
                    logger.Report($"安装目录下仍有 {remaining.Length} 个文件/文件夹，已保留目录。");
                }
            }
            catch
            {
                // 安装目录无法删除（可能因为安装器自身还在其中运行），忽略
                logger.Report("安装目录自身无法删除 (可能有文件正在使用)，已保留。");
            }

            progress.Report(100);
            logger.Report("卸载完成！");
        }
    }
}
