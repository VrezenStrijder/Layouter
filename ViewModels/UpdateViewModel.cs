using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Layouter.Models;
using System.Text.Json;

namespace Layouter.ViewModels
{
    public partial class UpdateWindowViewModel : ObservableObject
    {
        private readonly HttpClient httpClient = new HttpClient();
        private readonly string appPath;
        private string tempFilePath;
        private string extractPath;
        private WebClient webClient;
        private int versionFlag = 0;


        public UpdateWindowViewModel(string currentVersion, string updateApiUrl, string releasePageUrl, string releaseApiUrl = null)
        {
            CurrentVersion = currentVersion;
            UpdateApiUrl = updateApiUrl;
            ReleasePageUrl = releasePageUrl;
            ReleaseApiUrl = releaseApiUrl ?? Env.ReleaseApiUrl;
            appPath = Process.GetCurrentProcess().MainModule.FileName;

            // GitHub API 要求请求携带 User-Agent
            try
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Layouter");
            }
            catch
            {
                // 忽略
            }

            CurrentVersionInfo = new ProductVersion
            {
                LatestVersion = currentVersion,
                Description = "点击「检测新版本」获取该版本的更新说明。"
            };
        }

        [ObservableProperty]
        private string currentVersion;

        [ObservableProperty]
        private string updateApiUrl;

        [ObservableProperty]
        private string releasePageUrl;

        [ObservableProperty]
        private string releaseApiUrl;

        [ObservableProperty]
        private string statusMessage = "点击检测按钮获取版本信息";

        [ObservableProperty]
        private int downloadProgress;

        [ObservableProperty]
        private string progressMessage = "正在下载...";

        [ObservableProperty]
        private Visibility versionsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility progressVisibility = Visibility.Collapsed;

        /// <summary>
        /// 当前版本的详细信息（版本号 / 发布日期 / 更新说明）
        /// </summary>
        [ObservableProperty]
        private ProductVersion currentVersionInfo;

        /// <summary>
        /// 可更新的最新版本信息，存在新版本时才有值
        /// </summary>
        [ObservableProperty]
        private ProductVersion latestVersionInfo;

        /// <summary>
        /// 是否存在可下载的新版本
        /// </summary>
        [ObservableProperty]
        private bool hasUpdate;

        [ObservableProperty]
        private ObservableCollection<ProductVersion> availableVersions = new ObservableCollection<ProductVersion>();

        [ObservableProperty]
        private ObservableCollection<OptionItem> optionItems = new ObservableCollection<OptionItem>()
        {
            new OptionItem { Name = "框架依赖版本",IsSelected= true },
            new OptionItem { Name = "独立安装版本",IsSelected= false }
        };

