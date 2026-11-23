using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Layouter.Models;
using Layouter.Services;
using Layouter.Utility;
using Layouter.ViewModels;

namespace Layouter.Views
{
    /// <summary>
    /// 全局搜索窗口
    /// </summary>
    /// <remarks>
    /// 结果分两类：分区内的图标、以及（在设置中开启后）整个文件系统的文件/文件夹。
    /// 分区结果按区块展示，文件系统结果单独成区块并支持「添加到分区」。
    /// 回车定位（文件系统结果则直接打开）并高亮；选择「打开」则等同于双击。
    /// </remarks>
    public partial class GlobalSearchWindow : Window
    {
        private readonly GlobalSearchViewModel viewModel = new GlobalSearchViewModel();

        public GlobalSearchWindow()
        {
            InitializeComponent();

            DataContext = viewModel;
            viewModel.LocateRequested += ViewModel_LocateRequested;
            viewModel.OpenRequested += ViewModel_OpenRequested;

            Loaded += GlobalSearchWindow_Loaded;
            Closed += GlobalSearchWindow_Closed;
        }

        private void GlobalSearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 索引在窗口打开时建立一次，之后每次输入只做字符串比较
            viewModel.RebuildIndex();
            FocusSearchBox();
        }

        private void GlobalSearchWindow_Closed(object? sender, EventArgs e)
        {
            viewModel.LocateRequested -= ViewModel_LocateRequested;
            viewModel.OpenRequested -= ViewModel_OpenRequested;
        }

        /// <summary>
        /// 设置项（全文件系统搜索开关）变化后由托盘菜单调用，立即刷新
        /// </summary>
        public void RefreshForSettingsChange()
        {
            viewModel.RefreshForSettingsChange();
        }

