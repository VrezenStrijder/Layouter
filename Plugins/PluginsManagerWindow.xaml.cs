using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
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
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using Layouter.Plugins.Controls;
using Layouter.Services;
using Layouter.Views;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Win32;
using PluginEntry;
using Path = System.IO.Path;

namespace Layouter.Plugins
{
    /// <summary>
    /// PluginsManagerWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PluginsManagerWindow : Window
    {
        private PluginManager pluginManager;
        private WindowTemplateManager templateManager;
        private string pluginsDirectory;
        private ObservableCollection<PluginViewModel> pluginViewModels = new ObservableCollection<PluginViewModel>();
        private int tabCounter = 1;

        public ICommand CreatePluginCommand { get; }


        public PluginsManagerWindow() : this(Ioc.Default.GetRequiredService<PluginManager>())
        {
        }

        public PluginsManagerWindow(PluginManager pluginManager)
        {
            InitializeComponent();

            // 使用AppData作为插件目录
            pluginsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Env.AppName, "Plugins");

            Directory.CreateDirectory(pluginsDirectory);

            this.pluginManager = pluginManager;
            templateManager = new WindowTemplateManager(pluginManager);

            this.pluginManager.PluginLoaded += PluginManager_PluginLoaded;
            this.pluginManager.PluginStatusChanged += PluginManager_PluginStatusChanged;
            this.pluginManager.PluginCodeLoaded += PluginManager_PluginCodeLoaded;
            CreatePluginCommand = new RelayCommand(CreatePlugin);

            PluginsGrid.ItemsSource = pluginViewModels;

            this.DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshPlugins();
        }

        private void RefreshPlugins()
        {
            pluginViewModels.Clear();
            pluginManager.LoadPluginsMetadata();

            foreach (var plugin in pluginManager.Plugins.Values)
            {
                pluginViewModels.Add(new PluginViewModel
                {
                    Id = plugin.Descriptor.Id,
                    Name = plugin.Descriptor.Name,
                    Version = plugin.Descriptor.Version,
                    Description = plugin.Descriptor.Description,
                    Author = plugin.Descriptor.Author,
                    IsEnabled = plugin.Descriptor.IsEnabled,
                    Plugin = plugin
                });
            }

            if (pluginManager.Plugins.Count == 0)
            {
                StatusBar.Text = "没有找到插件。点击\"导入插件\"按钮添加新插件。";
            }
            else
            {
                StatusBar.Text = $"已加载 {pluginManager.Plugins.Count} 个插件";
            }
        }

        private void PluginManager_PluginLoaded(object sender, PluginLoadedEventArgs e)
        {
            // 更新插件列表
            Dispatcher.Invoke(() =>
            {
                var existingPlugin = pluginViewModels.FirstOrDefault(p => p.Id == e.Plugin.Descriptor.Id);
                if (existingPlugin == null)
                {
                    pluginViewModels.Add(new PluginViewModel
                    {
                        Id = e.Plugin.Descriptor.Id,
                        Name = e.Plugin.Descriptor.Name,
                        Version = e.Plugin.Descriptor.Version,
                        Description = e.Plugin.Descriptor.Description,
                        Author = e.Plugin.Descriptor.Author,
                        IsEnabled = e.Plugin.Descriptor.IsEnabled,
                        Plugin = e.Plugin
                    });
                }
            });
        }

        private void PluginManager_PluginCodeLoaded(object sender, PluginCodeLoadedEventArgs e)
        {
            // 插件代码加载完成后的处理
            Dispatcher.Invoke(() =>
            {
                var pluginViewModel = pluginViewModels.FirstOrDefault(p => p.Id == e.Plugin.Descriptor.Id);
                if (pluginViewModel != null)
                {
                    pluginViewModel.Plugin = e.Plugin;
                    StatusBar.Text = $"插件 \"{e.Plugin.Descriptor.Name}\" 加载完成";
                }
            });
        }

        private void ShowPluginWindow(LoadedPlugin plugin)
        {
            //var pluginUI = templateManager.CreatePluginUI(plugin);
            //PluginsContainer.Children.Add(pluginUI);

            //首先检测是否已经存在对应插件的窗口的配置文件
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Env.AppName);
            var pluginConfigFile = Path.Combine(configPath, $"partition_{plugin.Descriptor.Id}.json");

