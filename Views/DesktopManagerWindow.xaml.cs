using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Layouter.Models;
using Layouter.Services;
using Layouter.Utility;
using Layouter.ViewModels;
using static Layouter.Utility.ShellUtil;
using static System.Windows.Win32;
using System.Text;
using System.Net.NetworkInformation;
using Window = System.Windows.Window;

namespace Layouter.Views
{
    public partial class DesktopManagerWindow : Window
    {
        private DesktopManagerViewModel vm;
        private Point? dragStartPoint = null;
        private bool isDragging = false;
        private DesktopIcon draggedIcon = null;
        private bool isMouseOver = false;
        private bool isResizing = false; // 是否正在调整大小
        private double originalOpacity = 1.0; //窗口默认透明度
        private bool isAdjustingSize = false; // 是否正在调整窗口大小
        private bool isSnapPending = false;
        private DateTime lastSizeSnapTime = DateTime.MinValue;

        private DateTime lastClickTime = DateTime.MinValue;
        private DesktopIcon lastClickedIcon = null;
        private const double DoubleClickTimeThreshold = 500; //毫秒

        private bool isContainerClapsed = false;//图标容器是否折叠
        private double windowHeight = 0;

        // 窗口卷起状态
        private bool isRolledUp = false;
        // 标题栏高度
        private const double TitleBarHeight = 30;

        public DesktopManagerWindow()
        {
            InitializeComponent();
            WindowId = Guid.NewGuid().ToString();

            // 创建ViewModel并设置为DataContext
            vm = new DesktopManagerViewModel
            {
                Name = "新分区"
            };

            DataContext = vm;

            // 挂载事件
            Loaded += DesktopManagerWindow_Loaded;
            PreviewDragOver += DesktopManagerWindow_PreviewDragOver;
            MouseRightButtonDown += DesktopManagerWindow_MouseRightButtonDown;
            Closing += Window_Closing;

            // 窗口大小调整事件
            SizeChanged += Window_SizeChanged;
            // 窗口状态变化事件
            //StateChanged += Window_StateChanged;

            vm.LockStateChanged += (s, e) =>
            {
                UpdateLockState(vm.IsLocked);
            };

            // 初始化拖拽状态
            isDragging = false;

            // 注册到窗口管理服务
            WindowManagerService.Instance.RegisterWindow(this);

            windowHeight = this.Height;

            // 添加窗口消息钩子
            SourceInitialized += DesktopManagerWindow_SourceInitialized;
        }

        public DesktopManagerWindow(string windowId) : this()
        {
            WindowId = windowId;
        }

        public string WindowId { get; set; }

        #region 分区窗口事件

        private void DesktopManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SysUtil.SetDesktopLevelWindow(this);

            // 加载分区数据
            LoadPartitionData();

            // 设置分区样式
            ApplyStyleSettings();

            //根据配置切换窗口卷起状态
            if (vm.IsRolledUp)
            {
                isRolledUp = true;
                RollUp();
            }
        }

        private void DesktopManagerWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            //匹配普通文件
            bool flag = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("DraggedIconSource");

            // 检查是否包含Shell项目
            if (!flag)
            {
                var formats = e.Data.GetFormats();
                flag = formats.Contains("Shell IDList Array") ||
                              formats.Contains("CF_HDROP") ||
                              formats.Contains("FileGroupDescriptor");
            }

            // 支持文件拖拽
            if (flag)
            {
                // 必须包含 Copy：分区内部拖拽时 DoDragDrop 只声明了 Copy 权限，
                // 若这里只给出 Move，WPF 会判定落点不被接受，光标显示为禁止拖放。
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void DesktopManagerWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击位置是否在图标上
            var clickedElement = e.OriginalSource as FrameworkElement;
            while (clickedElement != null && !(clickedElement.Tag is DesktopIcon))
            {
                clickedElement = clickedElement.Parent as FrameworkElement;
            }

            if (clickedElement != null && clickedElement.Tag is DesktopIcon icon)
            {
                //加载系统右键菜单
                bool flag = ShellContextMenu.ShowContextMenuForIcon(icon, e.GetPosition(this), this);

                #region 自定义右键菜单
                if (!flag)
                {
                    var iconCtxMenu = this.TryFindResource("IconContextMenu") as ContextMenu;

                    if (iconCtxMenu != null)
                    {
                        iconCtxMenu.Tag = icon; //将图标传递给右键菜单项
                        iconCtxMenu.IsOpen = true;
                    }
                }

                #endregion
            }
            else
            {
                // 点击空白区域,不弹出图标右键菜单
                var contextMenu = this.TryFindResource("PartitionContextMenu") as System.Windows.Controls.ContextMenu;
                if (contextMenu != null)
                {
                    contextMenu.IsOpen = true;
                }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // 保存分区数据
            SavePartitionData();

            // 只隐藏窗口,不关闭应用程序
            e.Cancel = true;
            this.Hide();
        }

        private void DesktopManagerWindow_SourceInitialized(object sender, EventArgs e)
        {
            // 获取窗口句柄
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // 添加钩子处理窗口消息
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            //判断当前窗口是否正常调整大小 (替代Window_SizeChanged判定)
            if (msg == (int)WindowsMessage.WM_ENTERSIZEMOVE)
            {
                isResizing = true;
                UpdateWindowOpacity();
            }
            else if (msg == (int)WindowsMessage.WM_EXITSIZEMOVE)
            {
                if (isResizing && Mouse.LeftButton != MouseButtonState.Pressed)
                {
                    isResizing = false;
                    UpdateWindowOpacity();
                    QueueWindowSizeSnap();
                }
            }

            return IntPtr.Zero;
        }



        #endregion

        #region 分区数据管理

        private void SavePartitionData()
        {
            try
            {
                // 如果窗口处于卷起状态,保存当前窗口高度
                if (isRolledUp && windowHeight > this.Height)
                {
                    this.Height = windowHeight;
                }
                // 使用现有的服务保存分区数据
                PartitionDataService.Instance.SavePartitionData(this);

                //保存分区样式
                PartitionSettingsService.Instance.SaveWindowSettings(vm);
            }
            catch (Exception ex)
            {
                Log.Information($"保存分区数据时出错: {ex.Message}");
            }
        }

        private void LoadPartitionData()
        {
            try
            {
                // 使用现有的服务加载分区数据
                PartitionDataService.Instance.LoadPartitionData(this, this.WindowId);
            }
            catch (Exception ex)
            {
                Log.Information($"加载分区数据时出错: {ex.Message}");
            }
        }

        #endregion

        #region 窗口事件

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isMouseOver = true;
            UpdateWindowOpacity();

            // 如果窗口处于卷起状态,鼠标进入时临时展开
            if (isRolledUp)
            {
                TemporarilyUnroll();
            }
        }

