using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Layouter.Models;

namespace Layouter.ViewModels
{
    public class DesktopManagerViewModel : ObservableObject
    {

        private ObservableCollection<DesktopIcon> icons = new ObservableCollection<DesktopIcon>();
        public ObservableCollection<DesktopIcon> Icons => icons;

        // 锁定状态变更事件
        public event EventHandler<bool> LockStateChanged;

        // 图标间距
        public const double IconSpacing = 10;

        private double IconAreaWidth = 68;
        private double IconAreaHeight = 70;

        private string name;
        private double opacity = 0.90;
        private string backgroundColor = "Transparent";
        private SolidColorBrush titleForeground = new SolidColorBrush(Colors.White);
        private SolidColorBrush titleBackground = new SolidColorBrush(Colors.Black);
        private FontFamily titleFont = new FontFamily("Microsoft YaHei");
        private double titleFontSize = 14.0;
        private HorizontalAlignment titleAlignment = HorizontalAlignment.Left;
        private IconSize iconSize = IconSize.Medium;
        private Size iconSizeValue = new Size(48, 48);
        private double iconTextSize = 12.0;
        private bool isLocked = false;
        private IconViewMode iconViewMode = IconViewMode.Grid;

        private double partitionWidth = 400;
        private double partitionHeight = 300;


        private SolidColorBrush contentBackground = new SolidColorBrush(Colors.Transparent);

        public DesktopManagerViewModel()
        {
            ArrangeIconsCommand = new RelayCommand(ArrangeIcons);

            ToggleLockCommand = new RelayCommand(SwitchLockState);
        }

        public DesktopManagerViewModel(string id) : this()
        {
            windowId = id;
        }

        public string windowId { get; set; }

        public byte TitleBaseAlpha { get; private set; } = 200;//透明度：200/256

        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }


        public double PartitionWidth
        {
            get => partitionWidth;
            set => SetProperty(ref partitionWidth, value);
        }

        public double PartitionHeight
        {
            get => partitionHeight;
            set
            {
                if (SetProperty(ref partitionHeight, value))
                {
                    UpdateIconsViewHeight();
                }
            }
        }

        private double iconsContentHeight;
        private double iconsViewHeight;

        /// <summary>
        /// 图标内容区高度（网格视图）
        /// </summary>
        /// <remarks>
        /// 网格视图使用 Canvas 排列图标，Canvas 自身不会根据子元素位置计算内容高度，
        /// 滚动容器因此拿不到可滚动的范围。这里给出显式高度，保证图标过多时可以滚动查看。
        /// </remarks>
        public double IconsContentHeight
        {
            get => iconsContentHeight;
            set => SetProperty(ref iconsContentHeight, value);
        }

        /// <summary>
        /// 图标区可见高度（始终按整行对齐）
        /// </summary>
        /// <remarks>
        /// 图标区高度如果不是整行倍数，最后一行会只露出一半，看起来像是排版出错。
        /// 这里把可见高度收敛到「能完整放下多少行就放多少行」，
        /// 多出来的空间留成分区底部的空白（分区背景是透明的，不会看出来），
        /// 超出部分则交给垂直滚动条查看。
        /// </remarks>
        public double IconsViewHeight
        {
            get => iconsViewHeight;
            private set => SetProperty(ref iconsViewHeight, value);
        }

        /// <summary>
        /// 按当前分区高度重新计算图标区可见高度
        /// </summary>
        private void UpdateIconsViewHeight()
        {
            double available = Math.Max(0, partitionHeight);
            double height;

            if (IsListView)
            {
                // 列表视图按固定行高排列
                double rows = Math.Floor(available / ListRowHeight + LayoutTolerance);
                height = rows * ListRowHeight;
            }
            else
            {
                int rows = CalculateRowCount(available);
                height = rows > 0 ? GetGridHeight(rows) : 0;
            }

            // 不能超出容器本身的高度
            IconsViewHeight = Math.Min(height, available);
        }

        #region 搬运进度

        private bool isTransferring;
        private double transferProgress;
        private bool transferProgressIndeterminate;
        private string transferProgressText = string.Empty;

        /// <summary>
        /// 是否正在把桌面项目搬进隐藏存储
        /// </summary>
        public bool IsTransferring
        {
            get => isTransferring;
            private set => SetProperty(ref isTransferring, value);
        }

        /// <summary>
        /// 搬运进度百分比（0-100）
        /// </summary>
        public double TransferProgress
        {
            get => transferProgress;
            private set => SetProperty(ref transferProgress, value);
        }

