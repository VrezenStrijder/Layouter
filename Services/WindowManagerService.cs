using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Layouter.ViewModels;
using Layouter.Views;

namespace Layouter.Services
{
    public class WindowManagerService
    {
        private static readonly Lazy<WindowManagerService> instance = new Lazy<WindowManagerService>(() => new WindowManagerService());
        public static WindowManagerService Instance => instance.Value;

        private List<DesktopManagerWindow> managedWindows = new List<DesktopManagerWindow>();

        private const double WindowMargin = 5;
        private const double SnapThreshold = 10; // 吸附阈值,当窗口边缘距离其他窗口边缘小于此值时触发吸附
        private bool isArrangingWindows = false; // 标记是否正在执行ArrangeWindows操作

        // 正在排队执行尺寸吸附的分区，防止吸附任务堆积把界面卡住
        private readonly HashSet<string> pendingSizeSnaps = new HashSet<string>();

        // 连续尺寸吸附次数，用于识别互相拉扯导致的死循环
        private readonly Dictionary<string, int> sizeSnapCounts = new Dictionary<string, int>();

        private const int MaxConsecutiveSizeSnaps = 3;

        private WindowManagerService()
        {
        }

        public void RegisterWindow(DesktopManagerWindow window)
        {
            if (!managedWindows.Contains(window))
            {
                managedWindows.Add(window);

                // 初始新窗口位置,确保与其他窗口对齐而不是居中
                PositionNewWindow(window);

                // 监听窗口位置变化
                window.LocationChanged += Window_LocationChanged;
                // 监听窗口大小变化
                window.SizeChanged += Window_SizeChanged;
            }
        }

        public DesktopManagerWindow GetWindowByHandle(IntPtr handle)
        {
            return managedWindows.FirstOrDefault(w => new WindowInteropHelper(w).Handle == handle);
        }

        public List<DesktopManagerWindow> GetAllWindows()
        {
            return new List<DesktopManagerWindow>(managedWindows);
        }

        public void UnregisterWindow(DesktopManagerWindow window)
        {
            if (managedWindows.Contains(window))
            {
                window.LocationChanged -= Window_LocationChanged;
                window.SizeChanged -= Window_SizeChanged;
                managedWindows.Remove(window);

                pendingSizeSnaps.Remove(window.WindowId);
                sizeSnapCounts.Remove(window.WindowId);
            }
        }

        public void ArrangeWindows()
        {
            try
            {
                isArrangingWindows = true;
                var visibleWindows = managedWindows.Where(w => w.IsVisible).ToList();
                if (visibleWindows.Count == 0)
                {
                    return;
                }
                var workArea = SystemParameters.WorkArea;
                double currentX = WindowMargin;
                double currentY = WindowMargin;
                double rowHeight = 0;

                foreach (var window in visibleWindows)
                {
                    // 如果窗口超出屏幕宽度,则换行
                    if (currentX + window.Width + WindowMargin > workArea.Width)
                    {
                        currentX = WindowMargin;
                        currentY += rowHeight + WindowMargin;
                        rowHeight = 0;
                    }

                    // 设置窗口位置
                    window.Left = currentX;
                    window.Top = currentY;

                    // 更新位置
                    currentX += window.Width + WindowMargin;
                    rowHeight = Math.Max(rowHeight, window.Height);

                    // 保存分区数据
                    PartitionDataService.Instance.SavePartitionData(window);
                }
            }
            finally
            {
                isArrangingWindows = false;
            }
        }

        /// <summary>
        /// 对齐所有分区窗口
        /// </summary>
        public void ArrangeAllPartitionWindows()
        {
            try
            {
                isArrangingWindows = true;
                var windows = GetAllWindows();
                if (windows.Count == 0)
                {
                    return;
                }
                // 获取屏幕工作区尺寸
                double screenWidth = SystemParameters.WorkArea.Width;
                double screenHeight = SystemParameters.WorkArea.Height;

                // 计算每个窗口的理想尺寸
                int numWindows = windows.Count;
                int numCols = (int)Math.Ceiling(Math.Sqrt(numWindows));
                int numRows = (int)Math.Ceiling((double)numWindows / numCols);

                double windowWidth = (screenWidth - (numCols + 1) * WindowMargin) / numCols;
                double windowHeight = (screenHeight - (numRows + 1) * WindowMargin) / numRows;

                // 分配窗口位置
                for (int i = 0; i < windows.Count; i++)
                {
                    int row = i / numCols;
                    int col = i % numCols;

                    // 计算窗口位置,考虑边距
                    double left = col * (windowWidth + WindowMargin) + WindowMargin;
                    double top = row * (windowHeight + WindowMargin) + WindowMargin;

                    // 设置窗口位置和大小
                    windows[i].Left = left;
                    windows[i].Top = top;
                    windows[i].Width = windowWidth;
                    windows[i].Height = windowHeight;

                    // 保存分区数据
                    PartitionDataService.Instance.SavePartitionData(windows[i]);
                }
            }
            finally
            {
                isArrangingWindows = false;
            }
        }

