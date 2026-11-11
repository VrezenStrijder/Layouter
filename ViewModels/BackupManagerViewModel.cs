using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Layouter.Models;
using Layouter.Services;

namespace Layouter.ViewModels
{
    /// <summary>
    /// 备份与还原窗口的视图模型
    /// </summary>
    public partial class BackupManagerViewModel : ObservableObject
    {
        [ObservableProperty]
        private string backupName = string.Empty;

        [ObservableProperty]
        private string backupRemark = string.Empty;

        [ObservableProperty]
        private string backupRootPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<BackupInfo> backups = new ObservableCollection<BackupInfo>();

        [ObservableProperty]
        private BackupInfo? selectedBackup;

        [ObservableProperty]
        private string statusMessage = "就绪";

        public BackupManagerViewModel()
        {
            RefreshBackupRootPath();
            RefreshBackups();
        }

        /// <summary>
        /// 是否已选中某个备份
        /// </summary>
        public bool HasSelectedBackup => SelectedBackup != null;

        /// <summary>
        /// 当前选中备份的详情文本
        /// </summary>
        public string SelectedBackupDetails
        {
            get
            {
                if (SelectedBackup == null)
                {
                    return "未选择备份";
                }

                BackupInfo backup = SelectedBackup;
                var builder = new StringBuilder();
                builder.AppendLine($"名称：{backup.Name}");
                builder.AppendLine($"类型：{backup.TypeText}");
                builder.AppendLine($"备注：{backup.RemarkText}");
                builder.AppendLine($"创建时间：{backup.CreatedAtText}");
                builder.AppendLine($"应用版本：{(string.IsNullOrWhiteSpace(backup.AppVersion) ? "-" : backup.AppVersion)}");
                builder.AppendLine($"文件数量：{backup.FileCountText}");
                builder.AppendLine($"文件大小：{backup.FileSizeText}");
                builder.AppendLine($"来源：{backup.SourceText}");
                builder.AppendLine($"状态：{backup.ValidText}");
                builder.Append($"文件：{backup.FilePath}");
                return builder.ToString();
            }
        }

        partial void OnSelectedBackupChanged(BackupInfo? value)
        {
            OnPropertyChanged(nameof(SelectedBackupDetails));
            OnPropertyChanged(nameof(HasSelectedBackup));
        }

        /// <summary>
        /// 刷新当前配置的备份根目录
        /// </summary>
        [RelayCommand]
        private void RefreshBackupRootPath()
        {
            try
            {
                BackupRootPath = BackupService.Instance.GetBackupRootDirectory();
            }
            catch (Exception ex)
            {
                Log.Information($"读取备份根目录失败: {ex.Message}");
                BackupRootPath = string.Empty;
            }
        }

