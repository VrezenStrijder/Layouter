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
        private Size sizeBeforeInteraction = Size.Empty; // 进入移动/缩放模态循环前的尺寸

        private DateTime lastClickTime = DateTime.MinValue;
        private DesktopIcon lastClickedIcon = null;
        private const double DoubleClickTimeThreshold = 500; //毫秒

        // 图标选中与重命名
        private DesktopIcon selectedIcon = null;
        private string renameOriginalName = null;
        private bool suppressSaveOnClose = false;

        private bool isContainerClapsed = false;//图标容器是否折叠
        private double windowHeight = 0;
        private bool isIconHeightSyncPending = false;//是否已排队图标高度校准

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

        /// <summary>
        /// 分区是否为空（尚未放置任何内容）
        /// </summary>
        /// <remarks>
        /// 对齐功能据此区分：已经有内容的分区只做位置吸附，不改尺寸；
        /// 空分区才允许在自动对齐时同时调整尺寸。
        /// </remarks>
        public bool IsEmptyPartition
        {
            get
            {
                return DataContext is not DesktopManagerViewModel viewModel || viewModel.Icons.Count == 0;
            }
        }

        /// <summary>
        /// 当前是否处于移动/缩放交互中
        /// </summary>
        /// <remarks>
        /// 交互过程中不做尺寸吸附，避免与用户的拖动互相拉扯造成抖动。
        /// </remarks>
        public bool IsInteracting => isResizing;

        #region 分区窗口事件

        private void DesktopManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SysUtil.SetDesktopLevelWindow(this);

            // 加载分区数据
            LoadPartitionData();

            // 同步图标显示方式按钮
            UpdateIconViewModeButton();

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
            // 还原备份后需要关闭窗口而不写回旧数据
            if (!suppressSaveOnClose)
            {
                // 保存分区数据
                SavePartitionData();
            }

            // 只隐藏窗口,不关闭应用程序
            e.Cancel = true;
            this.Hide();
        }

        /// <summary>
        /// 关闭窗口但不把当前（可能已经过时的）分区数据写回磁盘
        /// </summary>
        public void CloseWithoutSaving()
        {
            suppressSaveOnClose = true;
            try
            {
                Close();
            }
            finally
            {
                suppressSaveOnClose = false;
            }
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

                // 记录交互前的尺寸：移动与缩放都会进入这套模态循环，
                // 结束后靠尺寸是否变化来区分「移动」与「缩放」。
                sizeBeforeInteraction = new Size(Width, Height);

                UpdateWindowOpacity();
            }
            else if (msg == (int)WindowsMessage.WM_EXITSIZEMOVE)
            {
                if (isResizing)
                {
                    isResizing = false;
                    UpdateWindowOpacity();

                    // 鼠标仍按着说明这次拖动还没真正结束，先不做任何对齐
                    if (Mouse.LeftButton != MouseButtonState.Pressed)
                    {
                        // 只有真的改了尺寸才按图标网格对齐。
                        // 单纯移动位置时保持分区现有的长宽不变——
                        // 用户拖动分区通常只是想挪个位置，尺寸被改动就得重新调一遍。
                        if (Math.Abs(Width - sizeBeforeInteraction.Width) > 0.5
                            || Math.Abs(Height - sizeBeforeInteraction.Height) > 0.5)
                        {
                            QueueWindowSizeSnap();
                        }

                        // 交互结束后再做一次对齐：
                        // 空分区允许在自动对齐时调整尺寸，交互过程中不做，避免与用户互相拉扯
                        WindowManagerService.Instance.AlignWindowSize(this);
                    }
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
                // 窗口处于卷起状态时，实际高度只有标题栏高度。
                // 这里直接把「展开时的高度」写进配置文件，
                // 既不会让卷起的窗口突然弹开，下次启动也能恢复成原来的高度。
                double? heightOverride = isRolledUp && windowHeight > GetTitleBarHeight()
                    ? windowHeight
                    : (double?)null;

                // 使用现有的服务保存分区数据
                PartitionDataService.Instance.SavePartitionData(this, heightOverride);

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

                // 加载后同步图标显示方式按钮
                UpdateIconViewModeButton();

                // 依据加载到的图标位置计算内容高度，保证图标过多时可以滚动查看
                vm?.UpdateContentHeight();

                // 布局完成后按真实渲染高度校准（名称换行时图标更高）
                QueueIconItemHeightSync();
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
            // 卷起状态下窗口高度只剩标题栏高度，此时不能覆盖展开时的高度，
            // 否则展开后会停留在卷起后的小高度上。
            if (!isRolledUp && this.Height > GetTitleBarHeight())
            {
                windowHeight = this.Height;
            }

            vm.ArrangeIcons();
            UpdateWindowOpacity();

            // 图标名称换行后实际高度会变化，稍后按真实渲染高度校准
            QueueIconItemHeightSync();
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
        /// 按图标网格调整窗口尺寸
        /// </summary>
        /// <remarks>
        /// 用户拖动分区后，窗口会对齐成「n 行 m 列」的网格结构。
        /// 行数与列数由用户拖出的尺寸决定（能完整放下多少行/列就取多少），
        /// 而不是按图标总量把窗口撑满——放不下的图标交给垂直滚动条查看。
        /// </remarks>
        private void SnapWindowSizeToIconGrid()
        {
            if (vm == null || vm.IsLocked || isRolledUp || isAdjustingSize)
            {
                return;
            }

            // 空分区（尚未放置内容）不按图标网格约束尺寸：
            // 它的尺寸交给分区对齐功能处理，避免两套规则互相拉扯。
            if (vm.Icons.Count == 0)
            {
                return;
            }

            // 先按真实渲染高度校准：图标名称换行后实际高度更大，
            // 不校准的话这里会算出一个偏小的高度，行数就会少算。
            SyncIconItemHeight();

            var cellSize = vm.GetIconCellSize();
            if (cellSize.Width <= 0 || cellSize.Height <= 0)
            {
                return;
            }

            var spacing = DesktopManagerViewModel.IconSpacing;
            var titleHeight = GetTitleBarHeight();
            var iconMargin = GetIconContainerMargin();

            // 以实测的图标区尺寸为基准：窗口边框会占掉一部分宽高，
            // 直接用 Width/Height 推算容易差出半格，列数行数就会算错。
            double clientWidth = GetIconClientWidth();
            double clientHeight = GetIconClientHeight();
            double frameWidth = Math.Max(0, Width - clientWidth);
            double frameHeight = Math.Max(0, Height - titleHeight - clientHeight);

            double availableWidth = clientWidth - iconMargin.Left - iconMargin.Right;
            double availableHeight = clientHeight - iconMargin.Top - iconMargin.Bottom;

            double newWidth;
            double newHeight;
            double gridWidth;
            double gridHeight;

            if (vm.IsListView)
            {
                // 列表视图：宽度与图标网格无关，保持用户设定的宽度；
                // 高度按整行对齐，放不下的行由滚动条承载。
                int rows = Math.Max(1, (int)Math.Floor((availableHeight - spacing) / DesktopManagerViewModel.ListRowHeight));

                gridWidth = availableWidth;
                gridHeight = rows * DesktopManagerViewModel.ListRowHeight;

                newWidth = Width;
                newHeight = Math.Ceiling(titleHeight + gridHeight + iconMargin.Top + iconMargin.Bottom + frameHeight);
            }
            else
            {
                // 网格视图：按用户拖出的尺寸算出能完整放下的行列数，
                // 再把窗口对齐成这个 n 行 m 列的网格。
                int columns = vm.CalculateColumnCount(availableWidth - GetReservedScrollBarWidth());
                int rows = vm.CalculateRowCount(availableHeight);

                // 网格尺寸统一由 ViewModel 计算，避免视图层再抄一份公式
                gridWidth = vm.GetGridWidth(columns);
                gridHeight = vm.GetGridHeight(rows);

                newWidth = Math.Ceiling(gridWidth + iconMargin.Left + iconMargin.Right
                    + GetReservedScrollBarWidth() + frameWidth);
                newHeight = Math.Ceiling(titleHeight + gridHeight + iconMargin.Top + iconMargin.Bottom + frameHeight);
            }

            if (Math.Abs(Width - newWidth) > 0.5 || Math.Abs(Height - newHeight) > 0.5)
            {
                try
                {
                    isAdjustingSize = true;
                    Width = newWidth;
                    Height = newHeight;

                    // 窗口尺寸刚改完，实测宽度还没刷新，这里直接按新网格更新分区尺寸，
                    // 免得用旧宽度算列数，导致排布和窗口对不上。
                    UpdatePartitionSizeFromGrid(gridWidth, gridHeight);

                    vm.ArrangeIcons();

                    lastSizeSnapTime = DateTime.UtcNow;
                }
                finally
                {
                    isAdjustingSize = false;
                }
            }
        }

        /// <summary>
        /// 获取当前图标容器的内边距
        /// </summary>
        private Thickness GetIconContainerMargin()
        {
            var container = vm != null && vm.IsListView ? iconsListContainer : iconsContainer;
            return container?.Margin ?? new Thickness(0);
        }

        /// <summary>
        /// 获取图标区（滚动容器）的实际宽度
        /// </summary>
        private double GetIconClientWidth()
        {
            var viewer = vm != null && vm.IsListView ? IconsListScrollViewer : IconsScrollViewer;

            if (viewer != null && viewer.ActualWidth > 0)
            {
                return viewer.ActualWidth;
            }

            return Width;
        }

        /// <summary>
        /// 获取图标区（滚动容器）的实际高度
        /// </summary>
        private double GetIconClientHeight()
        {
            if (IconsContainer != null && IconsContainer.ActualHeight > 0)
            {
                return IconsContainer.ActualHeight;
            }

            return Math.Max(0, Height - GetTitleBarHeight());
        }

        /// <summary>
        /// 获取网格视图需要预留的垂直滚动条宽度
        /// </summary>
        /// <remarks>
        /// 网格视图固定按「预留滚动条」的宽度排布，滚动条是否真的显示由图标数量决定。
        /// 预留宽度必须与实际渲染出来的滚动条一致，否则滚动条出现时会挡住最右侧一列。
        /// </remarks>
        private double GetReservedScrollBarWidth()
        {
            double width = 0;

            try
            {
                if (IconsScrollViewer != null
                    && IconsScrollViewer.Template != null
                    && IconsScrollViewer.Template.FindName("VerticalScrollBar", IconsScrollViewer) is System.Windows.Controls.Primitives.ScrollBar scrollBar)
                {
                    width = scrollBar.Width;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"读取垂直滚动条宽度失败: {ex.Message}");
            }

            return width > 0 ? width : SystemParameters.VerticalScrollBarWidth;
        }

        /// <summary>
        /// 查找网格视图中承载图标的 Canvas
        /// </summary>
        private Canvas FindIconCanvas()
        {
            if (iconsContainer == null)
            {
                return null;
            }

            return FindCanvasInVisualTree(iconsContainer);
        }

        private static Canvas FindCanvasInVisualTree(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is Canvas canvas)
                {
                    return canvas;
                }

                Canvas found = FindCanvasInVisualTree(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 测量网格视图中真实渲染出来的图标项高度
        /// </summary>
        private double MeasureIconItemHeight()
        {
            if (vm == null || vm.IsListView)
            {
                return 0;
            }

            Canvas canvas = FindIconCanvas();
            if (canvas == null)
            {
                return 0;
            }

            double max = 0;

            foreach (UIElement child in canvas.Children)
            {
                if (child is FrameworkElement element && element.ActualHeight > 0)
                {
                    max = Math.Max(max, element.ActualHeight);
                }
            }

            return max;
        }

        /// <summary>
        /// 按真实渲染高度校准图标项高度
        /// </summary>
        /// <remarks>
        /// 图标名称过长时会换行，真实高度比按图标尺寸估算的高度更大。
        /// 校准后排布与内容高度才与实际显示一致，自动高度也不会算小。
        /// </remarks>
        private void SyncIconItemHeight()
        {
            if (vm == null || vm.IsListView)
            {
                return;
            }

            double measured = MeasureIconItemHeight();
            if (measured <= 0)
            {
                return;
            }

            if (Math.Abs(vm.MeasuredItemHeight - measured) < 0.5)
            {
                return;
            }

            vm.MeasuredItemHeight = measured;
            vm.ArrangeIcons();
        }

        /// <summary>
        /// 延迟校准图标项高度（等待布局完成后再测量）
        /// </summary>
        private void QueueIconItemHeightSync()
        {
            if (isIconHeightSyncPending)
            {
                return;
            }

            isIconHeightSyncPending = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SyncIconItemHeight();
                }
                catch (Exception ex)
                {
                    Log.Information($"校准图标高度失败: {ex.Message}");
                }
                finally
                {
                    isIconHeightSyncPending = false;
                }
            }), DispatcherPriority.ContextIdle);
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

        /// <summary>
        /// 按当前布局更新分区尺寸
        /// </summary>
        private void UpdatePartitionSizeFromLayout()
        {
            if (vm == null)
            {
                return;
            }

            var iconMargin = GetIconContainerMargin();

            double clientWidth = GetIconClientWidth();
            double clientHeight = GetIconClientHeight();

            double width = clientWidth - iconMargin.Left - iconMargin.Right;
            double height = clientHeight - iconMargin.Top - iconMargin.Bottom;

            if (!vm.IsListView)
            {
                // 网格视图固定预留垂直滚动条的宽度：滚动条是否显示取决于图标数量，
                // 若按「当前可视宽度」排布，滚动条出现/隐藏会引起列数来回变化。
                width -= GetReservedScrollBarWidth();
            }

            UpdatePartitionSizeFromGrid(width, height);
        }

        /// <summary>
        /// 按图标内容区尺寸更新分区尺寸
        /// </summary>
        /// <remarks>
        /// 网格视图的列数、列表视图的行数都基于这个尺寸计算，
        /// 因此这里给的是扣除内边距与预留滚动条之后的净尺寸。
        /// </remarks>
        private void UpdatePartitionSizeFromGrid(double contentWidth, double contentHeight)
        {
            if (vm == null)
            {
                return;
            }

            vm.PartitionWidth = Math.Max(1, contentWidth);
            vm.PartitionHeight = Math.Max(1, contentHeight);
        }

        private double GetTitleBarHeight()
        {
            return TitleBar?.ActualHeight > 0 ? TitleBar.ActualHeight : TitleBarHeight;
        }

        /// <summary>
        /// 切换图标显示方式（网格 / 列表）
        /// </summary>
        private void ToggleIconViewMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (vm == null)
                {
                    return;
                }

                vm.IconViewMode = vm.IsGridView ? IconViewMode.List : IconViewMode.Grid;

                UpdateIconViewModeButton();
                SavePartitionData();
            }
            catch (Exception ex)
            {
                Log.Information($"切换图标显示方式时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 同步标题栏上的显示方式按钮
        /// </summary>
        private void UpdateIconViewModeButton()
        {
            try
            {
                if (IconViewModeIcon == null)
                {
                    return;
                }

                bool listMode = vm != null && vm.IsListView;
                IconViewModeIcon.Symbol = listMode
                    ? FluentIcons.Common.Symbol.List
                    : FluentIcons.Common.Symbol.Grid;

                if (IconViewModeButton != null)
                {
                    IconViewModeButton.ToolTip = listMode ? "切换为网格视图" : "切换为列表视图";
                }
            }
            catch (Exception ex)
            {
                Log.Information($"同步图标显示方式按钮时出错: {ex.Message}");
            }
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
        /// 把屏幕坐标转换为图标内容坐标系中的坐标
        /// </summary>
        /// <remarks>
        /// 图标区域外面套了滚动条，滚动后容器原点与内容原点不再重合，
        /// 这里统一把滚动偏移量补上，图标落点与排序才能对得上。
        /// </remarks>
        private Point GetIconContainerPoint(Point screenPoint)
        {
            try
            {
                if (iconsContainer != null && iconsContainer.IsVisible)
                {
                    Point point = iconsContainer.PointFromScreen(screenPoint);
                    point.Y += GetIconScrollOffset();
                    return point;
                }

                if (iconsListContainer != null && iconsListContainer.IsVisible)
                {
                    Point point = iconsListContainer.PointFromScreen(screenPoint);
                    point.Y += GetIconScrollOffset();
                    return point;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"转换图标落点坐标失败: {ex.Message}");
            }

            return PointFromScreen(screenPoint);
        }

        /// <summary>
        /// 获取图标区域的垂直滚动偏移量
        /// </summary>
        private double GetIconScrollOffset()
        {
            try
            {
                if (vm != null && vm.IsListView)
                {
                    return IconsListScrollViewer?.VerticalOffset ?? 0;
                }

                return IconsScrollViewer?.VerticalOffset ?? 0;
            }
            catch
            {
                return 0;
            }
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

                // 列表视图按行排列，直接按落点所在行计算目标位置
                if (viewModel.IsListView)
                {
                    ReorderIconInList(viewModel, icon, dropPoint, currentIndex);
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
        /// 列表视图下的排序：按落点所在的行计算目标位置
        /// </summary>
        private void ReorderIconInList(DesktopManagerViewModel viewModel, DesktopIcon icon, Point dropPoint, int currentIndex)
        {
            // 列表单行高度（与列表模板中的 MinHeight 保持一致）
            const double rowHeight = 30;

            // 列表容器内边距为 5
            int targetIndex = (int)Math.Floor((dropPoint.Y - 5) / rowHeight);
            targetIndex = Math.Max(0, Math.Min(targetIndex, viewModel.Icons.Count - 1));

            if (targetIndex != currentIndex)
            {
                viewModel.Icons.Move(currentIndex, targetIndex);
                Log.Information($"图标已在列表内重新排序：{currentIndex} → {targetIndex}");
            }

            viewModel.ArrangeIcons();
            SavePartitionData();
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
                // 在拖放结束时获取鼠标坐标
                Point dropPoint = e.GetPosition((ItemsControl)sender);

                // 图标区域可能处于滚动状态，落点需要换算到图标内容坐标系
                if (ReferenceEquals(sender, iconsContainer))
                {
                    dropPoint.Y += IconsScrollViewer?.VerticalOffset ?? 0;
                }
                else if (ReferenceEquals(sender, iconsListContainer))
                {
                    dropPoint.Y += IconsListScrollViewer?.VerticalOffset ?? 0;
                }

                if (HandleIconDrop(e.Data, dropPoint))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"处理拖放时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 处理图标拖放
        /// </summary>
        /// <remarks>
        /// 与 <see cref="IconsContainer_Drop"/> 的事件管道解耦，便于自动化验证真实的拖放逻辑。
        /// </remarks>
        /// <param name="data">拖放携带的数据</param>
        /// <param name="dropPoint">落点坐标（相对图标容器）</param>
        /// <returns>是否已处理该次拖放</returns>
        private bool HandleIconDrop(IDataObject data, Point dropPoint)
        {
            try
            {
                DesktopIcon newIcon = null;

                // 本次拖入的条目：记录原始路径与对应图标，便于搬运完成后回写路径
                var droppedItems = new List<DroppedItem>();

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
                    return true;
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
                            // 分区里已有的图标记的是存放路径，这里把两种写法都比对一遍
                            string predictedStoredPath = DesktopIconService.Instance.CombineHiddenPathWithIconPath(filePath);
                            if (viewModel.Icons.Any(i =>
                                    string.Equals(i.IconPath, filePath, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(i.IconPath, predictedStoredPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            // 为每个文件创建新的图标对象
                            // 图标路径先指向桌面上的原始文件：搬运开始前图标就能正确显示，
                            // 搬运成功后再切换成存放路径；搬运失败则保持原路径，文件仍在桌面上。
                            DesktopIcon icon = new DesktopIcon
                            {
                                Id = Guid.NewGuid().ToString(),
                                Position = new Point(x, y),
                                Size = iconSize,
                                TextSize = viewModel.IconTextSize,
                                IconPath = filePath
                            };

                            // 是否需要搬运到隐藏存储（Shell 虚拟项只需切换可见性）
                            bool needsMove = true;

                            // 根据文件类型设置图标属性
                            if (filePath.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                            {
                                string userProfile = ShellUtil.GetDisplayCurrentUserName();
                                icon.Name = userProfile;
                                icon.IconPath = ShellUtil.MapSpecialFolderByName(userProfile);
                                icon.IconType = IconType.Shell;
                                needsMove = false;
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
                                needsMove = false;
                            }

                            if (flag)
                            {
                                //保持图标路径不变
                                icon.IconPath = filePath;
                            }

                            // 将图标添加到列表中
                            newIcons.Add(icon);

                            droppedItems.Add(new DroppedItem
                            {
                                OriginalPath = filePath,
                                Icon = icon,
                                NeedsMove = needsMove
                            });

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
                    var formats = data.GetFormats();
                    if (formats.Contains("Shell IDList Array") || formats.Contains("CF_HDROP") || formats.Contains("FileGroupDescriptor"))
                    {
                        // 使用COM互操作获取Shell项目
                        var shellItems = ShellUtil.GetShellItemsFromDataObject(data);

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

                    // 先把分区数据落盘：此刻图标记录的是桌面上的原始路径，
                    // 搬运完成后会再保存一次（记录存放路径）。
                    // 这样即使搬运途中程序退出，重新启动也能凭原始路径找回文件。
                    SavePartitionData();

                    // 收集需要搬到隐藏存储的项目（Shell 虚拟项只切换可见性，不搬运）
                    var hideTargets = new List<HideTarget>();

                    if (!flag)
                    {
                        foreach (var item in droppedItems)
                        {
                            if (item.NeedsMove)
                            {
                                hideTargets.Add(new HideTarget { OriginalPath = item.OriginalPath, Icon = item.Icon });
                            }
                        }
                    }

                    // 处理本次拖入的 Shell 类型图标（只切换可见性，不搬运文件）
                    foreach (var item in droppedItems.Where(d => d.Icon.IconType == IconType.Shell))
                    {
                        string shellPath = DesktopIconService.Instance.RemoveHiddenPathInIconPath(item.Icon.IconPath);
                        DesktopIconService.Instance.HideDesktopIcon(shellPath, out _, refreshDesktop: false);
                    }

                    // 搬运放到后台线程：文件可能很大、目录可能包含上万个文件，
                    // 放在界面线程上会让整个程序假死。
                    // 图标已经先加入分区，用户可以立即继续操作。
                    StartBackgroundHide(hideTargets);

                    // 延迟执行排列,确保新图标已经加入集合
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        viewModel?.ArrangeIcons();

                        // 新加入的图标可能名称更长（换行后更高），重新校准高度
                        QueueIconItemHeightSync();
                    }));

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Information($"处理拖放时出错: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 后台搬运的目标：桌面原始路径 + 分区里对应的图标
        /// </summary>
        private class HideTarget
        {
            public string OriginalPath { get; set; } = string.Empty;
            public DesktopIcon Icon { get; set; }
        }

        /// <summary>
        /// 本次拖入的条目：原始路径 + 图标 + 是否需要搬运到隐藏存储
        /// </summary>
        private class DroppedItem
        {
            public string OriginalPath { get; set; } = string.Empty;
            public DesktopIcon Icon { get; set; }
            public bool NeedsMove { get; set; }
        }

        /// <summary>
        /// 搬运队列：同一时间只允许一批搬运在执行，避免多批次同时写索引互相覆盖
        /// </summary>
        private static readonly SemaphoreSlim hideQueue = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 在后台线程完成「把桌面项目搬进隐藏存储」的工作
        /// </summary>
        /// <remarks>
        /// 大文件、大目录的搬运可能耗时数秒，放在界面线程上会直接冻结整个程序；
        /// 这里放到后台串行执行，界面全程可操作。
        /// </remarks>
        private void StartBackgroundHide(List<HideTarget> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await hideQueue.WaitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Information($"等待搬运队列失败: {ex.Message}");
                    return;
                }

                try
                {
                    var started = DateTime.UtcNow;

                    // 标题栏显示搬运进度
                    await Dispatcher.InvokeAsync(() => vm?.BeginTransfer(targets.Count));

                    var results = DesktopIconService.Instance.HideDesktopIcons(
                        targets.Select(t => t.OriginalPath).ToList(),
                        (completed, total) =>
                        {
                            // 进度回调发生在后台线程，需要切回界面线程刷新标题栏
                            Dispatcher.Invoke(() => vm?.UpdateTransfer(completed, total));
                        });

                    // 结果写回界面（图标路径要换成真实存放路径）
                    await Dispatcher.InvokeAsync(() =>
                    {
                        int succeeded = 0;

                        for (int i = 0; i < results.Count && i < targets.Count; i++)
                        {
                            HideIconResult result = results[i];
                            HideTarget target = targets[i];

                            if (result.Success)
                            {
                                succeeded++;

                                if (target.Icon != null)
                                {
                                    target.Icon.IconPath = result.StoredPath;
                                }
                            }
                            else if (target.Icon != null)
                            {
                                // 搬运失败时文件仍在桌面，图标保持原始路径，
                                // 避免分区里留下打不开的空壳图标
                                target.Icon.IconPath = target.OriginalPath;
                                Log.Information($"搬运失败，图标保留原始路径: {target.OriginalPath}");
                            }
                        }

                        // 路径已更新，重新落盘
                        SavePartitionData();
                        vm?.ArrangeIcons();

                        // 结束进度显示
                        vm?.EndTransfer();

                        double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                        Log.Information($"后台搬运完成：成功 {succeeded} / {results.Count}，耗时 {elapsed:F1} 秒");
                    });
                }
                catch (Exception ex)
                {
                    Log.Information($"后台搬运失败: {ex.Message}");

                    // 出错也要把进度条收掉，否则会一直显示「正在移动」
                    await Dispatcher.InvokeAsync(() => vm?.EndTransfer());
                }
                finally
                {
                    hideQueue.Release();
                }
            });
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
                // 重命名编辑框内的点击不参与拖拽与选中
                if (IsInRenameEditor(e.OriginalSource))
                {
                    return;
                }

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

        /// <summary>
        /// 判断事件源是否位于重命名编辑框内
        /// </summary>
        private static bool IsInRenameEditor(object source)
        {
            DependencyObject current = source as DependencyObject;

            while (current != null)
            {
                if (current is TextBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
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
                // 重命名编辑框内的点击交给编辑框自己处理
                if (IsInRenameEditor(e.OriginalSource))
                {
                    return;
                }

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

                    if (ReferenceEquals(selectedIcon, icon))
                    {
                        // 已经选中的图标再次单击 → 进入重命名
                        BeginRenameIcon(icon);
                    }
                    else
                    {
                        // 首次单击 → 选中
                        SelectIcon(icon);
                    }
                }
            }
        }

        #region 图标选中与重命名

        /// <summary>
        /// 选中图标（同时取消其它图标的选中状态）
        /// </summary>
        private void SelectIcon(DesktopIcon icon)
        {
            if (selectedIcon != null && !ReferenceEquals(selectedIcon, icon))
            {
                selectedIcon.IsSelected = false;
            }

            selectedIcon = icon;

            if (selectedIcon != null)
            {
                selectedIcon.IsSelected = true;
            }
        }

        /// <summary>
        /// 开始重命名图标
        /// </summary>
        private void BeginRenameIcon(DesktopIcon icon)
        {
            if (icon == null)
            {
                return;
            }

            SelectIcon(icon);
            renameOriginalName = icon.Name;
            icon.IsRenaming = true;

            // 重命名编辑框是在模板里一次性创建的（初始为折叠状态），
            // Loaded 事件不会在每次进入重命名时再次触发，
            // 因此这里主动定位编辑框并全选当前名称，方便直接输入覆盖。
            Dispatcher.BeginInvoke(new Action(() => SelectRenameText(icon)), DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 定位指定图标的重命名编辑框并全选其中的文本
        /// </summary>
        private void SelectRenameText(DesktopIcon icon)
        {
            try
            {
                TextBox textBox = FindRenameTextBox(this, icon);
                if (textBox == null)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            }
            catch (Exception ex)
            {
                Log.Information($"全选重命名文本失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在可视树中查找指定图标对应的重命名编辑框（网格视图与列表视图各有一份）
        /// </summary>
        private static TextBox FindRenameTextBox(DependencyObject root, DesktopIcon icon)
        {
            if (root == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is TextBox textBox
                    && textBox.Visibility == Visibility.Visible
                    && textBox.DataContext is DesktopIcon target
                    && ReferenceEquals(target, icon))
                {
                    return textBox;
                }

                TextBox found = FindRenameTextBox(child, icon);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 提交重命名
        /// </summary>
        private void CommitRename(DesktopIcon icon)
        {
            if (icon == null || !icon.IsRenaming)
            {
                return;
            }

            string original = renameOriginalName;
            icon.IsRenaming = false;
            renameOriginalName = null;

            string name = icon.Name?.Trim();
            icon.Name = string.IsNullOrEmpty(name) ? (original ?? string.Empty) : name;

            // 名称变化可能让图标变高（换行），稍后重新校准高度
            QueueIconItemHeightSync();

            // 重命名后立即保存，保证重启 / 备份后依然生效
            SavePartitionData();
        }

        /// <summary>
        /// 取消重命名
        /// </summary>
        private void CancelRename(DesktopIcon icon)
        {
            if (icon == null || !icon.IsRenaming)
            {
                return;
            }

            if (!string.IsNullOrEmpty(renameOriginalName))
            {
                icon.Name = renameOriginalName;
            }

            icon.IsRenaming = false;
            renameOriginalName = null;
        }

        /// <summary>
        /// F2 键：重命名当前选中的图标
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                e.Handled = true;

                if (selectedIcon == null)
                {
                    // 没有选中图标时，取最后一个被点击过的图标
                    selectedIcon = lastClickedIcon;
                }

                BeginRenameIcon(selectedIcon);
            }
            else if (e.Key == Key.Escape)
            {
                if (selectedIcon != null && selectedIcon.IsRenaming)
                {
                    e.Handled = true;
                    CancelRename(selectedIcon);
                }
            }
        }

        private void IconRenameBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        private void IconRenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            var icon = textBox?.DataContext as DesktopIcon;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitRename(icon);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRename(icon);
            }
        }

        private void IconRenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            var icon = textBox?.DataContext as DesktopIcon;

            if (icon != null && icon.IsRenaming)
            {
                CommitRename(icon);
            }
        }

        #endregion

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
            if (isRolledUp)
            {
                isRolledUp = false;
                Unroll();
            }
            else
            {
                // 卷起前先记下展开时的高度，展开时按此恢复
                if (this.Height > GetTitleBarHeight())
                {
                    windowHeight = this.Height;
                }

                isRolledUp = true;
                RollUp();
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
                this.Height = GetExpandedHeight();
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
        /// 获取窗口展开状态下应有的高度
        /// </summary>
        /// <remarks>
        /// 卷起时窗口实际高度只剩标题栏高度，展开必须恢复成卷起前的高度。
        /// </remarks>
        private double GetExpandedHeight()
        {
            if (windowHeight > GetTitleBarHeight())
            {
                return windowHeight;
            }

            // 尚未记录过展开高度时，退回当前高度，避免窗口被压成 0
            return Math.Max(this.Height, GetTitleBarHeight());
        }

        /// <summary>
        /// 临时展开窗口
        /// </summary>
        private void TemporarilyUnroll()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                IconsContainer.Visibility = Visibility.Visible;
                this.Height = GetExpandedHeight();
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
                this.Height = GetExpandedHeight();

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