        private void PositionNewWindow(DesktopManagerWindow newWindow)
        {
            var visibleWindows = managedWindows
                .Where(w => w != newWindow && w.IsVisible)
                .ToList();

            if (visibleWindows.Count == 0)
            {
                // 如果没有其他可见窗口,则放在工作区左上角
                newWindow.Left = WindowMargin;
                newWindow.Top = WindowMargin;
                return;
            }

            // 计算新窗口应该放置的位置
            var workArea = SystemParameters.WorkArea;
            double maxRight = 0;
            double minTop = double.MaxValue;
            double maxBottom = 0;

            foreach (var window in visibleWindows)
            {
                maxRight = Math.Max(maxRight, window.Left + window.Width);
                minTop = Math.Min(minTop, window.Top);
                maxBottom = Math.Max(maxBottom, window.Top + window.Height);
            }

            // 尝试放在最右侧
            if (maxRight + newWindow.Width + WindowMargin <= workArea.Width)
            {
                newWindow.Left = maxRight + WindowMargin;
                newWindow.Top = minTop;
            }
            else
            {
                // 否则尝试放在下一行
                newWindow.Left = WindowMargin;
                newWindow.Top = maxBottom + WindowMargin;
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (sender is DesktopManagerWindow window && !isArrangingWindows)
            {
                // 分区被用户主动移动，重新开始计算连续尺寸吸附次数
                sizeSnapCounts.Remove(window.WindowId);

                // 检查是否需要吸附到其他窗口
                SnapWindowPosition(window);
            }
        }

        private void Window_SizeChanged(object sender, EventArgs e)
        {
            if (sender is DesktopManagerWindow window && !isArrangingWindows)
            {
                // 检查是否需要吸附调整大小
                SnapWindowSize(window);
            }
        }

        public DesktopManagerWindow GetWindowById(string windowId)
        {
            return managedWindows.FirstOrDefault(w => w.WindowId == windowId);
        }

        public DesktopManagerWindow GetWindowAt(Point screenPoint)
        {
            return managedWindows.FirstOrDefault(w => w.IsVisible && new Rect(w.Left, w.Top, w.Width, w.Height).Contains(screenPoint));
        }

        /// <summary>
        /// 从配置中删除指定标题的分区窗口
        /// </summary>
        /// <param name="windowId">窗口的唯一标识符</param>
        public void RemovePartitionWindow(DesktopManagerWindow window)
        {
            if (window == null || string.IsNullOrEmpty(window.WindowId))
            {
                return;
            }
            try
            {
                //还原图标
                if (window.DataContext is DesktopManagerViewModel viewModel)
                {
                    foreach (var icon in viewModel.Icons)
                    {
                        bool restored = DesktopIconService.Instance.ShowDesktopIcon(icon.IconPath);
                        if (restored)
                        {
                            Log.Information($"已恢复图标：{icon.IconPath}");
                        }
                    }
                }

                // 删除分区并更新配置
                PartitionDataService.Instance.RemoveWindow(window.WindowId);

                UnregisterWindow(window);
                window.Close();
                Log.Information($"已关闭分区窗口 {window.WindowId}");
            }
            catch (Exception ex)
            {
                Log.Information($"删除分区配置时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 窗口位置吸附功能
        /// </summary>
        /// <remarks>
        /// 位置吸附只改变分区坐标，不会改动长宽。
        /// </remarks>
        private void SnapWindowPosition(DesktopManagerWindow window)
        {
            var otherWindows = managedWindows
                    .Where(w => w != window && w.IsVisible)
                    .ToList();

            if (otherWindows.Count == 0)
            {
                return;
            }

            var workArea = SystemParameters.WorkArea;

            double newLeft = window.Left;
            double newTop = window.Top;
            bool leftSnapped = false;
            bool topSnapped = false;

            // 是否应该吸附到工作区边缘
            if (Math.Abs(window.Left - WindowMargin) < SnapThreshold)
            {
                newLeft = WindowMargin;
                leftSnapped = true;
            }
            else if (Math.Abs(window.Left + window.Width - (workArea.Width - WindowMargin)) < SnapThreshold)
            {
                newLeft = workArea.Width - window.Width - WindowMargin;
                leftSnapped = true;
            }

            if (Math.Abs(window.Top - WindowMargin) < SnapThreshold)
            {
                newTop = WindowMargin;
                topSnapped = true;
            }
            else if (Math.Abs(window.Top + window.Height - (workArea.Height - WindowMargin)) < SnapThreshold)
            {
                newTop = workArea.Height - window.Height - WindowMargin;
                topSnapped = true;
            }

            //是否应该吸附到其他窗口
            foreach (var otherWindow in otherWindows)
            {
                // 水平、垂直方向各自只认第一个命中的目标，
                // 多个候选互相覆盖会让分区在几个位置之间来回跳
                if (!leftSnapped)
                {
                    // 左边缘对齐
                    if (Math.Abs(window.Left - otherWindow.Left) < SnapThreshold)
                    {
                        newLeft = otherWindow.Left;
                        leftSnapped = true;
                    }
                    // 右边缘对齐
                    else if (Math.Abs(window.Left + window.Width - (otherWindow.Left + otherWindow.Width)) < SnapThreshold)
                    {
                        newLeft = otherWindow.Left + otherWindow.Width - window.Width;
                        leftSnapped = true;
                    }
                    // 右边缘对左边缘
                    else if (Math.Abs(window.Left + window.Width - (otherWindow.Left - WindowMargin)) < SnapThreshold)
                    {
                        newLeft = otherWindow.Left - window.Width - WindowMargin;
                        leftSnapped = true;
                    }
                    // 左边缘对右边缘
                    else if (Math.Abs(window.Left - (otherWindow.Left + otherWindow.Width + WindowMargin)) < SnapThreshold)
                    {
                        newLeft = otherWindow.Left + otherWindow.Width + WindowMargin;
                        leftSnapped = true;
                    }
                }

                if (!topSnapped)
                {
                    // 上边缘对齐
                    if (Math.Abs(window.Top - otherWindow.Top) < SnapThreshold)
                    {
                        newTop = otherWindow.Top;
                        topSnapped = true;
                    }
                    // 下边缘对齐
                    else if (Math.Abs(window.Top + window.Height - (otherWindow.Top + otherWindow.Height)) < SnapThreshold)
                    {
                        newTop = otherWindow.Top + otherWindow.Height - window.Height;
                        topSnapped = true;
                    }
                    // 下边缘对上边缘
                    else if (Math.Abs(window.Top + window.Height - (otherWindow.Top - WindowMargin)) < SnapThreshold)
                    {
                        newTop = otherWindow.Top - window.Height - WindowMargin;
                        topSnapped = true;
                    }
                    // 上边缘对下边缘
                    else if (Math.Abs(window.Top - (otherWindow.Top + otherWindow.Height + WindowMargin)) < SnapThreshold)
                    {
                        newTop = otherWindow.Top + otherWindow.Height + WindowMargin;
                        topSnapped = true;
                    }
                }
            }

            if (!leftSnapped && !topSnapped)
            {
                return;
            }

            // 位置没有变化时不再重复下发，
            // 否则会陷入「移动 → 触发 LocationChanged → 再移动」的无休止循环
            if (Math.Abs(newLeft - window.Left) < 0.5 && Math.Abs(newTop - window.Top) < 0.5)
            {
                return;
            }

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                window.Left = newLeft;
                window.Top = newTop;

                // 保存分区数据
                PartitionDataService.Instance.SavePartitionData(window);
            }));
        }

        /// <summary>
        /// 对齐单个分区的尺寸
        /// </summary>
        /// <remarks>
        /// 只对空分区有效（已经有内容的分区不允许因为对齐而改变尺寸），
        /// 用于用户结束拖动/缩放后再做一次对齐，避免在交互过程中与用户互相拉扯。
        /// </remarks>
        public void AlignWindowSize(DesktopManagerWindow window)
        {
            if (window == null || isArrangingWindows)
            {
                return;
            }

            SnapWindowSize(window);
        }

        /// <summary>
        /// 窗口大小吸附功能
        /// </summary>
        /// <remarks>
        /// 只有空分区（尚未放置内容）才允许在自动对齐时同时调整尺寸：
        /// 已经有内容的分区在拖动时通常只想移动位置，
        /// 若此时被强行改回尺寸，用户刚调好的长宽就白调了。
        /// </remarks>
        private void SnapWindowSize(DesktopManagerWindow window)
        {
            // 已经有内容的分区：只做位置吸附，绝不改动尺寸
            if (!window.IsEmptyPartition)
            {
                return;
            }

            // 交互过程中不做尺寸吸附，避免与用户的拖动互相拉扯造成抖动
            if (window.IsInteracting)
            {
                return;
            }

            // 获取除当前窗口外的所有可见窗口
            var otherWindows = managedWindows
                .Where(w => w != window && w.IsVisible)
                .ToList();

            if (otherWindows.Count == 0)
            {
                return;
            }
            var workArea = SystemParameters.WorkArea;

            double newWidth = window.Width;
            double newHeight = window.Height;
            bool widthSnapped = false;
            bool heightSnapped = false;

            // 是否应该吸附宽度到工作区边缘
            if (Math.Abs(window.Left + window.Width - (workArea.Width - WindowMargin)) < SnapThreshold)
            {
                newWidth = workArea.Width - window.Left - WindowMargin;
                widthSnapped = true;
            }

            // 是否应该吸附高度到工作区边缘
            if (Math.Abs(window.Top + window.Height - (workArea.Height - WindowMargin)) < SnapThreshold)
            {
                newHeight = workArea.Height - window.Top - WindowMargin;
                heightSnapped = true;
            }

            // 是否应该吸附到其他窗口的边缘
            foreach (var otherWindow in otherWindows)
            {
                // 宽高各自只认第一个命中的目标，
                // 多个候选互相覆盖会让窗口在几个尺寸之间来回跳
                if (!widthSnapped)
                {
                    // 右边缘吸附到其他窗口左边缘
                    if (Math.Abs(window.Left + window.Width - (otherWindow.Left - WindowMargin)) < SnapThreshold)
                    {
                        newWidth = otherWindow.Left - window.Left - WindowMargin;
                        widthSnapped = true;
                    }
                    // 右边缘吸附对齐
                    else if (Math.Abs(window.Left + window.Width - (otherWindow.Left + otherWindow.Width)) < SnapThreshold)
                    {
                        newWidth = otherWindow.Left + otherWindow.Width - window.Left;
                        widthSnapped = true;
                    }
                }

                if (!heightSnapped)
                {
                    // 下边缘吸附到其他窗口上边缘
                    if (Math.Abs(window.Top + window.Height - (otherWindow.Top - WindowMargin)) < SnapThreshold)
                    {
                        newHeight = otherWindow.Top - window.Top - WindowMargin;
                        heightSnapped = true;
                    }
                    // 下边缘吸附对齐
                    else if (Math.Abs(window.Top + window.Height - (otherWindow.Top + otherWindow.Height)) < SnapThreshold)
                    {
                        newHeight = otherWindow.Top + otherWindow.Height - window.Top;
                        heightSnapped = true;
                    }
                }
            }

            if (!widthSnapped && !heightSnapped)
            {
                return;
            }

            // 目标尺寸与当前一致时不再重复下发，
            // 否则会陷入「改尺寸 → 触发 SizeChanged → 再改尺寸」的无休止循环
            if (Math.Abs(newWidth - window.Width) < 0.5 && Math.Abs(newHeight - window.Height) < 0.5)
            {
                // 已经稳定下来，重置连续吸附计数
                sizeSnapCounts.Remove(window.WindowId);
                return;
            }

            // 连续改尺寸说明窗口和用户正在互相拉扯（甚至两个目标来回死循环），
            // 超过上限就停手，交回给用户控制
            int applied = sizeSnapCounts.TryGetValue(window.WindowId, out int count) ? count : 0;
            if (applied >= MaxConsecutiveSizeSnaps)
            {
                return;
            }
            sizeSnapCounts[window.WindowId] = applied + 1;

            // 同一时刻只允许一个尺寸吸附在排队，避免堆积成一长串任务把界面卡住
            if (!pendingSizeSnaps.Add(window.WindowId))
            {
                return;
            }

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    window.Width = newWidth;
                    window.Height = newHeight;

                    // 保存分区数据
                    PartitionDataService.Instance.SavePartitionData(window);
                }
                finally
                {
                    pendingSizeSnaps.Remove(window.WindowId);
                }
            }));
        }


        /// <summary>
        /// 更新所有窗口样式
        /// </summary>
        public void UpdateAllWindowStyles()
        {
            try
            {
                // 检查是否启用全局样式
                bool enableGlobalStyle = GeneralSettingsService.Instance.GetEnableGlobalStyle();
                var viewModels = managedWindows.Select(t => t.DataContext as DesktopManagerViewModel).ToList();

                if (enableGlobalStyle)
                {
                    // 加载全局设置
                    var globalSettings = PartitionSettingsService.Instance.LoadGlobalSettings();

                    // 应用到所有窗口
                    foreach (var viewModel in viewModels)
                    {
                        PartitionSettingsService.Instance.UpdateViewModelSettings(viewModel, globalSettings);
                    }
                }
                else
                {
                    // 加载每个分区的特定设置
                    foreach (var vm in viewModels)
                    {
                        var settings = PartitionSettingsService.Instance.LoadWindowSettings(vm.windowId);
                        PartitionSettingsService.Instance.UpdateViewModelSettings(vm, settings);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"更新所有窗口样式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取分区ViewModel
        /// </summary>
        public DesktopManagerViewModel GetDesktopManagerViewModel(string partitionId)
        {
            var viewModels = managedWindows.Select(t => t.DataContext as DesktopManagerViewModel).ToList();

            if (viewModels.Any(t => t.windowId.Equals(partitionId)))
            {
                return viewModels.Find(t => t.windowId.Equals(partitionId));
            }
            return null;
        }
    }
}