        /// <summary>
        /// 聚焦搜索框（窗口已存在时重新唤起也要用）
        /// </summary>
        public void FocusSearchBox()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            }));
        }

        private void ViewModel_LocateRequested(object? sender, SearchResultItem item)
        {
            Locate(item);
        }

        private void ViewModel_OpenRequested(object? sender, SearchResultItem item)
        {
            Open(item);
        }

        /// <summary>
        /// 定位到图标：切换到所属分区并高亮（文件系统结果改为直接打开）
        /// </summary>
        private void Locate(SearchResultItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsFileSystem)
            {
                Open(item);
                return;
            }

            DesktopManagerWindow window = WindowManagerService.Instance.GetWindowById(item.WindowId);
            if (window == null)
            {
                AppMessageBox.Show("未找到该图标所属的分区，可能已被删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            window.FocusIcon(item.Icon);
            Close();
        }

        /// <summary>
        /// 打开结果：分区图标走分区窗口，文件系统结果直接用 Shell 打开
        /// </summary>
        private void Open(SearchResultItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsFileSystem)
            {
                OpenPath(item.OpenPath);
                Close();
                return;
            }

            DesktopManagerWindow window = WindowManagerService.Instance.GetWindowById(item.WindowId);
            window?.OpenIcon(item.Icon);
            Close();
        }

        /// <summary>
        /// 以系统默认方式打开一个文件/文件夹
        /// </summary>
        private static void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Information($"打开结果失败({path}): {ex.Message}");
                AppMessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GlobalSearchWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Ctrl+Enter：直接打开
                        if (viewModel.SelectedResult != null)
                        {
                            viewModel.OpenCommand.Execute(viewModel.SelectedResult);
                        }
                    }
                    else
                    {
                        // Enter：定位并高亮（文件系统结果在 Locate 内部转为打开）
                        if (viewModel.SelectedResult != null)
                        {
                            viewModel.LocateCommand.Execute(viewModel.SelectedResult);
                        }
                    }
                    e.Handled = true;
                    break;

                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// 上下键在结果列表中移动选择
        /// </summary>
        private void MoveSelection(int offset)
        {
            if (viewModel.Results.Count == 0)
            {
                return;
            }

            int index = viewModel.SelectedResult == null ? -1 : viewModel.Results.IndexOf(viewModel.SelectedResult);
            index += offset;

            if (index < 0)
            {
                index = 0;
            }
            else if (index >= viewModel.Results.Count)
            {
                index = viewModel.Results.Count - 1;
            }

            viewModel.SelectedResult = viewModel.Results[index];
            ResultsList.ScrollIntoView(viewModel.SelectedResult);
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 双击建议项等同于双击图标 / 打开文件
            if (viewModel.SelectedResult != null)
            {
                viewModel.OpenCommand.Execute(viewModel.SelectedResult);
            }
        }

        /// <summary>
        /// 定位按钮（仅分区图标结果显示）
        /// </summary>
        private void LocateResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SearchResultItem item && item.CanLocate)
            {
                viewModel.LocateCommand.Execute(item);
            }
        }

        private void OpenResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SearchResultItem item)
            {
                viewModel.OpenCommand.Execute(item);
            }
        }

        /// <summary>
        /// 以管理员身份运行结果
        /// </summary>
        /// <remarks>
        /// 用户在 UAC 弹窗点「否」时会抛出 Win32Exception，这属于主动取消，
        /// 只记日志不弹错误框，避免误报。
        /// </remarks>
        private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SearchResultItem item || !item.CanRunAsAdmin)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.OpenPath,
                    Verb = "runas",
                    UseShellExecute = true
                });
                Close();
            }
            catch (Exception ex)
            {
                Log.Information($"以管理员身份运行失败({item.OpenPath}): {ex.Message}");
            }
        }

        /// <summary>
        /// 「添加到分区」按钮：弹出分区列表，选中后把该文件加入目标分区
        /// </summary>
        private void AddToPartition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SearchResultItem item)
            {
                return;
            }

            if (!item.IsFileSystem || string.IsNullOrEmpty(item.FilePath))
            {
                return;
            }

            var windows = WindowManagerService.Instance.GetAllWindows()
                .Where(w => w?.DataContext is DesktopManagerViewModel)
                .ToList();

            if (windows.Count == 0)
            {
                AppMessageBox.Show("当前没有可用的分区，请先创建一个分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu();
            foreach (var window in windows)
            {
                var vm = (DesktopManagerViewModel)window.DataContext;
                string name = string.IsNullOrEmpty(vm.Name) ? "未命名分区" : vm.Name;

                var menuItem = new MenuItem
                {
                    Header = name,
                    Tag = new Tuple<DesktopManagerWindow, string>(window, item.FilePath)
                };
                menuItem.Click += AddToPartitionMenuItem_Click;
                menu.Items.Add(menuItem);
            }

            menu.PlacementTarget = element;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddToPartitionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is Tuple<DesktopManagerWindow, string> target)
            {
                AddFileToPartition(target.Item1, target.Item2);
            }
        }

        /// <summary>
        /// 把一个文件系统路径登记为某个分区里的图标（不移动原文件，仅建立引用并落盘）
        /// </summary>
        private static void AddFileToPartition(DesktopManagerWindow window, string filePath)
        {
            if (window?.DataContext is not DesktopManagerViewModel vm)
            {
                return;
            }

            try
            {
                // 去重：分区里已存在同一路径时不再添加
                foreach (var existing in vm.Icons)
                {
                    if (existing != null &&
                        string.Equals(existing.DisplayPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        TrayIconService.Instance.ShowBalloonTip("添加到分区", "该分区中已存在此项目。", BalloonIcon.Info);
                        return;
                    }
                }

                var icon = new DesktopIcon
                {
                    Id = Guid.NewGuid().ToString(),
                    Position = new Point(0, 0),
                    Size = vm.GetIconSize(),
                    TextSize = vm.IconTextSize,
                    IconPath = filePath
                };

                if (ShortCutUtil.IsShortcutPath(filePath))
                {
                    icon.Name = Path.GetFileNameWithoutExtension(filePath);
                    icon.IconType = IconType.Shortcut;
                }
                else if (Directory.Exists(filePath))
                {
                    icon.Name = new DirectoryInfo(filePath).Name;
                    icon.IconType = IconType.Folder;
                }
                else
                {
                    icon.Name = Path.GetFileName(filePath);
                    icon.IconType = IconType.File;
                }

                vm.AddIcon(icon);

                // 重新排布并落盘，保证新图标位置正确、配置即时持久化
                window.ArrangeIconsNow();

                string partitionName = string.IsNullOrEmpty(vm.Name) ? "未命名分区" : vm.Name;
                TrayIconService.Instance.ShowBalloonTip("添加到分区", $"已把「{icon.Name}」添加到「{partitionName}」。", BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Information($"添加到分区失败({filePath}): {ex.Message}");
                AppMessageBox.Show($"添加到分区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开图标程序所在的目录
        /// </summary>
        /// <remarks>
        /// 目标是文件时用 <c>explorer /select</c> 打开所在目录并选中该文件；
        /// 目标不可用时退化为直接打开目录本身。
        /// </remarks>
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SearchResultItem item)
            {
                return;
            }

            string folder = item.FolderPath ?? string.Empty;

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            try
            {
                string target = item.TargetPath ?? string.Empty;

                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    StartExplorer($"/select,\"{target}\"");
                }
                else
                {
                    StartExplorer($"\"{folder}\"");
                }
            }
            catch (Exception ex)
            {
                Log.Information($"打开图标所在目录失败({folder}): {ex.Message}");
                AppMessageBox.Show($"打开所在目录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 调用资源管理器打开目录
        /// </summary>
        /// <remarks>
        /// 必须走 <c>UseShellExecute = false</c>：
        /// 资源管理器是单实例进程，用 ShellExecute 启动时新参数会被忽略。
        /// </remarks>
        private static void StartExplorer(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = false
            };

            Process.Start(startInfo);
        }

        /// <summary>
        /// 打开搜索设置面板（全盘搜索开关、最短字符数、索引重建、快捷键）
        /// </summary>
        private void SearchSettings_Click(object sender, RoutedEventArgs e)
        {
            SearchSettingsWindow.ShowSettings(this);
        }
    }
}
