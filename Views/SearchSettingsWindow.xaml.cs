using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Layouter.Services;
using Layouter.Utility;

namespace Layouter.Views
{
    /// <summary>
    /// 搜索设置面板
    /// </summary>
    /// <remarks>
    /// <para>
    /// 集中管理全局搜索的三项配置：是否开启全盘搜索、触发全盘搜索的最短字符数、
    /// 以及文件索引的查看与重建。早先这些入口散落在托盘的多级子菜单里，
    /// 既不好找也看不到索引状态，因此统一收到这个面板。
    /// </para>
    /// <para>
    /// 所有改动用「改完立即写配置」的方式处理（没有取消/回滚）：
    /// 索引重建动辄几十秒，让用户先改再确认反而容易误解为没生效。
    /// </para>
    /// </remarks>
    public partial class SearchSettingsWindow : Window
    {
        /// <summary>索引状态的刷新间隔</summary>
        private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromMilliseconds(800);

        private readonly DispatcherTimer statusTimer;

        /// <summary>初始化期间不响应控件事件，避免把默认值当成用户修改写回配置</summary>
        private bool isInitializing = true;

        public SearchSettingsWindow()
        {
            InitializeComponent();

            LoadSettings();

            statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
            statusTimer.Tick += (s, e) => RefreshIndexStatus();

            Loaded += SearchSettingsWindow_Loaded;
            Closed += SearchSettingsWindow_Closed;
        }

