using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Layouter.Plugins
{
    /// <summary>
    /// PluginCompilerWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PluginCompilerWindow : Window
    {
        public PluginCompilerWindow()
        {
            InitializeComponent();

            // 设置默认输出目录
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Layouter", "Plugins");
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            OutputFileTextBox.Text = Path.Combine(appDataPath, "MyPlugin.dll");
        }

        private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "C# 源文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                Title = "选择插件源代码文件"
            };

            if (ofd.ShowDialog() == true)
            {
                SourceFileTextBox.Text = ofd.FileName;

                try
                {
                    // 加载源代码
                    SourceCodeTextBox.Text = File.ReadAllText(ofd.FileName);

                    // 设置默认输出文件名
                    string fileName = Path.GetFileNameWithoutExtension(ofd.FileName);
                    string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Layouter", "Plugins");
                    OutputFileTextBox.Text = Path.Combine(appDataPath, $"{fileName}.dll");
                }
                catch (Exception ex)
                {
                    AppMessageBox.Show($"读取源文件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "DLL 文件 (*.dll)|*.dll|所有文件 (*.*)|*.*",
                Title = "选择插件输出文件",
                FileName = Path.GetFileName(OutputFileTextBox.Text)
            };

            if (sfd.ShowDialog() == true)
            {
                OutputFileTextBox.Text = sfd.FileName;
            }
        }

        private void CompileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查源代码
                if (string.IsNullOrWhiteSpace(SourceCodeTextBox.Text))
                {
                    AppMessageBox.Show("请输入或加载源代码", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 检查输出文件
                if (string.IsNullOrWhiteSpace(OutputFileTextBox.Text))
                {
                    AppMessageBox.Show("请指定输出文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 保存源代码到临时文件
                string tempFile = Path.GetTempFileName() + ".cs";
                File.WriteAllText(tempFile, SourceCodeTextBox.Text);

                // 编译插件
                bool result = PluginCompiler.CompilePlugin(tempFile, OutputFileTextBox.Text);

                // 删除临时文件
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                if (result)
                {
                    AppMessageBox.Show("插件编译成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 询问是否加载插件
                    MessageBoxResult loadResult = AppMessageBox.Show("是否立即加载编译好的插件？", "加载插件", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (loadResult == MessageBoxResult.Yes)
                    {
                        bool loadSuccess = Plugins.PluginManager.Instance.LoadPlugin(OutputFileTextBox.Text);
                        if (loadSuccess)
                        {
                            AppMessageBox.Show("插件加载成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            AppMessageBox.Show("插件加载失败，请检查插件是否实现了IPlugin接口。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    AppMessageBox.Show("插件编译失败，请检查源代码。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"编译插件时出错: {ex.Message}");
                AppMessageBox.Show($"编译插件时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
