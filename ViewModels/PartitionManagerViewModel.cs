using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Layouter.Models;
using Layouter.Services;
using Layouter.Views;

namespace Layouter.ViewModels
{
    public class PartitionManagerViewModel : ObservableObject
    {
        private ObservableCollection<PartitionItemViewModel> partitions = new ObservableCollection<PartitionItemViewModel>();
        private PartitionItemViewModel selectedPartition;
        private bool enableGlobalStyle;
        private bool allSelected;
        private bool anySelectedVisible;

        /// <summary>
        /// 分区列表
        /// </summary>
        public ObservableCollection<PartitionItemViewModel> Partitions
        {
            get => partitions;
            set => SetProperty(ref partitions, value);
        }

        /// <summary>
        /// 选中的分区
        /// </summary>
        public PartitionItemViewModel SelectedPartition
        {
            get => selectedPartition;
            set => SetProperty(ref selectedPartition, value);
        }

        /// <summary>
        /// 是否启用全局样式
        /// </summary>
        public bool EnableGlobalStyle
        {
            get => enableGlobalStyle;
            set
            {
                if (SetProperty(ref enableGlobalStyle, value))
                {
                    // 保存设置
                    GeneralSettingsService.Instance.SetEnableGlobalStyle(value);

                    //通知窗口管理器更新样式
                    WindowManagerService.Instance.UpdateAllWindowStyles();
                }
            }
        }

        /// <summary>
        /// 是否全部选中
        /// </summary>
        public bool AllSelected
        {
            get => allSelected;
            set => SetProperty(ref allSelected, value);
        }

        /// <summary>
        /// 是否有选中的可见分区
        /// </summary>
        public bool AnySelectedVisible
        {
            get => anySelectedVisible;
            set => SetProperty(ref anySelectedVisible, value);
        }

        public PartitionManagerViewModel()
        {
            // 加载全局样式设置
            enableGlobalStyle = GeneralSettingsService.Instance.GetEnableGlobalStyle();

            // 订阅分区可见性变更事件
            GeneralSettingsService.Instance.PartitionVisibilityChanged += OnPartitionVisibilityChanged;
            // 订阅分区集合变更事件
            PropertyChangedEventManager.AddHandler(this, OnPartitionsCollectionChanged, nameof(Partitions));
        }

        private void OnPartitionsCollectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionState();
        }

        /// <summary>
        /// 更新选择状态
        /// </summary>
        private void UpdateSelectionState()
        {
            if (Partitions.Count == 0)
            {
                AllSelected = false;
                AnySelectedVisible = false;
                return;
            }

            AllSelected = Partitions.All(p => p.IsSelected);

            var selectedPartitions = Partitions.Where(p => p.IsSelected).ToList();
            AnySelectedVisible = selectedPartitions.Any(p => p.IsVisible);
        }