            if ((plugin.Descriptor.Style == (int)TemplateWindowStyle.CardView) && (PartitionDataService.Instance.GetWindow(plugin.Descriptor.Id) != null))
            {
                //已经存在窗口,返回
                return;
            }

            if (File.Exists(pluginConfigFile))
            {
                // 对于已有配置文件的插件,直接显示窗口,不需要等待代码加载
                PartitionDataService.Instance.ShowWindow(plugin.Descriptor.Id);
            }
            //首次加载
            else
            {
                // 异步加载插件代码,不阻塞UI线程
                if (!plugin.IsCodeLoaded)
                {
                    Task.Run(async () =>
                    {
                        await pluginManager.LoadPluginCode(plugin.Descriptor.Id);
                    });
                }

                var pluginWindow = templateManager.CreatePluginWindow(plugin);
                pluginWindow.Show();
            }
        }

        private void PluginManager_PluginStatusChanged(object sender, PluginStatusChangedEventArgs e)
        {
            // 更新UI状态
            Dispatcher.Invoke(() =>
            {
                var pluginViewModel = pluginViewModels.FirstOrDefault(p => p.Id == e.Plugin.Descriptor.Id);
                if (pluginViewModel != null)
                {
                    pluginViewModel.IsEnabled = e.IsEnabled;
                }
                StatusBar.Text = $"插件 \"{e.Plugin.Descriptor.Name}\" 已{(e.IsEnabled ? "启用" : "禁用")}";
            });
        }

        private void ImportPlugin_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "插件文件 (*.plug)|*.plug",
                Title = "选择插件文件"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string targetFile = Path.Combine(
                        pluginsDirectory,
                        Path.GetFileName(dialog.FileName));

                    // 复制文件到插件目录
                    File.Copy(dialog.FileName, targetFile, true);

                    // 重新加载插件
                    RefreshPlugins();

                    StatusBar.Text = "插件导入成功";
                }
                catch (Exception ex)
                {
                    AppMessageBox.Show($"导入插件出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusBar.Text = "插件导入失败";
                }
            }
        }

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlugins();
            StatusBar.Text = "插件已刷新";
        }

        private void PluginEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string pluginId)
            {
                try
                {
                    bool isEnabled = checkBox.IsChecked ?? false;
                    pluginManager.SetPluginEnabled(pluginId, isEnabled);

                    // 获取插件对象
                    if (pluginManager.Plugins.TryGetValue(pluginId, out var plugin))
                    {
                        if (isEnabled)
                        {
                            // 启用插件 - 显示插件窗口
                            ShowPluginWindow(plugin);
                            pluginManager.UpdatePluginWindowState(pluginId, true);
                        }
                        else
                        {
                            // 禁用插件 - 关闭插件窗口
                            ClosePluginWindow(pluginId);
                            pluginManager.UpdatePluginWindowState(pluginId, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppMessageBox.Show($"启用/禁用插件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClosePluginWindow(string pluginId)
        {
            // 查找并关闭与插件ID对应的所有窗口
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
            {
                var isMatchedByTag = window.Tag is string windowPluginId && windowPluginId == pluginId;
                var isMatchedByPartitionId = window is DesktopManagerWindow desktopWindow && desktopWindow.WindowId == pluginId;

                if (isMatchedByTag || isMatchedByPartitionId)
                {
                    if (window is DesktopManagerWindow dmw)
                    {
                        PartitionDataService.Instance.HideWindow(dmw);
                    }
                    else
                    {
                        window.Close();
                    }
                }
            }
        }

        private void PluginSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string pluginId)
            {
                var plugin = pluginManager.Plugins[pluginId];
                if (plugin != null)
                {
                    // 显示参数设置窗口
                    var settingsWindow = new Window
                    {
                        Title = $"{plugin.Descriptor.Name} - 参数设置",
                        Width = 500,
                        Height = 400,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this
                    };

                    var panel = new StackPanel { Margin = new Thickness(10) };

                    // 添加每个功能的参数设置
                    foreach (var paramGroup in plugin.ParameterDescriptions)
                    {
                        panel.Children.Add(new TextBlock
                        {
                            Text = $"功能：{paramGroup.Key}",
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 10, 0, 5)
                        });

                        foreach (var param in paramGroup.Value)
                        {
                            var paramPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                            paramPanel.Children.Add(new TextBlock
                            {
                                Text = $"{param.DisplayName}：",
                                Width = 100,
                                VerticalAlignment = VerticalAlignment.Center
                            });

                            // 根据参数类型创建不同的控件
                            UIElement control;
                            if (param.ParameterType == typeof(bool))
                            {
                                control = new CheckBox
                                {
                                    IsChecked = param.ParameterValue != null && (bool)param.ParameterValue,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                            }
                            else if (param.ParameterType == typeof(int) || param.ParameterType == typeof(double))
                            {
                                control = new TextBox
                                {
                                    Text = param.ParameterValue?.ToString() ?? "0",
                                    Width = 150,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                            }
                            else
                            {
                                control = new TextBox
                                {
                                    Text = param.ParameterValue?.ToString() ?? "",
                                    Width = 250,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                            }

                            paramPanel.Children.Add(control);
                            panel.Children.Add(paramPanel);

                            if (!string.IsNullOrEmpty(param.Description))
                            {
                                panel.Children.Add(new TextBlock
                                {
                                    Text = param.Description,
                                    Foreground = Brushes.Gray,
                                    FontStyle = FontStyles.Italic,
                                    Margin = new Thickness(100, 2, 0, 5),
                                    TextWrapping = TextWrapping.Wrap
                                });
                            }
                        }
                    }

                    var buttonsPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 20, 0, 0)
                    };

                    var saveButton = new Button
                    {
                        Content = "保存",
                        Width = 80,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    saveButton.Click += (s, args) => settingsWindow.Close();

                    var cancelButton = new Button
                    {
                        Content = "取消",
                        Width = 80
                    };
                    cancelButton.Click += (s, args) => settingsWindow.Close();

                    buttonsPanel.Children.Add(saveButton);
                    buttonsPanel.Children.Add(cancelButton);
                    panel.Children.Add(buttonsPanel);

                    var scrollViewer = new ScrollViewer
                    {
                        Content = panel,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };

                    settingsWindow.Content = scrollViewer;
                    settingsWindow.ShowDialog();
                }
            }
        }

        private void EditPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string pluginId)
            {
                return;
            }

            try
            {
                if (!pluginManager.Plugins.TryGetValue(pluginId, out var plugin) || plugin == null)
                {
                    AppMessageBox.Show("未找到插件信息。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var pluginDirectory = plugin.ManifestDirectory;
                if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
                {
                    AppMessageBox.Show("未找到插件解压目录，请刷新插件列表后重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var codeEditor = new CodeEditor();
                codeEditor.LoadPluginFromDirectory(pluginDirectory);

                var tabItem = new TabItem
                {
                    Header = $"编辑 {plugin.Descriptor.Name}",
                    Content = codeEditor,
                    IsSelected = true
                };

                MainTabControl.Items.Add(tabItem);
                StatusBar.Text = $"已打开插件 \"{plugin.Descriptor.Name}\" 的开发示例";
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"打开插件编辑器失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string pluginId)
            {
                var plugin = pluginManager.Plugins[pluginId];
                if (plugin != null)
                {
                    var result = AppMessageBox.Show(
                        $"确定要删除插件 \"{plugin.Descriptor.Name}\" 吗？此操作不可撤销。",
                        "确认删除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            if (pluginManager.RemovePlugin(pluginId))
                            {
                                RefreshPlugins();
                                StatusBar.Text = $"插件 \"{plugin.Descriptor.Name}\" 已删除";
                            }
                        }
                        catch (Exception ex)
                        {
                            AppMessageBox.Show($"删除插件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void CreatePlugin()
        {
            // 创建新标签页,包含代码编辑器
            var tabItem = new TabItem
            {
                Header = $"新插件 {tabCounter}",
                IsSelected = true
            };

            var codeEditor = new CodeEditor();

            tabItem.Content = codeEditor;
            MainTabControl.Items.Add(tabItem);
            tabCounter++;
        }

        //private void CreatePlugin_Click(object sender, RoutedEventArgs e)
        //{
        //    // 创建新标签页,包含代码编辑器
        //    var tabItem = new TabItem
        //    {
        //        Header = $"新插件 {tabCounter}",
        //        IsSelected = true
        //    };

        //    var codeEditor = new CodeEditor();

        //    tabItem.Content = codeEditor;
        //    MainTabControl.Items.Add(tabItem);
        //    tabCounter++;
        //}

        //private void SavePlugin(CodeEditor codeEditor, TabItem tabItem)
        //{
        //    try
        //    {
        //        // 验证代码
        //        string validationResult = codeEditor.ValidateCode(codeEditor.Code);
        //        if (!string.IsNullOrEmpty(validationResult))
        //        {
        //            AppMessageBox.Show($"代码验证失败：\n{validationResult}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //            return;
        //        }

        //        // 从插件描述中获取插件名称和类名
        //        string pluginName = "自定义插件";
        //        string className = "MyCustomPlugin";

        //        try
        //        {
        //            // 解析插件描述JSON
        //            var pluginDescriptor = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(codeEditor.PluginDescription);
        //            if (pluginDescriptor != null)
        //            {
        //                pluginName = pluginDescriptor["Name"]?.ToString() ?? pluginName;
        //                className = pluginDescriptor["PluginClassName"]?.ToString() ?? className;
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            // 如果解析失败,则使用默认值
        //            AppMessageBox.Show($"解析插件描述失败：{ex.Message}\n将使用默认值。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);

        //            // 尝试从代码中提取类名
        //            var syntaxTree = CSharpSyntaxTree.ParseText(codeEditor.Code);
        //            var root = syntaxTree.GetRoot();
        //            var classDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();

        //            foreach (var classDeclaration in classDeclarations)
        //            {
        //                className = classDeclaration.Identifier.Text;
        //                break; // 只处理第一个类
        //            }
        //        }

        //        // 创建保存对话框
        //        var saveDialog = new SaveFileDialog
        //        {
        //            Filter = "插件文件 (*.zip)|*.zip",
        //            Title = "保存插件",
        //            FileName = $"{pluginName}.zip",
        //            InitialDirectory = pluginsDirectory
        //        };

        //        if (saveDialog.ShowDialog() == true)
        //        {
        //            // 创建临时目录
        //            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        //            Directory.CreateDirectory(tempDir);

        //            try
        //            {
        //                // 保存代码文件
        //                string codeFilePath = Path.Combine(tempDir, className);
        //                File.WriteAllText(codeFilePath, codeEditor.Code);

        //                // 使用编辑器中的插件描述信息
        //                string descriptorJson = codeEditor.PluginDescription;
        //                string descriptorFilePath = Path.Combine(tempDir, "plugin.json");
        //                File.WriteAllText(descriptorFilePath, descriptorJson);

        //                // 创建空的图标描述文件
        //                string iconJson = "{}";
        //                string iconFilePath = Path.Combine(tempDir, "icon.json");
        //                File.WriteAllText(iconFilePath, iconJson);

        //                // 创建ZIP文件
        //                if (File.Exists(saveDialog.FileName))
        //                {
        //                    File.Delete(saveDialog.FileName);
        //                }
        //                ZipFile.CreateFromDirectory(tempDir, saveDialog.FileName);

        //                // 刷新插件列表
        //                //RefreshPlugins();
        //                StatusBar.Text = $"插件 \"{pluginName}\" 已保存";

        //                // 关闭标签页
        //                //MainTabControl.Items.Remove(tabItem);
        //            }

        //            finally
        //            {
        //                // 清理临时目录
        //                if (Directory.Exists(tempDir))
        //                {
        //                    Directory.Delete(tempDir, true);
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AppMessageBox.Show($"保存插件时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}
    }

    public class PluginViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public bool IsEnabled { get; set; }
        public LoadedPlugin Plugin { get; set; }
    }
}