        /// <summary>
        /// 进度是否不确定
        /// </summary>
        /// <remarks>
        /// 搬一个大目录是一次整体操作，中间拿不到进度，
        /// 此时用不确定进度条表示「正在忙」，避免出现卡在 0% 的错觉。
        /// </remarks>
        public bool TransferProgressIndeterminate
        {
            get => transferProgressIndeterminate;
            private set => SetProperty(ref transferProgressIndeterminate, value);
        }

        /// <summary>
        /// 进度文字（如「正在移动 3/8」）
        /// </summary>
        public string TransferProgressText
        {
            get => transferProgressText;
            private set => SetProperty(ref transferProgressText, value);
        }

        /// <summary>
        /// 开始显示搬运进度
        /// </summary>
        /// <param name="total">本次需要搬运的项目总数</param>
        public void BeginTransfer(int total)
        {
            TransferProgress = 0;
            TransferProgressIndeterminate = total <= 1;
            TransferProgressText = total <= 1 ? "正在移动…" : $"正在移动 0/{total}";
            IsTransferring = true;
        }

        /// <summary>
        /// 更新搬运进度
        /// </summary>
        /// <param name="completed">已完成的项数</param>
        /// <param name="total">总项数</param>
        public void UpdateTransfer(int completed, int total)
        {
            if (total <= 1)
            {
                return;
            }

            TransferProgress = completed * 100.0 / total;
            TransferProgressText = $"正在移动 {completed}/{total}";
        }

        /// <summary>
        /// 结束搬运进度显示
        /// </summary>
        public void EndTransfer()
        {
            IsTransferring = false;
            TransferProgress = 0;
            TransferProgressIndeterminate = false;
            TransferProgressText = string.Empty;
        }

        #endregion

        /// <summary>
        /// 列表视图单行高度（与列表视图模板保持一致）
        /// </summary>
        public const double ListRowHeight = 30;
        private double measuredItemHeight;

        /// <summary>
        /// 实测得到的图标项高度
        /// </summary>
        /// <remarks>
        /// 图标名称过长时会换行，实际渲染高度比按图标尺寸估算的高度更大。
        /// 视图层测量真实高度后回填到这里，排布与内容高度才能与实际显示一致，
        /// 否则自动高度会算小了、进而出现本不该有的滚动条。
        /// 为 0 表示尚未测量，按估算高度处理。
        /// </remarks>
        public double MeasuredItemHeight
        {
            get => measuredItemHeight;
            set
            {
                if (Math.Abs(measuredItemHeight - value) < 0.5)
                {
                    return;
                }

                measuredItemHeight = value;
                OnPropertyChanged(nameof(EffectiveItemHeight));

                // 行高变了，可见高度能容纳的整行数也会跟着变
                UpdateIconsViewHeight();
            }
        }

        /// <summary>
        /// 当前视图下单个图标项的实际占用高度
        /// </summary>
        public double EffectiveItemHeight
        {
            get
            {
                if (IsListView)
                {
                    return ListRowHeight;
                }

                return MeasuredItemHeight > 0 ? MeasuredItemHeight : IconAreaHeight;
            }
        }

        public SolidColorBrush TitleForeground
        {
            get => titleForeground;
            set => SetProperty(ref titleForeground, value);
        }

        // 标题栏背景色
        public SolidColorBrush TitleBackground
        {
            get => titleBackground;
            set
            {
                Color c = value.Color;
                if (value != null && c.A == 255)
                {
                    byte baseAlpha = TitleBaseAlpha;
                    c.A = baseAlpha;
                }

                SetProperty(ref titleBackground, new SolidColorBrush(c));
            }
        }

        // 标题栏字体
        public FontFamily TitleFont
        {
            get => titleFont;
            set => SetProperty(ref titleFont, value);
        }

        public double TitleFontSize
        {
            get => titleFontSize;
            set => SetProperty(ref titleFontSize, value);
        }

        // 标题栏文本显示位置
        public HorizontalAlignment TitleAlignment
        {
            get => titleAlignment;
            set => SetProperty(ref titleAlignment, value);
        }


        // 分区窗口透明度
        public double Opacity
        {
            get => opacity;
            set => SetProperty(ref opacity, value);
        }

        // 图标大小
        public IconSize IconSize
        {
            get => iconSize;
            set
            {
                if (SetProperty(ref iconSize, value))
                {
                    // 更新图标尺寸
                    UpdateIconSize();
                }
            }
        }

        /// <summary>
        /// 图标文本大小
        /// </summary>
        public double IconTextSize
        {
            get => iconTextSize;
            set
            {
                if (SetProperty(ref iconTextSize, value))
                {
                    UpdateIconSize();
                    foreach (var icon in Icons)
                    {
                        icon.TextSize = value;
                    }
                }
            }
        }