        /// <summary>
        /// 重新读取备份列表
        /// </summary>
        [RelayCommand]
        private void RefreshBackups()
        {
            try
            {
                List<BackupInfo> list = BackupService.Instance.GetBackups();

                Backups.Clear();
                foreach (BackupInfo backup in list)
                {
                    Backups.Add(backup);
                }

                SelectedBackup = null;
                StatusMessage = $"共 {Backups.Count} 个备份";
            }
            catch (Exception ex)
            {
                Log.Information($"读取备份列表失败: {ex.Message}");
                AppMessageBox.Show($"读取备份列表失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建一个新的配置备份
        /// </summary>
        [RelayCommand]
        private void CreateBackup()
        {
            if (string.IsNullOrWhiteSpace(BackupName))
            {
                AppMessageBox.Show("请输入备份名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                StatusMessage = "正在创建备份...";
                string path = BackupService.Instance.CreateBackup(BackupName, BackupRemark);

                BackupName = string.Empty;
                BackupRemark = string.Empty;
                RefreshBackups();

                StatusMessage = "备份创建成功";
                AppToastService.Success("备份创建成功", $"已保存到 {path}");
            }
            catch (Exception ex)
            {
                Log.Information($"创建备份失败: {ex.Message}");
                StatusMessage = "创建备份失败";
                AppMessageBox.Show($"创建备份失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 还原选中的备份
        /// </summary>
        [RelayCommand]
        private void RestoreBackup()
        {
            if (SelectedBackup == null)
            {
                AppMessageBox.Show("请先选择要还原的备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!SelectedBackup.IsValid)
            {
                AppMessageBox.Show("该备份无效或已损坏，无法还原。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 还原安全快照时不会再自动生成新的快照，提示文案需要区分
            string snapshotHint = SelectedBackup.IsSafetySnapshot
                ? "该备份是还原前自动生成的安全快照，还原时不会再产生新的快照。"
                : "还原前会自动在备份目录中保存一份安全快照。";

            MessageBoxResult confirm = AppMessageBox.Show(
                $"确定要还原备份「{SelectedBackup.Name}」吗？\n\n还原会覆盖当前的分区与通用配置。\n{snapshotHint}",
                "确认还原", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StatusMessage = "正在还原...";
                RestoreResult result = BackupService.Instance.RestoreBackup(SelectedBackup);

                if (!result.Success)
                {
                    StatusMessage = "还原失败";
                    AppMessageBox.Show($"还原失败：{result.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "还原完成";
                RefreshBackups();

                // 分区窗口已在还原过程中自动重载，这里只需汇总结果
                var details = new System.Text.StringBuilder();
                details.AppendLine("还原已完成，当前打开的分区已同步为还原后的内容。");
                if (result.RestoredToDesktopCount > 0)
                {
                    details.AppendLine($"有 {result.RestoredToDesktopCount} 个不再属于任何分区的图标已放回桌面。");
                }
                if (result.RemovedInvalidIconCount > 0)
                {
                    details.AppendLine($"已清理 {result.RemovedInvalidIconCount} 个目标文件已不存在的图标。");
                }

                AppToastService.Success("还原完成", details.ToString().Trim());
            }
            catch (Exception ex)
            {
                Log.Information($"还原备份失败: {ex.Message}");
                StatusMessage = "还原失败";
                AppMessageBox.Show($"还原备份失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除选中的备份
        /// </summary>
        [RelayCommand]
        private void DeleteBackup()
        {
            if (SelectedBackup == null)
            {
                AppMessageBox.Show("请先选择要删除的备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirm = AppMessageBox.Show(
                $"确定要删除备份「{SelectedBackup.Name}」吗？\n删除后无法恢复。",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (BackupService.Instance.DeleteBackup(SelectedBackup))
            {
                StatusMessage = "备份已删除";
                RefreshBackups();
            }
            else
            {
                AppMessageBox.Show("删除备份失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 选择新的备份根目录
        /// </summary>
        [RelayCommand]
        private void ChangeBackupRoot()
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "选择用于存放配置备份的目录",
                    ShowNewFolderButton = true
                })
                {
                    if (!string.IsNullOrWhiteSpace(BackupRootPath) && Directory.Exists(BackupRootPath))
                    {
                        dialog.SelectedPath = BackupRootPath;
                    }

                    IntPtr owner = GetOwnerWindowHandle();
                    System.Windows.Forms.DialogResult result = owner == IntPtr.Zero
                        ? dialog.ShowDialog()
                        : dialog.ShowDialog(new WindowHandleWrapper(owner));

                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }

                    string selected = dialog.SelectedPath;
                    if (string.IsNullOrWhiteSpace(selected))
                    {
                        return;
                    }

                    BackupService.Instance.SetBackupRootDirectory(selected);
                    RefreshBackupRootPath();
                    RefreshBackups();

                    StatusMessage = "备份目录已更新";
                }
            }
            catch (Exception ex)
            {
                Log.Information($"更改备份目录失败: {ex.Message}");
                AppMessageBox.Show($"更改备份目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 在资源管理器中打开备份根目录
        /// </summary>
        [RelayCommand]
        private void OpenBackupRoot()
        {
            try
            {
                BackupService.Instance.OpenBackupRootFolder();
            }
            catch (Exception ex)
            {
                Log.Information($"打开备份目录失败: {ex.Message}");
                AppMessageBox.Show($"打开备份目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重启应用程序，使还原结果完全生效
        /// </summary>
        private static void RestartApplication()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log.Information($"重新启动应用失败: {ex.Message}");
            }
            finally
            {
                Application.Current?.Shutdown();
            }
        }

        /// <summary>
        /// 获取当前活动窗口的句柄，用于给 WinForms 对话框设置父窗口
        /// </summary>
        private static IntPtr GetOwnerWindowHandle()
        {
            try
            {
                Window? owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
                if (owner == null)
                {
                    return IntPtr.Zero;
                }

                return new WindowInteropHelper(owner).Handle;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 将 WPF 窗口句柄包装为 WinForms 的父窗口句柄
        /// </summary>
        private sealed class WindowHandleWrapper : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }

            public WindowHandleWrapper(IntPtr handle)
            {
                Handle = handle;
            }
        }
    }
}
