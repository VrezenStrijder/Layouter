using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Data;
using System.Windows.Documents;

namespace Layouter.Plugins
{
    /// <summary>
    /// PluginManagerWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PluginManagerWindow : Window
    {
        private List<PluginInfo> plugins;

        public PluginManagerWindow()
        {
            InitializeComponent();
            LoadPlugins();
        }

        /// <summary>
        /// 加载插件列表
        /// </summary>
        private void LoadPlugins()
        {
            plugins = PluginManager.Instance.PluginInfos.Values.ToList();
            PluginsDataGrid.ItemsSource = plugins;
            UpdateButtonStatus();
        }

        /// <summary>
        /// 更新按钮状态
        /// </summary>
        private void UpdateButtonStatus()
        {
            bool hasSelected = PluginsDataGrid.SelectedItem != null;
            EnableButton.IsEnabled = hasSelected;
            DisableButton.IsEnabled = hasSelected;
            RemoveButton.IsEnabled = hasSelected;
        }

        /// <summary>
        /// 插件选择变更事件
        /// </summary>
        private void PluginsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonStatus();
        }

        /// <summary>
        /// 导入插件按钮点击事件
        /// </summary>
        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "插件文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Title = "选择插件文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                bool result = PluginManager.Instance.ImportPlugin(filePath);

                if (result)
                {
                    AppMessageBox.Show("插件导入成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadPlugins();
                }
                else
                {
                    AppMessageBox.Show("插件导入失败，请检查插件文件是否有效。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 启用插件按钮点击事件
        /// </summary>
        private void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            if (PluginsDataGrid.SelectedItem is PluginInfo selectedPlugin)
            {
                bool result = PluginManager.Instance.EnablePlugin(selectedPlugin.Id);

                if (result)
                {
                    LoadPlugins();
                }
                else
                {
                    AppMessageBox.Show("启用插件失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 禁用插件按钮点击事件
        /// </summary>
        private void DisableButton_Click(object sender, RoutedEventArgs e)
        {
            if (PluginsDataGrid.SelectedItem is PluginInfo selectedPlugin)
            {
                bool result = PluginManager.Instance.DisablePlugin(selectedPlugin.Id);

                if (result)
                {
                    LoadPlugins();
                }
                else
                {
                    AppMessageBox.Show("禁用插件失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 删除插件按钮点击事件
        /// </summary>
        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (PluginsDataGrid.SelectedItem is PluginInfo selectedPlugin)
            {
                MessageBoxResult result = AppMessageBox.Show(
                    $"确定要删除插件 {selectedPlugin.Name} 吗？此操作不可撤销。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    bool removeResult = PluginManager.Instance.RemovePlugin(selectedPlugin.Id);

                    if (removeResult)
                    {
                        LoadPlugins();
                    }
                    else
                    {
                        AppMessageBox.Show("删除插件失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