        // 分区背景颜色
        public string BackgroundColor
        {
            get
            {
                return backgroundColor;
            }
            set
            {
                backgroundColor = value;
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }

        public bool IsLocked
        {
            get => isLocked;
            set
            {
                if (SetProperty(ref isLocked, value))
                {
                    // 更新窗口可拖动和可调整大小状态
                    UpdateWindowDraggableState();
                    // 触发锁定状态变更事件
                    LockStateChanged?.Invoke(this, value);
                }
            }
        }

        public SolidColorBrush ContentBackground
        {
            get => contentBackground;
            set => SetProperty(ref contentBackground, value);
        }

        public bool IsRolledUp { get; set; } = false;

        /// <summary>
        /// 分区内图标的显示方式（网格 / 列表）
        /// </summary>
        public IconViewMode IconViewMode
        {
            get => iconViewMode;
            set
            {
                if (SetProperty(ref iconViewMode, value))
                {
                    OnPropertyChanged(nameof(IsGridView));
                    OnPropertyChanged(nameof(IsListView));

                    // 网格与列表的行高不同，可见高度需要按新的行高重新计算
                    UpdateIconsViewHeight();
                }
            }
        }

        /// <summary>是否以网格方式显示图标</summary>
        public bool IsGridView => IconViewMode == IconViewMode.Grid;

        /// <summary>是否以列表方式显示图标</summary>
        public bool IsListView => IconViewMode == IconViewMode.List;

        public IRelayCommand ArrangeIconsCommand { get; }

        public ICommand ToggleLockCommand { get; }


        public void ArrangeIcons()
        {
            // 列表视图由 StackPanel 逐行排列，位置交给布局容器处理
            if (IsListView)
            {
                UpdateContentHeight();
                return;
            }

            // 网格视图按「从左到右、从上到下」的顺序依次排布：
            // 先算出当前宽度下能完整放下的列数，再按列数折行。
            int columns = CalculateColumnCount(PartitionWidth);

            // 图标名称换行后实际高度会变大，这里按实测高度排布，避免行与行重叠
            double itemHeight = EffectiveItemHeight;

            int index = 0;
            int rows = 0;

            foreach (var icon in Icons)
            {
                if (icon == null)
                {
                    continue;
                }

                int row = index / columns;
                int column = index % columns;

                // 设置图标位置
                icon.Position = new Point(
                    IconSpacing + column * (IconAreaWidth + IconSpacing),
                    IconSpacing + row * (itemHeight + IconSpacing));

                rows = row + 1;
                index++;
            }

            IconColumnCount = columns;
            IconRowCount = rows;

            // 网格视图由 Canvas 承载图标，而 Canvas 不会自行累加子元素位置，
            // 必须显式给出内容高度，垂直滚动条才能滚动到最后一个图标。
            UpdateContentHeight();
        }

        /// <summary>
        /// 布局计算的容差（像素）
        /// </summary>
        /// <remarks>
        /// 窗口尺寸、边距、行高都是浮点数，「刚好放得下」时除法结果可能是 2.9999999996，
        /// 直接向下取整会凭空少算一行/一列，表现为图标区突然塌缩一行、
        /// 最后一行图标消失、底部还空出一大块。这里给一点容差消除这种误差。
        /// </remarks>
        private const double LayoutTolerance = 0.05;

        /// <summary>
        /// 按可用宽度计算能完整放下的列数
        /// </summary>
        /// <remarks>
        /// 每列占用「图标宽度 + 间距」，最后一列右侧还要留出间距，
        /// 因此这里减去一个间距再取整除，保证每一列都完整可见、右侧不贴边。
        /// </remarks>
        public int CalculateColumnCount(double availableWidth)
        {
            double pitch = IconAreaWidth + IconSpacing;

            if (availableWidth <= 0 || pitch <= 0)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Floor((availableWidth - IconSpacing) / pitch + LayoutTolerance));
        }

