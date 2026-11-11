using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Layouter.Services;
using Layouter.ViewModels;

namespace Layouter.Views
{
    /// <summary>
    /// PartitionManagerWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PartitionManagerWindow : Window
    {
        private PartitionManagerViewModel vm;

        public PartitionManagerWindow()
        {
            InitializeComponent();

            vm = new PartitionManagerViewModel();
            DataContext = vm;

            // 加载分区数据
            vm.LoadPartitions();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            vm.UpdateSelectedPartition();
        }

        /// <summary>
        /// 双击分区行：打开该分区的编辑对话框
        /// </summary>
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 双击操作列上的按钮时不再重复打开编辑对话框
                if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
                {
                    return;
                }

                var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
                if (row?.Item is PartitionItemViewModel partition)
                {
                    e.Handled = true;
                    vm.EditPartition(partition);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"双击打开分区编辑对话框时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 从指定元素向上查找指定类型的祖先
        /// </summary>
        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T match)
                {
                    return match;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private void EditPartition_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var partition = (PartitionItemViewModel)button.DataContext;

            vm.EditPartition(partition);
        }

        private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var partition = (PartitionItemViewModel)button.DataContext;

            vm.TogglePartitionVisibility(partition);
        }

        private void DeletePartition_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var partition = (PartitionItemViewModel)button.DataContext;

            if (AppMessageBox.Show($"确定要删除分区 \"{partition.Title}\" 吗？此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                vm.DeletePartition(partition);
            }
        }

        private void ToggleSelectAll_Click(object sender, RoutedEventArgs e)
        {
            vm.ToggleSelectAll();
        }

        private void ToggleVisibilitySelected_Click(object sender, RoutedEventArgs e)
        {
            vm.ToggleVisibilitySelected();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            int count = vm.Partitions.Count(p => p.IsSelected);

            if (count == 0)
            {
                AppMessageBox.Show("请先选择要删除的分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (AppMessageBox.Show($"确定要删除选中的 {count} 个分区吗？此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                vm.DeleteSelected();
            }
        }

        private void EditGlobalStyle_Click(object sender, RoutedEventArgs e)
        {
            vm.EditGlobalStyle();
        }

        /// <summary>
        /// 打开备份与还原窗口
        /// </summary>
        private void BackupManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SingletonWindowManager.Instance.ShowWindow<BackupManagerWindow>(() => new BackupManagerWindow(), false);
            }
            catch (Exception ex)
            {
                Log.Information($"打开备份与还原窗口时出错: {ex.Message}");
                AppMessageBox.Show($"打开备份与还原窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 新建分区
        /// </summary>
        private void NewPartition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new DesktopManagerWindow();
                PartitionDataService.Instance.ShowWindow(window);

                // 让新分区直接进入标题编辑状态，便于立即命名
                window.Dispatcher.BeginInvoke(new Action(() => window.EnableTitleEditOnFirstLoad()));

                // 立即落盘并刷新列表，保证新分区马上出现在列表中
                PartitionDataService.Instance.SaveAllPartitions();
                vm.LoadPartitions();
            }
            catch (Exception ex)
            {
                Log.Information($"创建新分区窗口时出错: {ex.Message}");
                AppMessageBox.Show($"创建新分区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 刷新分区列表
        /// </summary>
        private void RefreshPartitions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PartitionDataService.Instance.SaveAllPartitions();
                vm.LoadPartitions();
            }
            catch (Exception ex)
            {
                Log.Information($"刷新分区列表时出错: {ex.Message}");
                AppMessageBox.Show($"刷新分区列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