        /// <summary>
        /// 鼠标离开窗口事件
        /// </summary>
        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isMouseOver = false;
            UpdateWindowOpacity();

            // 如果窗口处于卷起状态,鼠标离开时恢复卷起
            if (isRolledUp)
            {
                RollUp();
            }
        }

        /// <summary>
        /// 窗口大小变化事件
        /// </summary>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePartitionSizeFromLayout();

            // 调整窗口尺寸
            // 如果不是卷起状态,更新保存的窗口高度
            if (this.Height > TitleBarHeight)
            {
                windowHeight = this.Height;
            }

            vm.ArrangeIcons();
            UpdateWindowOpacity();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowOpacity();
        }

        private void UpdateWindowOpacity()
        {
            if (vm != null)
            {
                originalOpacity = vm.Opacity;

                if (isMouseOver || IsActive || isResizing)
                {
                    vm.ContentBackground = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                    this.Opacity = 1.0;
                }
                else
                {
                    vm.ContentBackground = new SolidColorBrush(Colors.Transparent);
                    this.Opacity = originalOpacity * 0.7;
                }
            }
        }

        /// <summary>
        /// 按图标大小的整倍数调整窗口尺寸
        /// </summary>
        private void SnapWindowSizeToIconGrid()
        {
            if (vm == null || vm.IsLocked || isRolledUp || isAdjustingSize)
            {
                return;
            }

            var cellSize = vm.GetIconCellSize();
            if (cellSize.Width <= 0 || cellSize.Height <= 0)
            {
                return;
            }

            var spacing = DesktopManagerViewModel.IconSpacing;
            var titleHeight = GetTitleBarHeight();
            var iconMargin = iconsContainer?.Margin ?? new Thickness(0);
            var availableWidth = Math.Max(cellSize.Width + spacing * 2, Width - iconMargin.Left - iconMargin.Right);
            var availableHeight = Math.Max(cellSize.Height + spacing * 2, Height - titleHeight - iconMargin.Top - iconMargin.Bottom);

            var columns = Math.Max(1, (int)Math.Round((availableWidth - spacing) / (cellSize.Width + spacing)));
            var rows = Math.Max(1, (int)Math.Round((availableHeight - spacing) / (cellSize.Height + spacing)));

            var newAvailableWidth = columns * cellSize.Width + (columns + 1) * spacing;
            var newAvailableHeight = rows * cellSize.Height + (rows + 1) * spacing;
            var newWidth = Math.Ceiling(newAvailableWidth + iconMargin.Left + iconMargin.Right);
            var newHeight = Math.Ceiling(titleHeight + newAvailableHeight + iconMargin.Top + iconMargin.Bottom);

            if (Math.Abs(Width - newWidth) > 0.5 || Math.Abs(Height - newHeight) > 0.5)
            {
                try
                {
                    isAdjustingSize = true;
                    Width = newWidth;
                    Height = newHeight;
                    UpdatePartitionSizeFromLayout();
                    vm.ArrangeIcons();
                    lastSizeSnapTime = DateTime.UtcNow;
                }
                finally
                {
                    isAdjustingSize = false;
                }
            }
        }

        private void QueueWindowSizeSnap()
        {
            if (isSnapPending || DateTime.UtcNow - lastSizeSnapTime < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            isSnapPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SnapWindowSizeToIconGrid();
                }
                finally
                {
                    isSnapPending = false;
                }
            }), DispatcherPriority.ContextIdle);
        }

        private void UpdatePartitionSizeFromLayout()
        {
            if (vm == null)
            {
                return;
            }

            var iconMargin = iconsContainer?.Margin ?? new Thickness(0);
            var width = IconsContainer?.ActualWidth > 0 ? IconsContainer.ActualWidth : Width;
            var height = IconsContainer?.ActualHeight > 0 ? IconsContainer.ActualHeight : Math.Max(0, Height - GetTitleBarHeight());

            vm.PartitionWidth = Math.Max(1, width - iconMargin.Left - iconMargin.Right);
            vm.PartitionHeight = Math.Max(1, height - iconMargin.Top - iconMargin.Bottom);
        }

        private double GetTitleBarHeight()
        {
            return TitleBar?.ActualHeight > 0 ? TitleBar.ActualHeight : TitleBarHeight;
        }

        #endregion

        #region 标题栏事件

        private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveTitleEdit();
            }
            else if (e.Key == Key.Escape)
            {
                // 取消编辑
                TitleTextBlock.Visibility = Visibility.Visible;
                TitleEditBox.Visibility = Visibility.Collapsed;
            }
        }

        private void TitleEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveTitleEdit();
        }

        private void TitleBar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 双击标题栏开始编辑名称
            ShowTitleEditor();
        }

        private void TitleTextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 检测是否是双击
            if (e.ClickCount == 2)
            {
                ShowTitleEditor();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // 创建右键菜单
            ContextMenu menu = new ContextMenu();

            // 添加窗口设置菜单项
            MenuItem settingsItem = new MenuItem() { Header = "窗口设置" };
            settingsItem.Click += (s, args) =>
            {
                var settingsWindow = new PartitionSettingsWindow(vm, false);
                settingsWindow.ShowDialog();
            };
            menu.Items.Add(settingsItem);

            // 添加卷起窗口菜单项
            MenuItem rollUpItem = new MenuItem() { Header = isRolledUp ? "展开窗口" : "卷起窗口" };
            rollUpItem.Click += (s, args) => ToggleRollUp();
            menu.Items.Add(rollUpItem);

            // 显示菜单
            menu.IsOpen = true;
        }

        private void NewPartition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建新的分区窗口
                var window = new DesktopManagerWindow();
                PartitionDataService.Instance.ShowWindow(window);

                Task.Delay(100).Wait();
                // 在新窗口显示后让标题变为可编辑状态
                window.EnableTitleEditOnFirstLoad();

                Log.Information("已创建新分区窗口");
            }
            catch (Exception ex)
            {
                Log.Information($"创建新分区窗口时出错: {ex.Message}");
            }
        }


        private void PartitionSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.IsOpen = false;

            try
            {
                var settingsWindow = new PartitionSettingsWindow(vm, false);
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();

                // 如果设置已保存,更新UI
                if (settingsWindow.DialogResult == true)
                {
                    // 保存分区数据
                    SavePartitionData();
                }
            }
            catch (Exception ex)
            {
                Log.Information($"打开分区设置窗口时出错: {ex.Message}");
            }
        }

        private void DeletePartition_Click(object sender, RoutedEventArgs e)
        {
            // 关闭设置菜单
            SettingsPopup.IsOpen = false;

            // 确认是否删除
            MessageBoxResult result = AppMessageBox.Show(
                "确定要删除该分区吗？分区内的所有图标将会恢复到桌面。",
                "删除确认",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.OK)
            {
                // 执行删除操作
                WindowManagerService.Instance.RemovePartitionWindow(this);
            }
        }

        private void AutoArrange_Click(object sender, RoutedEventArgs e)
        {
            // 关闭设置菜单
            SettingsPopup.IsOpen = false;

            // 执行自动排列
            var viewModel = DataContext as DesktopManagerViewModel;
            viewModel?.ArrangeIcons();
        }

        private void AlignPartitions_Click(object sender, RoutedEventArgs e)
        {
            // 关闭设置菜单
            SettingsPopup.IsOpen = false;

            WindowManagerService.Instance.ArrangeWindows();
        }


        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            vm.IsLocked = !vm.IsLocked;

            AppToastService.Info(vm.IsLocked ? "分区已锁定" : "分区已解锁",
                vm.IsLocked ? "锁定后无法拖动或调整分区大小" : "现在可以自由拖动分区");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 支持拖动窗口
            if (e.ClickCount == 1 && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                if (!vm.IsLocked)
                {
                    ResizeMode = ResizeMode.NoResize;
                    this.DragMove();
                }
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!vm.IsLocked)
            {
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
        }

        private void DoubleClickHandler(object sender, MouseButtonEventArgs e)
        {
            //SwitchContainerClapsedState();
            ToggleRollUp();
        }

        /// <summary>
        /// 在窗口初始化完成后自动开始编辑名称
        /// </summary>
        public void EnableTitleEditOnFirstLoad()
        {
            if (string.IsNullOrWhiteSpace(TitleTextBlock.Text) || TitleTextBlock.Text == "新分区")
            {
                // 使用Dispatcher延迟执行,确保UI已完全加载
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowTitleEditor();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void ShowTitleEditor()
        {
            try
            {
                // 隐藏标题文本
                TitleTextBlock.Visibility = Visibility.Collapsed;

                // 设置编辑框内容
                TitleEditBox.Text = TitleTextBlock.Text;

                // 显示编辑框
                TitleEditBox.Visibility = Visibility.Visible;

                // 聚焦并全选文本
                TitleEditBox.Focus();
                TitleEditBox.SelectAll();
            }
            catch (Exception ex)
            {
                Log.Information($"显示标题编辑器时出错: {ex.Message}");
            }
        }

        private void SaveTitleEdit()
        {
            try
            {
                // 保存新标题
                if (!string.IsNullOrWhiteSpace(TitleEditBox.Text))
                {
                    TitleTextBlock.Text = TitleEditBox.Text;

                    // 更新ViewModel中的名称
                    var viewModel = DataContext as DesktopManagerViewModel;
                    if (viewModel != null)
                    {
                        viewModel.Name = TitleEditBox.Text;

                        // 保存到配置
                        SavePartitionData();
                    }
                }

                // 恢复显示文本块
                TitleTextBlock.Visibility = Visibility.Visible;
                TitleEditBox.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Information($"保存标题编辑时出错: {ex.Message}");
            }
        }

        private void SwitchContainerClapsedState()
        {
            if (isContainerClapsed)
            {
                //展开
                IconsContainer.Visibility = Visibility.Visible;
                this.Height = windowHeight;
                isContainerClapsed = false;
            }
            else
            {
                //折叠
                IconsContainer.Visibility = Visibility.Collapsed;
                this.Height = TitleBar.Height;
                isContainerClapsed = true;
            }
        }

        #endregion

        #region 图标区域事件

        private void IconsContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击的是否是图标
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && !(element.Tag is DesktopIcon))
            {
                element = element.Parent as FrameworkElement;
            }

            if (element != null && element.Tag is DesktopIcon icon)
            {
                dragStartPoint = e.GetPosition(this);
                draggedIcon = icon;
            }
        }

        private void IconsContainer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && draggedIcon != null && !isDragging)
            {
                var currentPos = e.GetPosition(this);
                if (Math.Abs(currentPos.X - dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPos.Y - dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    try
                    {
                        // 开始拖拽操作
                        isDragging = true;

                        // 标记图标正在拖拽
                        draggedIcon.IsDragging = true;

                        // 创建拖拽数据 - 使用文件拖拽格式,这样桌面可以接受
                        var dataObject = new DataObject();
                        var originalIcon = draggedIcon;

                        // 图标实体当前位于「安全隐藏存储」中，存放名称是哈希摘要。
                        // 拖拽时必须提供实体文件路径，桌面才能正常接收这次拖放。
                        string storePath = originalIcon.IconPath;

                        // 拖拽前临时解除存放实体的隐藏属性，
                        // 否则资源管理器复制到桌面的副本会带上隐藏属性而看不见。
                        bool hiddenAttributeCleared = ClearHiddenAttributes(storePath);

                        // 将图标路径添加为可拖放的文件 - 这是关键,桌面只接受FileDrop格式
                        string[] files = new string[] { storePath };
                        dataObject.SetData(DataFormats.FileDrop, files);

                        // 同时添加我们自己的自定义数据
                        dataObject.SetData("DraggedIconSource", "Partition");
                        dataObject.SetData("IconId", originalIcon.Id);
                        dataObject.SetData("IconName", originalIcon.Name);
                        dataObject.SetData("IconPath", storePath);
                        dataObject.SetData("SourceWindowHandle", new WindowInteropHelper(this).Handle);

                        // 开始拖放操作
                        // 仅使用 Copy：隐藏存储中的实体文件必须保留在原处，
                        // 否则会被资源管理器「移动」走，导致存储索引失效。
                        var result = DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy);

                        // 拖拽结束，由「拖出方」统一判定落点并完成图标转移。
                        // 之前由目标窗口自行接收，落点落在标题栏等非图标区域时
                        // 目标窗口收不到 Drop 事件，图标就会被源窗口移除后直接丢失。
                        HandleDragEnd(originalIcon, result);

                        // 恢复存放实体的隐藏属性
                        if (hiddenAttributeCleared)
                        {
                            RestoreHiddenAttributes(storePath);
                        }

                        // 无论如何都要重置状态
                        isDragging = false;
                        if (draggedIcon != null)
                        {
                            draggedIcon.IsDragging = false;
                            draggedIcon = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"拖放操作异常: {ex.Message}\n{ex.StackTrace}");

                        // 出现异常时重置状态
                        isDragging = false;
                        if (draggedIcon != null)
                        {
                            draggedIcon.IsDragging = false;
                            draggedIcon = null;
                        }
                    }
                }
            }
        }

        #region 拖放落点处理

        /// <summary>
        /// 拖拽结束后由「拖出方」统一判定落点。
        /// <para>
        /// 之所以由拖出方处理而不是让目标窗口自己接收，是因为落点可能落在分区窗口的
        /// 标题栏等非图标区域上，目标窗口此时根本收不到 Drop 事件；
        /// 而旧实现会在拖出方无条件移除图标，结果就是跨分区拖动时图标直接丢失。
        /// </para>
        /// </summary>
        private void HandleDragEnd(DesktopIcon icon, DragDropEffects result)
        {
            if (icon == null)
            {
                return;
            }

            try
            {
                Win32.POINT endPt;
                Win32.GetCursorPos(out endPt);
                IntPtr hitWindow = Win32.WindowFromPoint(endPt);
                IntPtr rootWindow = Win32.GetAncestor(hitWindow, Win32.GA_ROOT);

                IntPtr selfHandle = new WindowInteropHelper(this).Handle;
                var screenPoint = new Point(endPt.X, endPt.Y);

                // 1) 落在其它分区窗口 → 把图标转移过去
                DesktopManagerWindow targetWindow = FindPartitionWindow(rootWindow);
                if (targetWindow != null && !ReferenceEquals(targetWindow, this))
                {
                    if (targetWindow.TransferIconFrom(icon, screenPoint))
                    {
                        var viewModel = this.DataContext as DesktopManagerViewModel;
                        viewModel?.RemoveIcon(icon);
                        SavePartitionData();
                        Log.Information("图标已转移到其它分区");
                    }
                    else
                    {
                        Log.Information("图标转移失败，仍保留在当前分区");
                    }
                    return;
                }

                // 2) 落在本分区 → 重新排序
                if (rootWindow == selfHandle)
                {
                    ReorderIcon(icon, GetIconContainerPoint(screenPoint));
                    return;
                }

                // 3) 落在桌面 → 还原到桌面，并保证桌面上显示的是原始名称
                if (IsDesktopTarget(rootWindow))
                {
                    if (result == DragDropEffects.None)
                    {
                        Log.Information("桌面拒绝了此次拖放，图标仍保留在分区中");
                        return;
                    }

                    RestoreIconToDesktop(icon);

                    var viewModel = this.DataContext as DesktopManagerViewModel;
                    viewModel?.RemoveIcon(icon);
                    SavePartitionData();
                    return;
                }

                // 4) 其它应用 → 保留在分区中，避免误删
                Log.Information($"图标被拖放到其它应用（{result}），仍保留在当前分区");
            }
            catch (Exception ex)
            {
                Log.Information($"处理拖放落点时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 根据顶层窗口句柄查找对应的分区窗口
        /// </summary>
        private DesktopManagerWindow FindPartitionWindow(IntPtr rootWindow)
        {
            if (rootWindow == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                foreach (var window in WindowManagerService.Instance.GetAllWindows())
                {
                    if (window == null)
                    {
                        continue;
                    }

                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero && handle == rootWindow)
                    {
                        return window;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"查找分区窗口失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 判断窗口句柄是否属于桌面（Progman 及其子窗口）
        /// </summary>
        private static bool IsDesktopTarget(IntPtr rootWindow)
        {
            if (rootWindow == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var builder = new StringBuilder(64);
                if (Win32.GetClassName(rootWindow, builder, builder.Capacity) <= 0)
                {
                    return false;
                }

                return string.Equals(builder.ToString(), "Progman", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Information($"判断桌面落点失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 把屏幕坐标转换为图标容器内的坐标
        /// </summary>
        private Point GetIconContainerPoint(Point screenPoint)
        {
            try
            {
                if (iconsContainer != null && iconsContainer.IsVisible)
                {
                    return iconsContainer.PointFromScreen(screenPoint);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"转换图标落点坐标失败: {ex.Message}");
            }

            return PointFromScreen(screenPoint);
        }

        /// <summary>
        /// 卡片内排序：把被拖动的图标移动到落点最近的图标位置
        /// </summary>
        private void ReorderIcon(DesktopIcon icon, Point dropPoint)
        {
            try
            {
                var viewModel = this.DataContext as DesktopManagerViewModel;
                if (viewModel == null || icon == null)
                {
                    return;
                }

                int currentIndex = viewModel.Icons.IndexOf(icon);
                if (currentIndex < 0)
                {
                    return;
                }

                int targetIndex = currentIndex;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < viewModel.Icons.Count; i++)
                {
                    var other = viewModel.Icons[i];
                    if (other == null || ReferenceEquals(other, icon))
                    {
                        continue;
                    }

                    double dx = dropPoint.X - other.Position.X;
                    double dy = dropPoint.Y - other.Position.Y;
                    double distance = dx * dx + dy * dy;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        targetIndex = i;
                    }
                }

                if (targetIndex != currentIndex)
                {
                    viewModel.Icons.Move(currentIndex, targetIndex);
                    Log.Information($"图标已在卡片内重新排序：{currentIndex} → {targetIndex}");
                }

                viewModel.ArrangeIcons();
                SavePartitionData();
            }
            catch (Exception ex)
            {
                Log.Information($"卡片内排序失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 接收来自其它分区的图标
        /// </summary>
        /// <param name="icon">被拖动的图标</param>
        /// <param name="screenPoint">落点（屏幕坐标）</param>
        /// <returns>是否接收成功</returns>
        public bool TransferIconFrom(DesktopIcon icon, Point screenPoint)
        {
            try
            {
                var viewModel = this.DataContext as DesktopManagerViewModel;
                if (viewModel == null || icon == null)
                {
                    return false;
                }

                // 目标分区已经存在同一个图标时不再重复添加
                if (viewModel.Icons.Any(i => i.IconPath != null
                    && icon.IconPath != null
                    && i.IconPath.Equals(icon.IconPath, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                var moved = new DesktopIcon
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = icon.Name,
                    IconPath = icon.IconPath,
                    IconType = icon.IconType,
                    Size = viewModel.GetIconSize(),
                    TextSize = viewModel.IconTextSize,
                    Position = GetIconContainerPoint(screenPoint)
                };

                viewModel.AddIcon(moved);
                viewModel.ArrangeIcons();
                SavePartitionData();
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"接收其它分区的图标失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 把图标还原到桌面，并保证桌面上显示的是原始名称
        /// </summary>
        private void RestoreIconToDesktop(DesktopIcon icon)
        {
            string storePath = icon?.IconPath;
            if (string.IsNullOrEmpty(storePath))
            {
                return;
            }

            // 还原后应当使用的原始名称
            string originalName = string.Empty;
            try
            {
                string originalPath = DesktopIconService.Instance.RemoveHiddenPathInIconPath(storePath);
                originalName = Path.GetFileName(originalPath);
            }
            catch (Exception ex)
            {
                Log.Information($"解析图标原始名称失败: {ex.Message}");
            }

            bool isStorePath = SecureHiddenStore.Instance.IsStorePath(storePath);

            // 1) 优先从安全隐藏存储还原，桌面上出现的是原始名称
            bool restored = DesktopIconService.Instance.ShowDesktopIcon(storePath);

            if (restored)
            {
                // 资源管理器复制到桌面的「哈希名」副本需要清理掉
                CleanupStrayStoreCopy(storePath);
                Log.Information($"图标已还原到桌面：{originalName}");
                return;
            }

            if (!isStorePath)
            {
                Log.Information($"图标不在隐藏存储中，无需还原：{storePath}");
                return;
            }

            // 2) 实体可能已被资源管理器「移动」到桌面（而不是复制），
            //    此时桌面上残留的就是哈希名称的副本，直接改名回原始名称。
            if (RenameStrayStoreCopy(storePath, originalName))
            {
                Log.Information($"桌面上的图标已重命名为原始名称：{originalName}");
            }
            else
            {
                Log.Information($"警告：无法恢复桌面上的图标：{storePath}");
            }
        }

        /// <summary>
        /// 把桌面上残留的「哈希名」副本改回原始名称
        /// </summary>
        /// <returns>是否改名成功</returns>
        private static bool RenameStrayStoreCopy(string storedPath, string originalName)
        {
            try
            {
                if (string.IsNullOrEmpty(storedPath) || string.IsNullOrEmpty(originalName))
                {
                    return false;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string strayPath = Path.Combine(desktopPath, Path.GetFileName(storedPath));
                string targetPath = Path.Combine(desktopPath, originalName);

                bool strayIsFile = File.Exists(strayPath);
                bool strayIsDirectory = Directory.Exists(strayPath);

                if (!strayIsFile && !strayIsDirectory)
                {
                    return false;
                }

                // 原始名称已被占用时保持现状，避免覆盖用户文件
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    Log.Information($"桌面已存在同名文件，未重命名：{originalName}");
                    return false;
                }

                if (strayIsFile)
                {
                    File.Move(strayPath, targetPath);
                }
                else
                {
                    Directory.Move(strayPath, targetPath);
                }

                ClearHiddenAttributes(targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"重命名桌面副本失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 临时解除存放实体的隐藏属性（拖拽期间使用）
        /// </summary>
        /// <returns>是否成功解除</returns>
        private static bool ClearHiddenAttributes(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return false;
                }

                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                {
                    return false;
                }

                attributes &= ~(FileAttributes.Hidden | FileAttributes.System);
                File.SetAttributes(path, attributes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"解除隐藏属性失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 恢复存放实体的隐藏属性
        /// </summary>
        private static void RestoreHiddenAttributes(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return;
                }

                FileAttributes attributes = File.GetAttributes(path);
                attributes |= FileAttributes.Hidden | FileAttributes.System;
                File.SetAttributes(path, attributes);
            }
            catch (Exception ex)
            {
                Log.Information($"恢复隐藏属性失败: {ex.Message}");
            }
        }

        #endregion

        private void IconsContainer_Drop(object sender, DragEventArgs e)
        {
            try
            {
                // 获取拖放的数据对象
                var data = e.Data;
                DesktopIcon newIcon = null;

                // 在拖放结束时获取鼠标坐标
                Point dropPoint = e.GetPosition((ItemsControl)sender);

                // 放置点坐标 - 以Panel为相对坐标系
                double x = dropPoint.X;
                double y = dropPoint.Y;

                var viewModel = DataContext as DesktopManagerViewModel;
                var iconSize = viewModel?.GetIconSize() ?? new Size(48, 48);

                // 判断是否是从另一个分区拖过来的图标(true:是,false:不是)
                bool flag = data.GetDataPresent("DraggedIconSource") && (data.GetData("DraggedIconSource").ToString() == "Partition");

                if (flag)
                {
                    // 来自其它分区的图标统一由「拖出方」处理：
                    // 目标窗口只负责标记事件已处理，避免出现重复添加或图标丢失。
                    e.Handled = true;
                    return;
                }

                // 检查是否是从其他来源拖过来的文件
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];

                    if (files != null && files.Length > 0)
                    {
                        // 创建一个图标列表,用于存储所有拖放的图标
                        List<DesktopIcon> newIcons = new List<DesktopIcon>();

                        // 处理所有拖放的文件
                        foreach (string filePath in files)
                        {
                            // 检查是否已经在这个分区中 - 如果已存在则跳过此文件
                            if (viewModel.Icons.Any(i => i.IconPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            // 为每个文件创建新的图标对象
                            DesktopIcon icon = new DesktopIcon
                            {
                                Id = Guid.NewGuid().ToString(),
                                Position = new Point(x, y),
                                Size = iconSize,
                                TextSize = viewModel.IconTextSize,
                                IconPath = DesktopIconService.Instance.CombineHiddenPathWithIconPath(filePath)
                            };

                            // 根据文件类型设置图标属性
                            if (filePath.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                            {
                                string userProfile = ShellUtil.GetDisplayCurrentUserName();
                                icon.Name = userProfile;
                                icon.IconPath = ShellUtil.MapSpecialFolderByName(userProfile);
                                icon.IconType = IconType.Shell;
                            }
                            else if (ShortCutUtil.IsShortcutPath(filePath))
                            {
                                icon.Name = Path.GetFileNameWithoutExtension(filePath);
                                icon.IconType = IconType.Shortcut;
                            }
                            else if (Directory.Exists(filePath))
                            {
                                icon.Name = new DirectoryInfo(filePath).Name;
                                icon.IconType = IconType.Folder;
                            }
                            else if (File.Exists(filePath))
                            {
                                icon.Name = new FileInfo(filePath).Name;
                                icon.IconType = IconType.File;
                            }
                            else
                            {
                                icon.Name = ShellUtil.GetSpecialFolderDisplayName(filePath);
                                icon.IconType = IconType.Shell;
                            }

                            if (flag)
                            {
                                //保持图标路径不变
                                icon.IconPath = filePath;
                            }

                            // 将图标添加到列表中
                            newIcons.Add(icon);

                            // 稍微偏移下一个图标的位置,避免完全重叠
                            x += 20;
                            y += 20;
                        }

                        // 如果有图标被添加,则使用第一个图标作为newIcon（用于后续处理）
                        if (newIcons.Count > 0)
                        {
                            newIcon = newIcons[0];

                            // 将所有图标添加到视图模型中
                            foreach (var icon in newIcons)
                            {
                                viewModel?.AddIcon(icon);
                            }
                        }
                    }
                }
                else
                {
                    var formats = e.Data.GetFormats();
                    if (formats.Contains("Shell IDList Array") || formats.Contains("CF_HDROP") || formats.Contains("FileGroupDescriptor"))
                    {
                        // 使用COM互操作获取Shell项目
                        var shellItems = ShellUtil.GetShellItemsFromDataObject(e.Data);

                        if (shellItems != null && shellItems.Count > 0)
                        {
                            // 创建一个图标列表,用于存储所有拖放的Shell项目
                            List<DesktopIcon> shellIcons = new List<DesktopIcon>();

                            // 处理所有拖放的Shell项目
                            foreach (var shellItem in shellItems)
                            {
                                // 为每个Shell项目创建图标
                                DesktopIcon icon = CreateIconFromShellItem(shellItem, dropPoint, iconSize);

                                if (icon != null)
                                {
                                    // 将图标添加到列表和视图模型中
                                    shellIcons.Add(icon);
                                    viewModel?.AddIcon(icon);

                                    // 稍微偏移下一个图标的位置,避免完全重叠
                                    dropPoint.X += 20;
                                    dropPoint.Y += 20;
                                }
                            }

                            // 如果有图标被添加,则使用第一个图标作为newIcon（用于后续处理）
                            if (shellIcons.Count > 0)
                            {
                                newIcon = shellIcons[0];
                            }
                        }
                    }
                }

                // 如果成功创建了新图标,保存分区数据
                if (newIcon != null)
                {
                    // 注意：图标已经在前面的代码中添加到视图模型中,这里不需要再次添加

                    // 保存分区数据
                    SavePartitionData();

                    // 如果是从桌面拖过来的文件,则隐藏桌面上的原图标
                    if (!flag && data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var files = data.GetData(DataFormats.FileDrop) as string[];
                        if (files != null && files.Length > 0)
                        {
                            // 处理所有拖放的文件
                            foreach (string filePath in files)
                            {
                                // 跳过特殊Shell类型的图标
                                bool isShellType = viewModel.Icons.Any(i => i.IconPath.Equals(filePath, StringComparison.OrdinalIgnoreCase) && i.IconType == IconType.Shell);
                                if (isShellType)
                                {
                                    continue;
                                }

                                //确保图标路径为桌面图标路径
                                string desktopPath = DesktopIconService.Instance.RemoveHiddenPathInIconPath(filePath);
                                // 隐藏桌面上的原图标
                                bool hidden = DesktopIconService.Instance.HideDesktopIcon(desktopPath);

                                if (!hidden)
                                {
                                    Log.Information($"警告：无法隐藏桌面上的图标,但仍会将其添加到分区：{desktopPath}");
                                }
                                else
                                {
                                    Log.Information($"成功隐藏桌面上的图标：{desktopPath}");
                                }
                            }
                        }
                    }
                    // 处理Shell类型的图标
                    foreach (var icon in viewModel.Icons.Where(i => i.IconType == IconType.Shell))
                    {
                        var filePath = DesktopIconService.Instance.RemoveHiddenPathInIconPath(icon.IconPath);
                        // 隐藏桌面上的原图标
                        DesktopIconService.Instance.HideDesktopIcon(filePath);
                    }

                    // 延迟执行排列,确保新图标已经加入集合
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        viewModel?.ArrangeIcons();
                    }));

                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"处理拖放时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private DesktopIcon CreateIconFromShellItem(ShellItemInfo item, Point point, Size iconSize)
        {
            var viewModel = DataContext as DesktopManagerViewModel;
            if (viewModel.Icons.Any(i => i.IconPath.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            // 创建新的图标对象
            var icon = new DesktopIcon
            {
                Id = Guid.NewGuid().ToString(),
                Name = item.DisplayName,
                IconPath = item.Path,
                Position = point,
                Size = iconSize,
                IconType = IconType.Shell
            };

            return icon;
        }

        #endregion

        #region 分区图标事件

        private void Icon_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击的是否是图标
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && !(element.Tag is DesktopIcon))
            {
                element = element.Parent as FrameworkElement;
            }

            if (element != null && element.Tag is DesktopIcon icon)
            {
                // 处理双击事件 - 打开文件或程序
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    return;
                }

                // 处理单击 - 准备拖拽
                dragStartPoint = e.GetPosition(this);
                draggedIcon = icon;
                e.Handled = true;
            }
        }

        private void Icon_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                // 如果正在拖动,则不处理双击
                return;
            }

            var element = sender as FrameworkElement;
            if (element?.Tag is DesktopIcon icon)
            {
                DateTime now = DateTime.Now;

                // 判断是否是双击（同一个图标在短时间内点击两次）
                if (lastClickedIcon == icon && (now - lastClickTime).TotalMilliseconds < DoubleClickTimeThreshold)
                {
                    // 双击,打开文件
                    OpenFileOrProgram(icon.IconPath);
                    e.Handled = true;

                    // 重置状态,防止连续多次打开
                    lastClickTime = DateTime.MinValue;
                    lastClickedIcon = null;
                }
                else
                {
                    // 单击,更新状态
                    lastClickTime = now;
                    lastClickedIcon = icon;
                }
            }
        }

        private void MenuItem_Open(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取菜单项的Tag,即对应的DesktopIcon对象
                var menuItem = sender as MenuItem;
                var contextMenu = menuItem?.Parent as System.Windows.Controls.ContextMenu;

                //获取在DesktopManagerWindow_MouseRightButtonDown事件中设置的Tag
                if (contextMenu?.Tag is DesktopIcon icon)
                {
                    // 使用我们的辅助方法打开文件
                    OpenFileOrProgram(icon.IconPath);
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"无法打开文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItem_Delete(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取菜单项的Tag,即对应的DesktopIcon对象
                var menuItem = sender as MenuItem;
                var contextMenu = menuItem?.Parent as System.Windows.Controls.ContextMenu;

                //获取在DesktopManagerWindow_MouseRightButtonDown事件中设置的Tag
                if (contextMenu?.Tag is DesktopIcon icon)
                {
                    // 确认是否删除
                    MessageBoxResult result = AppMessageBox.Show(
                        "确定要从分区中删除此图标吗？将会恢复桌面上对应的图标。",
                        "删除确认",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 恢复桌面图标
                        var desktopIconService = new DesktopIconService();
                        bool restored = desktopIconService.ShowDesktopIcon(icon.IconPath);

                        if (!restored)
                        {
                            Log.Information($"警告：无法恢复桌面上的图标,但仍会从分区中移除：{icon.IconPath}");
                        }

                        // 从分区移除图标
                        var viewModel = DataContext as DesktopManagerViewModel;
                        viewModel?.RemoveIcon(icon);

                        // 保存配置
                        SavePartitionData();
                    }
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"删除图标时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItem_Properties(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取菜单项的Tag,即对应的DesktopIcon对象
                var menuItem = sender as MenuItem;
                var contextMenu = menuItem?.Parent as System.Windows.Controls.ContextMenu;

                //获取在DesktopManagerWindow_MouseRightButtonDown事件中设置的Tag
                if (contextMenu?.Tag is DesktopIcon icon)
                {

                    ShellUtil.ShowProperties(icon.IconPath);
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"无法查看属性: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFileOrProgram(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    // 使用Windows默认处理方式打开文件
                    Win32.ShellExecute(IntPtr.Zero, "open", path, "", Path.GetDirectoryName(path), Win32.SW_SHOWNORMAL);
                }
                else if (!ShortCutUtil.IsShortcutPath(path))
                {
                    if (path.StartsWith("::") && !path.StartsWith(":::"))
                    {
                        path = $"shell:{path}";
                    }
                    ShellUtil.OpenSpecialFolder(path);
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"无法打开文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 分区窗口样式

        private void ApplyStyleSettings()
        {
            try
            {
                // 使用PartitionSettingsService应用样式配置
                PartitionSettingsService.Instance.ApplySettingsToViewModel(vm);

                // 更新图标大小
                UpdateIconSizes();

                // 更新锁定状态
                UpdateLockState(vm.IsLocked);
            }
            catch (Exception ex)
            {
                Log.Information($"应用样式配置时出错: {ex.Message}");
            }
        }

        #region 窗口卷起功能

        private void ToggleRollUp()
        {
            isRolledUp = !isRolledUp;

            if (isRolledUp)
            {
                RollUp();
            }
            else
            {
                Unroll();
            }

            // 保存设置
            //SavePartitionData();
        }

        /// <summary>
        /// 卷起窗口
        /// </summary>
        private void RollUp()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                IconsContainer.Visibility = Visibility.Collapsed;
                this.Height = TitleBar.Height;
                isContainerClapsed = true;

                // 隐藏功能按钮
                if (NewPartitionButton != null)
                {
                    NewPartitionButton.Visibility = Visibility.Collapsed;
                }
                if (SettingsButton != null)
                {
                    SettingsButton.Visibility = Visibility.Collapsed;
                }
                if (LockButton != null)
                {
                    LockButton.Visibility = Visibility.Collapsed;
                }

            }), System.Windows.Threading.DispatcherPriority.Loaded);

            vm.IsRolledUp = true;
        }

        /// <summary>
        /// 展开窗口
        /// </summary>
        private void Unroll()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                IconsContainer.Visibility = Visibility.Visible;
                this.Height = windowHeight;
                isContainerClapsed = false;

                // 显示功能按钮
                if (NewPartitionButton != null)
                {
                    NewPartitionButton.Visibility = Visibility.Visible;
                }
                if (SettingsButton != null)
                {
                    SettingsButton.Visibility = Visibility.Visible;
                }
                if (LockButton != null)
                {
                    LockButton.Visibility = Visibility.Visible;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            vm.IsRolledUp = false;
        }

        /// <summary>
        /// 临时展开窗口
        /// </summary>
        private void TemporarilyUnroll()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                IconsContainer.Visibility = Visibility.Visible;
                this.Height = windowHeight;
                isContainerClapsed = false;

                // 显示功能按钮
                if (NewPartitionButton != null)
                {
                    NewPartitionButton.Visibility = Visibility.Visible;
                }
                if (SettingsButton != null)
                {
                    SettingsButton.Visibility = Visibility.Visible;
                }
                if (LockButton != null)
                {
                    LockButton.Visibility = Visibility.Visible;
                }

                // 恢复窗口原始高度
                this.Height = windowHeight;

            }), System.Windows.Threading.DispatcherPriority.Loaded);

        }

        #endregion

        /// <summary>
        /// 更新图标大小
        /// </summary>
        public void UpdateIconSizes()
        {
            try
            {
                Size iconSize;
                switch (vm.IconSize)
                {
                    case IconSize.Small:
                        iconSize = new Size(32, 32);
                        break;
                    case IconSize.Large:
                        iconSize = new Size(64, 64);
                        break;
                    case IconSize.Medium:
                    default:
                        iconSize = new Size(48, 48);
                        break;
                }

                // 更新所有图标的尺寸
                foreach (var icon in vm.Icons)
                {
                    icon.Size = iconSize;
                    icon.TextSize = vm.IconTextSize;
                }

                // 重新排列图标
                vm.ArrangeIcons();
            }
            catch (Exception ex)
            {
                Log.Information($"更新图标大小时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新锁定状态
        /// </summary>
        private void UpdateLockState(bool isLocked)
        {
            LockIcon.Symbol = isLocked ? FluentIcons.Common.Symbol.LockClosed : FluentIcons.Common.Symbol.LockOpen;
            LockButton.ToolTip = isLocked ? "已锁定" : "未锁定";

            // 更新窗口可拖动和可调整大小状态
            if (isLocked)
            {
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
        }

        #endregion

        #region 数据同步

        public void Sync(DesktopManagerWindow window)
        {
            if (window == null)
            {
                return;
            }

            vm = window.DataContext as DesktopManagerViewModel;
            if (vm != null)
            {
                vm.windowId = window.WindowId;
                DataContext = vm;
            }

            // 应用样式设置
            ApplyStyleSettings();
        }

        #endregion

        #region 隐藏存储辅助

        /// <summary>
        /// 清理资源管理器在桌面留下的「存放路径」副本。
        /// 存放路径使用哈希命名，正常情况下桌面不会出现该名称的文件，
        /// 若存在则说明是本次拖放产生的临时副本，删除后只保留原始名称的文件。
        /// </summary>
        private static void CleanupStrayStoreCopy(string storedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(storedPath))
                {
                    return;
                }

                if (!SecureHiddenStore.Instance.IsStorePath(storedPath))
                {
                    return;
                }

                string strayName = Path.GetFileName(storedPath);
                if (string.IsNullOrEmpty(strayName))
                {
                    return;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string strayPath = Path.Combine(desktopPath, strayName);

                if (File.Exists(strayPath))
                {
                    File.Delete(strayPath);
                }
                else if (Directory.Exists(strayPath))
                {
                    Directory.Delete(strayPath, true);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"清理拖放副本时出错: {ex.Message}");
            }
        }

        #endregion

    }
}