        /// <summary>
        /// 打开搜索设置面板
        /// </summary>
        /// <param name="owner">所属窗口；从托盘打开时传 <c>null</c></param>
        public static void ShowSettings(Window? owner = null)
        {
            try
            {
                var window = new SearchSettingsWindow();

                if (owner != null && owner.IsLoaded)
                {
                    window.Owner = owner;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Information($"打开搜索设置失败: {ex.Message}");
                AppMessageBox.Show($"打开搜索设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshIndexStatus();
            statusTimer.Start();
        }

        private void SearchSettingsWindow_Closed(object? sender, EventArgs e)
        {
            statusTimer.Stop();
        }

        /// <summary>
        /// 把当前配置填进控件
        /// </summary>
        private void LoadSettings()
        {
            isInitializing = true;

            try
            {
                GeneralSettingsService settings = GeneralSettingsService.Instance;

                EnableFileSystemSearchCheck.IsChecked = settings.GetEnableFullFileSystemSearch();

                MinSearchLengthCombo.Items.Clear();
                for (int length = GeneralSettingsService.MinSearchLengthLimit;
                     length <= GeneralSettingsService.MaxSearchLengthLimit;
                     length++)
                {
                    MinSearchLengthCombo.Items.Add(length);
                }

                MinSearchLengthCombo.SelectedItem = settings.GetMinSearchLength();
                UpdateMinLengthHint(settings.GetMinSearchLength());
            }
            finally
            {
                isInitializing = false;
            }
        }

        /// <summary>
        /// 全盘搜索开关
        /// </summary>
        private void EnableFileSystemSearch_Changed(object sender, RoutedEventArgs e)
        {
            if (isInitializing)
            {
                return;
            }

            try
            {
                bool enabled = EnableFileSystemSearchCheck.IsChecked == true;
                GeneralSettingsService.Instance.SetEnableFullFileSystemSearch(enabled);

                if (enabled)
                {
                    // 开启后立刻在后台建库：索引已在磁盘上时这一步几乎瞬间返回
                    LocalFileIndexService.Instance.StartEnsure();
                }

                FooterHint.Text = enabled
                    ? "已开启全盘搜索，正在后台准备文件索引。"
                    : "已关闭全盘搜索，搜索结果只包含分区内的图标。";

                RefreshOpenSearchWindows();
                RefreshIndexStatus();
            }
            catch (Exception ex)
            {
                Log.Information($"保存全盘搜索开关失败: {ex.Message}");
                AppMessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 最短搜索字符数
        /// </summary>
        private void MinSearchLength_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing || MinSearchLengthCombo.SelectedItem is not int length)
            {
                return;
            }

            try
            {
                GeneralSettingsService.Instance.SetMinSearchLength(length);
                UpdateMinLengthHint(length);

                FooterHint.Text = $"已设置为至少 {length} 个字符才检索全盘文件。";

                RefreshOpenSearchWindows();
            }
            catch (Exception ex)
            {
                Log.Information($"保存最短搜索字符数失败: {ex.Message}");
                AppMessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateMinLengthHint(int length)
        {
            MinSearchLengthHint.Text = length <= 1
                ? "最灵敏：输入单个字符就会检索全盘，结果较多"
                : $"当前：至少 {length} 个字符";
        }

        /// <summary>
        /// 重建索引
        /// </summary>
        private void Rebuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool started = LocalFileIndexService.Instance.Rebuild();

                FooterHint.Text = started
                    ? "已开始重建索引，期间搜索仍可使用旧索引外的已建盘，稍候即可完成。"
                    : "索引正在建立中，请等这一轮完成后再重建。";

                RefreshIndexStatus();
            }
            catch (Exception ex)
            {
                Log.Information($"重建文件索引失败: {ex.Message}");
                AppMessageBox.Show($"重建索引失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenIndexFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string directory = LocalFileIndexService.IndexDirectory;
                System.IO.Directory.CreateDirectory(directory);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Log.Information($"打开索引目录失败: {ex.Message}");
                AppMessageBox.Show($"打开索引目录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开快捷键设置（原先由搜索窗口右上角的齿轮直接进入）
        /// </summary>
        private void HotkeySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new HotkeySettingsWindow
                {
                    Owner = this
                };

                if (window.ShowDialog() == true)
                {
                    // 立即按新配置重新注册全部全局快捷键
                    GlobalHotkeyService.RegisterConfiguredHotkeys();
                }
            }
            catch (Exception ex)
            {
                Log.Information($"打开快捷键设置窗口时出错: {ex.Message}");
                AppMessageBox.Show($"打开快捷键设置窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 刷新索引状态区（面板打开期间定时调用，便于看到建库进度）
        /// </summary>
        private void RefreshIndexStatus()
        {
            FileIndexSummary summary = LocalFileIndexService.Instance.GetSummary();

            RebuildButton.IsEnabled = !summary.IsBuilding;
            RebuildButtonText.Text = summary.IsBuilding ? "正在建立…" : "重建索引";

            if (summary.IsBuilding)
            {
                IndexStatusText.Text = BuildBuildingText(summary);
                return;
            }

            if (!summary.HasIndex)
            {
                IndexStatusText.Text = "尚未建立索引。开启全盘搜索或点击下方「重建索引」即可开始建立。";
                return;
            }

            string drives = string.Join("  ·  ", summary.Drives.Select(d => d.Exists
                ? $"{d.Drive}: {FormatSize(d.SizeBytes)}"
                : $"{d.Drive}: 未建立"));

            string updated = summary.LatestBuiltAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";

            IndexStatusText.Text =
                $"已建立索引：{drives}\n" +
                $"合计 {FormatSize(summary.TotalBytes)}，最近更新 {updated}（超过 24 小时后自动重建）";
        }

        private static string BuildBuildingText(FileIndexSummary summary)
        {
            var built = summary.Drives.Where(d => d.Exists).ToList();

            string progress = built.Count == 0
                ? "正在枚举磁盘文件…"
                : $"已完成 {string.Join("、", built.Select(d => d.Drive + ":"))}，继续处理其余磁盘…";

            return $"正在建立索引：{progress}\n" +
                   "首次建库需要枚举全部磁盘，约 1~3 分钟；期间已建好索引的盘仍可正常搜索。";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 MB";
            }

            const double megabyte = 1024d * 1024d;

            return bytes >= 1024L * 1024L * 1024L
                ? $"{bytes / (megabyte * 1024):0.00} GB"
                : $"{bytes / megabyte:0.#} MB";
        }

        /// <summary>
        /// 设置变化后立刻刷新已打开的搜索窗口，无需重新打开或重新输入
        /// </summary>
        private static void RefreshOpenSearchWindows()
        {
            try
            {
                Application? app = Application.Current;
                if (app == null)
                {
                    return;
                }

                foreach (GlobalSearchWindow window in app.Windows.OfType<GlobalSearchWindow>().ToList())
                {
                    window.RefreshForSettingsChange();
                }
            }
            catch (Exception ex)
            {
                Log.Information($"刷新已打开的搜索窗口失败: {ex.Message}");
            }
        }
    }
}