        /// <summary>
        /// 按可用高度计算能完整放下的行数
        /// </summary>
        /// <remarks>
        /// 与列数同理：每行占用「图标高度 + 间距」，末行下方也要留出间距。
        /// </remarks>
        public int CalculateRowCount(double availableHeight)
        {
            double pitch = EffectiveItemHeight + IconSpacing;

            if (availableHeight <= 0 || pitch <= 0)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Floor((availableHeight - IconSpacing) / pitch + LayoutTolerance));
        }

        /// <summary>
        /// 网格视图的行距（图标高度 + 间距）
        /// </summary>
        public double GridRowPitch => EffectiveItemHeight + IconSpacing;

        /// <summary>
        /// 网格视图的列距（图标宽度 + 间距）
        /// </summary>
        public double GridColumnPitch => IconAreaWidth + IconSpacing;

        /// <summary>
        /// 网格视图排布指定列数所需的宽度
        /// </summary>
        /// <remarks>
        /// 布局从 IconSpacing 开始，列与列之间再各留一个间距。
        /// </remarks>
        public double GetGridWidth(int columns)
        {
            if (columns <= 0)
            {
                return 0;
            }

            return IconSpacing + columns * GridColumnPitch;
        }

        /// <summary>
        /// 网格视图排布指定行数所需的高度
        /// </summary>
        public double GetGridHeight(int rows)
        {
            if (rows <= 0)
            {
                return 0;
            }

            return IconSpacing + rows * GridRowPitch;
        }

        /// <summary>
        /// 当前网格布局的列数
        /// </summary>
        public int IconColumnCount
        {
            get => iconColumnCount;
            private set => SetProperty(ref iconColumnCount, value);
        }

        /// <summary>
        /// 当前网格布局的行数
        /// </summary>
        public int IconRowCount
        {
            get => iconRowCount;
            private set => SetProperty(ref iconRowCount, value);
        }

        private int iconColumnCount = 1;
        private int iconRowCount = 1;

        /// <summary>
        /// 依据当前图标位置计算图标内容区高度
        /// </summary>
        public void UpdateContentHeight()
        {
            // 列表视图由 StackPanel 逐行排列，行高固定
            if (IsListView)
            {
                IconsContentHeight = Icons.Count * ListRowHeight;
                return;
            }

            double bottom = 0;
            double itemHeight = EffectiveItemHeight;

            foreach (var icon in Icons)
            {
                if (icon == null)
                {
                    continue;
                }

                double iconBottom = icon.Position.Y + itemHeight;
                if (iconBottom > bottom)
                {
                    bottom = iconBottom;
                }
            }

            IconsContentHeight = Icons.Count == 0 ? 0 : bottom + IconSpacing;
        }

        public void SwitchLockState()
        {
            IsLocked = !IsLocked;
        }

        private void UpdateWindowDraggableState()
        {
            //Todo: 更新窗口可拖动和可调整大小状态
        }

        private void UpdateIconSize()
        {
            switch (IconSize)
            {
                case IconSize.Small:
                    iconSizeValue = new Size(32, 32);
                    IconAreaWidth = 60;
                    IconAreaHeight = Math.Ceiling(32 + 7 + IconTextSize * 1.35 + 4);
                    break;
                case IconSize.Large:
                    iconSizeValue = new Size(64, 64);
                    IconAreaWidth = 84;
                    IconAreaHeight = Math.Ceiling(64 + 7 + IconTextSize * 1.35 + 4);
                    break;
                case IconSize.Medium:
                default:
                    iconSizeValue = new Size(48, 48);
                    IconAreaWidth = 68;
                    IconAreaHeight = Math.Ceiling(48 + 7 + IconTextSize * 1.35 + 4);
                    break;
            }

            // 更新所有图标的尺寸
            foreach (var icon in Icons)
            {
                icon.Size = iconSizeValue;
            }

            // 重新排列图标
            ArrangeIcons();
        }


        /// <summary>
        /// 获取分区窗口大小
        /// </summary>
        /// <returns>窗口大小</returns>
        public Size GetPartitionSize()
        {
            return new Size(partitionWidth, partitionHeight);
        }

        public void AddIcon(DesktopIcon icon)
        {
            if (!Icons.Any(i => i.Id == icon.Id))
            {
                Icons.Add(icon);
            }
        }

        public void RemoveIcon(DesktopIcon icon)
        {
            if (Icons.Contains(icon))
            {
                Icons.Remove(icon);

                // 图标被拖走或删除后，剩余图标需要重新排布填补空位，
                // 否则原来的格子会留下一个空洞。
                ArrangeIcons();
            }
        }

        public DesktopIcon GetIconById(string id)
        {
            foreach (var icon in Icons)
            {
                if (icon.Id == id)
                {
                    return icon;
                }
            }
            return null;
        }

        public Size GetIconSize()
        {
            var size = new Size(48, 48);
            switch (IconSize)
            {
                case IconSize.Small:
                    size = new Size(32, 32);
                    break;
                case IconSize.Large:
                    size = new Size(64, 64);
                    break;
                case IconSize.Medium:
                default:
                    size = new Size(48, 48);
                    break;
            }
            return size;
        }

        public Size GetIconCellSize()
        {
            if (IconAreaWidth <= 0 || IconAreaHeight <= 0)
            {
                UpdateIconSize();
            }

            return new Size(IconAreaWidth, EffectiveItemHeight);
        }
    }

    public enum IconSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// 分区内图标的显示方式
    /// </summary>
    public enum IconViewMode
    {
        /// <summary>网格（图标在上、名称在下）</summary>
        Grid = 0,

        /// <summary>列表（图标在左、名称在右，逐行排列）</summary>
        List = 1
    }

}