        /// <summary>
        /// 加载分区数据
        /// </summary>
        public void LoadPartitions()
        {
            try
            {
                Partitions.Clear();

                // 获取所有分区数据
                var metadata = PartitionDataService.Instance.GetMetadata();
                if (metadata?.WindowIds == null || metadata.WindowIds.Count == 0)
                {
                    UpdateSelectionState();
                    return;
                }

                foreach (var windowId in metadata.WindowIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
                {
                    // 获取分区显示状态
                    bool isVisible = GeneralSettingsService.Instance.GetPartitionVisibility(windowId);
                    // 加载分区设置
                    var settings = PartitionSettingsService.Instance.LoadWindowSettings(windowId);
                    // 加载分区数据（新建分区尚未落盘时数据文件可能缺失，此时仍需在列表中显示）
                    var partitionData = PartitionDataService.Instance.GetPartitionData(windowId);
                    if (partitionData == null)
                    {
                        Log.Information($"分区尚未生成数据文件，使用默认名称显示: {windowId}");
                    }

                    // 创建分区项视图模型
                    var partitionItem = new PartitionItemViewModel
                    {
                        PartitionId = windowId,
                        Title = partitionData == null || string.IsNullOrWhiteSpace(partitionData.Name)
                            ? "未命名分区"
                            : partitionData.Name,
                        IsVisible = isVisible,
                        IsLocked = settings?.IsLocked ?? false,
                        IsSelected = false
                    };

                    // 订阅属性变更事件
                    partitionItem.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(PartitionItemViewModel.IsSelected))
                        {
                            UpdateSelectionState();
                        }
                        else if (e.PropertyName == nameof(PartitionItemViewModel.IsVisible))
                        {
                            GeneralSettingsService.Instance.SetPartitionVisibility(partitionItem.PartitionId, partitionItem.IsVisible);
                            UpdateWindowVisibility(partitionItem.PartitionId, partitionItem.IsVisible);
                            UpdateSelectionState();
                        }
                    };

                    Partitions.Add(partitionItem);
                }

                // 初始化选择状态
                UpdateSelectionState();
            }
            catch (Exception ex)
            {
                Log.Information($"加载分区数据失败: {ex.Message}");
                AppMessageBox.Show($"加载分区数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新窗口显示状态
        /// </summary>
        private void UpdateWindowVisibility(string partitionId, bool isVisible)
        {
            try
            {
                if (isVisible)
                {
                    PartitionDataService.Instance.RestoreWindow(partitionId);
                }
                else
                {
                    var window = WindowManagerService.Instance.GetWindowById(partitionId);
                    if (window != null)
                    {
                        WindowManagerService.Instance.UnregisterWindow(window);
                        window.Close();
                        PartitionDataService.Instance.RemoveWindowMapping(window);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"更新窗口显示状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新选中的分区
        /// </summary>
        public void UpdateSelectedPartition()
        {

        }

        /// <summary>
        /// 编辑分区
        /// </summary>
        public void EditPartition(PartitionItemViewModel partition)
        {
            try
            {
                var viewModel = WindowManagerService.Instance.GetDesktopManagerViewModel(partition.PartitionId);

                if (viewModel != null)
                {
                    var settingsWindow = new PartitionSettingsWindow(viewModel, false);
                    settingsWindow.ShowDialog();

                    // 刷新分区数据
                    LoadPartitions();
                }
                else
                {
                    var settings = PartitionSettingsService.Instance.LoadWindowSettings(partition.PartitionId);
                    var newViewModel = new DesktopManagerViewModel();
                    newViewModel = PartitionSettingsService.Instance.UpdateViewModelSettings(newViewModel, settings);

                    // 打开分区设置窗口
                    var settingsWindow = new PartitionSettingsWindow(newViewModel, false);
                    if (settingsWindow.ShowDialog() == true)
                    {
                        // 保存设置
                        PartitionSettingsService.Instance.SaveSettings(newViewModel, false);
                    }

                    // 刷新分区数据
                    LoadPartitions();
                }
            }
            catch (Exception ex)
            {
                Log.Information($"编辑分区失败: {ex.Message}");
                AppMessageBox.Show($"编辑分区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 切换分区可见性
        /// </summary>
        public void TogglePartitionVisibility(PartitionItemViewModel partition)
        {
            try
            {
                // 切换可见性
                bool newVisibility = !partition.IsVisible;
                partition.IsVisible = newVisibility;

                // 保存设置
                GeneralSettingsService.Instance.SetPartitionVisibility(partition.PartitionId, newVisibility);
            }
            catch (Exception ex)
            {
                Log.Information($"切换分区可见性失败: {ex.Message}");
                AppMessageBox.Show($"切换分区可见性失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除分区
        /// </summary>
        public void DeletePartition(PartitionItemViewModel partition)
        {
            try
            {
                var window = WindowManagerService.Instance.GetWindowById(partition.PartitionId);

                if (window != null)
                {
                    // 关闭分区窗口
                    WindowManagerService.Instance.RemovePartitionWindow(window);
                }
                else
                {
                    PartitionDataService.Instance.RemoveWindow(partition.PartitionId);
                }

                // 从列表中移除
                Partitions.Remove(partition);
            }
            catch (Exception ex)
            {
                Log.Information($"删除分区失败: {ex.Message}");
                AppMessageBox.Show($"删除分区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 切换全选状态
        /// </summary>
        public void ToggleSelectAll()
        {
            bool newState = !AllSelected;

            foreach (var partition in Partitions)
            {
                partition.IsSelected = newState;
            }

            AllSelected = newState;
        }

        /// <summary>
        /// 切换选中分区的可见性
        /// </summary>
        public void ToggleVisibilitySelected()
        {
            try
            {
                var selectedPartitions = Partitions.Where(p => p.IsSelected).ToList();

                if (selectedPartitions.Count == 0)
                {
                    AppMessageBox.Show("请先选择要操作的分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 根据当前状态决定新的可见性
                bool newVisibility = !AnySelectedVisible;

                // 批量设置可见性
                foreach (var partition in selectedPartitions)
                {
                    partition.IsVisible = newVisibility; //触发PropertyChanged事件
                }

            }
            catch (Exception ex)
            {
                Log.Information($"切换选中分区可见性失败: {ex.Message}");
                AppMessageBox.Show($"切换选中分区可见性失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除选中的分区
        /// </summary>
        public void DeleteSelected()
        {
            try
            {
                var selectedPartitions = Partitions.Where(p => p.IsSelected).ToList();

                if (selectedPartitions.Count == 0)
                {
                    AppMessageBox.Show("请先选择要删除的分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 批量删除
                foreach (var partition in selectedPartitions)
                {
                    var window = WindowManagerService.Instance.GetWindowById(partition.PartitionId);
                    if (window != null)
                    {
                        WindowManagerService.Instance.RemovePartitionWindow(window);
                    }
                    else
                    {
                        PartitionDataService.Instance.RemoveWindow(partition.PartitionId);
                    }
                    Partitions.Remove(partition);
                }

                UpdateSelectionState();
                AppToastService.Success("删除完成", $"已删除 {selectedPartitions.Count} 个分区");
            }
            catch (Exception ex)
            {
                Log.Information($"批量删除分区失败: {ex.Message}");
                AppMessageBox.Show($"批量删除分区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// 编辑全局样式
        /// </summary>
        public void EditGlobalStyle()
        {
            ShowGlobalPartitionSettings();
        }

        /// <summary>
        /// 打开「全局分区设置」窗口。
        /// <para>抽成静态方法后，托盘菜单等外部入口也可以直接复用。</para>
        /// </summary>
        public static void ShowGlobalPartitionSettings()
        {
            try
            {
                // 创建一个临时ViewModel用于全局设置
                var globalViewModel = new DesktopManagerViewModel();

                // 加载全局设置
                var globalSettings = PartitionSettingsService.Instance.LoadGlobalSettings();

                // 应用设置到ViewModel
                globalViewModel = PartitionSettingsService.Instance.UpdateViewModelSettings(globalViewModel, globalSettings);

                // 打开设置窗口
                var settingsWindow = new PartitionSettingsWindow(globalViewModel, true);
                if (settingsWindow.ShowDialog() == true)
                {
                    // 保存全局设置
                    PartitionSettingsService.Instance.SaveSettings(globalViewModel, true);

                    // 如果启用了全局样式,则更新所有窗口
                    if (GeneralSettingsService.Instance.GetEnableGlobalStyle())
                    {
                        // 应用全局样式到所有窗口
                        WindowManagerService.Instance.UpdateAllWindowStyles();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"编辑全局样式失败: {ex.Message}");
                AppMessageBox.Show($"编辑全局样式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 分区可见性变更事件处理
        /// </summary>
        private void OnPartitionVisibilityChanged(object sender, PartitionVisibilityChangedEventArgs e)
        {
            // 更新UI中的分区可见性
            var partition = Partitions.FirstOrDefault(p => p.PartitionId == e.PartitionId);
            if (partition != null && partition.IsVisible != e.IsVisible)
            {
                partition.IsVisible = e.IsVisible;
            }
        }
    }
}