        /// <summary>
        /// 检测新版本，并同时拉取「当前版本」与「最新版本」对应的更新信息
        /// </summary>
        public async Task CheckForUpdatesAsync()
        {
            StatusMessage = "正在检测新版本...";
            VersionsVisibility = Visibility.Collapsed;
            AvailableVersions.Clear();
            LatestVersionInfo = null;
            HasUpdate = false;

            CurrentVersionInfo = new ProductVersion
            {
                LatestVersion = CurrentVersion,
                Description = "正在获取当前版本的更新信息..."
            };

            try
            {
                // 获取版本信息
                HttpResponseMessage response = await httpClient.GetAsync(UpdateApiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    StatusMessage = $"检测更新失败: {(int)response.StatusCode} {response.ReasonPhrase}";
                    await FillCurrentVersionInfoAsync(null);
                    return;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                var latestVersion = JsonSerializer.Deserialize<ProductVersion>(jsonContent);

                if (latestVersion == null || string.IsNullOrWhiteSpace(latestVersion.LatestVersion))
                {
                    StatusMessage = "未获取到有效的版本信息";
                    await FillCurrentVersionInfoAsync(null);
                    return;
                }

                // 最新版本的更新说明：优先使用 update.json 中的描述，缺失时从发布页拉取
                string latestNotes = latestVersion.Description;
                if (string.IsNullOrWhiteSpace(latestNotes))
                {
                    latestNotes = await FetchReleaseNotesAsync(latestVersion.LatestVersion);
                }
                latestVersion.Description = string.IsNullOrWhiteSpace(latestNotes)
                    ? "该版本暂未提供更新说明。"
                    : FormatReleaseNotes(latestNotes);

                // 比较版本
                Version currentVer = ParseVersion(CurrentVersion);
                Version newVer = ParseVersion(latestVersion.LatestVersion);

                bool updateAvailable = newVer != null && currentVer != null && newVer > currentVer;

                // 无论是否需要更新，都展示当前版本对应的更新信息
                await FillCurrentVersionInfoAsync(updateAvailable ? null : latestVersion.ReleaseDate);

                if (updateAvailable)
                {
                    latestVersion.IsUpdateAvailable = true;
                    HasUpdate = true;
                    AvailableVersions.Add(latestVersion);
                    LatestVersionInfo = latestVersion;
                    VersionsVisibility = Visibility.Visible;
                    StatusMessage = $"发现新版本 {latestVersion.LatestVersion}";
                }
                else
                {
                    StatusMessage = "您当前使用的已经是最新版本";
                    VersionsVisibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"检测更新失败: {ex.Message}";
                await FillCurrentVersionInfoAsync(null);
            }
        }

        /// <summary>
        /// 填充当前版本的详细信息与更新说明
        /// </summary>
        private async Task FillCurrentVersionInfoAsync(string releaseDate)
        {
            string notes = await FetchReleaseNotesAsync(CurrentVersion);

            CurrentVersionInfo = new ProductVersion
            {
                LatestVersion = CurrentVersion,
                ReleaseDate = string.IsNullOrWhiteSpace(releaseDate) ? "未知" : releaseDate,
                Description = string.IsNullOrWhiteSpace(notes)
                    ? "该版本暂未提供更新说明。"
                    : FormatReleaseNotes(notes)
            };
        }

        /// <summary>
        /// 从 GitHub Releases 拉取指定标签的更新说明
        /// </summary>
        private async Task<string> FetchReleaseNotesAsync(string version)
        {
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(ReleaseApiUrl))
            {
                return string.Empty;
            }

            // 发布标签可能带 v 前缀，也可能不带
            string[] candidates = version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? new[] { version, version.TrimStart('v') }
                : new[] { "v" + version, version };

            foreach (string tag in candidates)
            {
                try
                {
                    string url = $"{ReleaseApiUrl}{tag}";
                    HttpResponseMessage response = await httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        if (document.RootElement.TryGetProperty("body", out JsonElement body))
                        {
                            string text = body.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Information($"获取版本 {version} 的更新说明失败: {ex.Message}");
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 解析版本号，兼容 "2.0" / "2.0.0.0" / "v2.0.0" 等写法
        /// </summary>
        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim().TrimStart('v', 'V');

            // 去掉可能存在的预发布后缀（如 2.0.0-beta）
            int dashIndex = text.IndexOfAny(new[] { '-', '+' });
            if (dashIndex > 0)
            {
                text = text.Substring(0, dashIndex);
            }

            return Version.TryParse(text, out Version version) ? version : null;
        }

        /// <summary>
        /// 把 Markdown 形式的更新说明转换为便于阅读的纯文本
        /// </summary>
        private static string FormatReleaseNotes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return string.Empty;
            }

            var lines = notes.Replace("\r\n", "\n").Split('\n');
            var builder = new StringBuilder();

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();

                // 标题：去掉 # 标记
                int heading = line.TrimStart().TakeWhile(c => c == '#').Count();
                if (heading > 0)
                {
                    line = line.TrimStart().TrimStart('#').Trim();
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }
                    builder.AppendLine($"【{line}】");
                    continue;
                }

                // 列表项
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    trimmed = "• " + trimmed.Substring(2).Trim();
                }

                // 行内链接 / 粗体 / 斜体
                trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\[([^\]]+)\]\(([^)]+)\)", "$1");
                trimmed = trimmed.Replace("**", string.Empty).Replace("__", string.Empty);
                trimmed = trimmed.Replace("`", string.Empty);

                builder.AppendLine(trimmed);
            }

            return builder.ToString().Trim();
        }

        public void OpenReleasePage()
        {
            Process.Start(new ProcessStartInfo { FileName = ReleasePageUrl, UseShellExecute = true });
        }

        public void DownloadAndInstall(bool useFirstLink = true)
        {
            var versionInfo = AvailableVersions.FirstOrDefault();

            if (versionInfo == null)
            {
                return;
            }

            // 根据用户选择决定使用哪个下载链接
            string downloadUrl = useFirstLink
                ? versionInfo.DownloadLink1
                : versionInfo.DownloadLink2;

            versionFlag = useFirstLink ? 0 : 1;

            if (string.IsNullOrEmpty(downloadUrl))
            {
                StatusMessage = "下载链接无效";
                return;
            }

            ProgressVisibility = Visibility.Visible;
            DownloadProgress = 0;
            ProgressMessage = $"正在下载版本 {versionInfo.LatestVersion}...";

            // 创建临时目录
            string tempDir = Path.Combine(Path.GetTempPath(), "AppUpdater");
            Directory.CreateDirectory(tempDir);
            tempFilePath = Path.Combine(tempDir, $"update_{versionInfo.LatestVersion}.zip");
            extractPath = Path.Combine(tempDir, $"extract_{versionInfo.LatestVersion}");

            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }
            Directory.CreateDirectory(extractPath);

            // 下载更新包
            webClient = new WebClient();
            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;
            webClient.DownloadFileAsync(new Uri(downloadUrl), tempFilePath);
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgress = e.ProgressPercentage;
        }

        private void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ProgressMessage = $"下载失败: {e.Error.Message}";
                return;
            }

            ProgressMessage = "下载完成，正在解压...";

            try
            {
                // 解压文件
                ZipFile.ExtractToDirectory(tempFilePath, extractPath);
                ProgressMessage = "解压完成，准备安装更新...";

                // 创建重启脚本
                string batchPath = Path.Combine(Path.GetTempPath(), "AppUpdater", "update.bat");
                string appDir = Path.GetDirectoryName(appPath);
                string appExe = Path.GetFileName(appPath);
                string extractAppFolder = $"{extractPath}\\{(versionFlag == 0? Env.FrameworkDependencyVersionFolderName : Env.IndependentVersionFolderName)}";

                using (StreamWriter writer = new StreamWriter(batchPath))
                {
                    writer.WriteLine("@echo off");
                    writer.WriteLine("timeout /t 2 /nobreak > NUL"); // 等待2秒确保应用已关闭
                    writer.WriteLine($"echo 正在更新应用...");
                    writer.WriteLine($"xcopy \"{extractAppFolder}\\*\" \"{appDir}\\\" /E /Y /I");
                    writer.WriteLine("if %ERRORLEVEL% NEQ 0 (");
                    writer.WriteLine("  echo 更新失败");
                    writer.WriteLine("  pause");
                    writer.WriteLine("  exit /b %ERRORLEVEL%");
                    writer.WriteLine(")");
                    writer.WriteLine($"echo 更新完成，正在启动应用...");
                    writer.WriteLine($"start \"\" \"{appDir}\\{appExe}\"");
                    writer.WriteLine("del /Q \"%~f0\""); // 自删除批处理文件
                    writer.WriteLine("pause"); // 自删除批处理文件
                }

                // 执行重启脚本
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    WindowStyle = ProcessWindowStyle.Normal,
                    CreateNoWindow = true
                };

                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ProgressMessage = $"更新安装失败: {ex.Message}";
                Log.Error(ProgressMessage);
            }
        }
    }

}
