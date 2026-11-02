using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Layouter.Models;
using Layouter.Services;
using Layouter.ViewModels;
using Layouter.Views;

using Path = System.IO.Path;
using PluginEntry;
using System.Windows.Input;
using Layouter.Plugins.Views;

namespace Layouter.Plugins
{
    public class WindowTemplateManager
    {
        private readonly Dictionary<TemplateWindowStyle, Func<LoadedPlugin, PluginManager, Window>> templateFactories =
            new Dictionary<TemplateWindowStyle, Func<LoadedPlugin, PluginManager, Window>>();

        private readonly PluginManager pluginManager;

        public WindowTemplateManager(PluginManager pluginManager)
        {
            this.pluginManager = pluginManager;
            RegisterDefaultTemplates();
        }

        /// <summary>
        /// 检查插件代码是否已加载,如果未加载则异步加载
        /// </summary>
        private async Task EnsurePluginCodeLoadedAsync(LoadedPlugin plugin)
        {
            if (!plugin.IsCodeLoaded)
            {
                await pluginManager.LoadPluginCode(plugin.Descriptor.Id);
            }
        }

        private void RegisterDefaultTemplates()
        {
            // 默认模板(图标按钮)
            templateFactories[TemplateWindowStyle.CardView] = CreateCardViewTemplate;

            //明细信息窗口
            templateFactories[TemplateWindowStyle.DetailedView] = CreateDetailedViewTemplate;

            //悬浮窗口
            templateFactories[TemplateWindowStyle.FloatingWindow] = CreateFloatingWindowTemplate;
        }

        public void RegisterTemplate(TemplateWindowStyle winStyle, Func<LoadedPlugin, PluginManager, Window> templateFactory)
        {
            templateFactories[winStyle] = templateFactory;
        }

        public Window CreatePluginWindow(LoadedPlugin plugin)
        {
            if (templateFactories.TryGetValue((TemplateWindowStyle)plugin.Descriptor.Style, out var factory))
            {
                var window = factory(plugin, pluginManager);
                // 设置窗口的Tag为插件ID,以便在关闭窗口时能够识别
                window.Tag = plugin.Descriptor.Id;
                return window;
            }

            // 默认模板
            var defaultWindow = CreateCardViewTemplate(plugin, pluginManager);
            defaultWindow.Tag = plugin.Descriptor.Id;
            return defaultWindow;
        }

        private Window CreateCardViewTemplate(LoadedPlugin plugin, PluginManager pluginManager)
        {
            var viewModel = new DesktopManagerViewModel(plugin.Descriptor.Id);
            if (viewModel != null)
            {
                viewModel.Name = plugin.Descriptor.Name;

                // 添加所有图标
                foreach (var kvp in plugin.IconDescriptor.IconPaths)
                {
                    var icon = new DesktopIcon()
                    {
                        Name = kvp.Key,
                        IconPath = kvp.Value,
                        Position = new Point(0, 0)
                    };

                    viewModel.AddIcon(icon);
                }

                viewModel.ArrangeIcons();

                Log.Information($"成功加载分区 '{viewModel.Name}' 数据,包含 {viewModel.Icons.Count} 个图标");
            }

            var window = new DesktopManagerWindow(plugin.Descriptor.Id);
            window.DataContext = viewModel;

            return window;
        }


        #region 明细类型窗口

        private Window CreateDetailedViewTemplate(LoadedPlugin plugin, PluginManager pluginManager)
        {
            // 使用通用的详细视图模板窗口,使用单例模式防止重复打开
            var detailedWindow = DetailedTemplateWindow.GetInstance(plugin.Descriptor);

            detailedWindow.OnSettingSaved += (s, e) =>
            {
                plugin.UpdateStyle(e.Style);
                pluginManager.SavePluginStyle(plugin.Descriptor.Id, e.Style);
            };


            // 尝试加载插件的样式文件
            try
            {
                // 根据插件内容动态填充列表项
                if (plugin.IsCodeLoaded)
                {
                    LoadDetailWindow(plugin, detailedWindow);
                }
                else
                {
                    // 如果插件代码尚未加载,添加默认项
                    detailedWindow.Style = plugin.Style ?? new PluginStyle();
                    detailedWindow.AddResourceItem("加载中...", 50, new SolidColorBrush(Colors.Orange));

                    // 异步加载插件代码
                    Task.Run(async () =>
                    {
                        await pluginManager.LoadPluginCode(plugin.Descriptor.Id);

                        // 在UI线程更新界面
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LoadDetailWindow(plugin, detailedWindow);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"加载插件样式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return detailedWindow;
        }

        /// <summary>
        /// 加载明细窗口
        /// </summary>
        private void LoadDetailWindow(LoadedPlugin plugin, DetailedTemplateWindow window)
        {
            if (plugin == null || window == null)
            {
                return;
            }

            window.Style = plugin.Style ?? new PluginStyle();

            window.Width = window.Style.WindowPosition.Width;
            window.Height = window.Style.WindowPosition.Height;
            window.Left = window.Style.WindowPosition.Left;
            window.Top = window.Style.WindowPosition.Top;
            window.Opacity = window.Style.Opacity;
            window.Title = plugin.Descriptor.Name;
            window.Key = plugin.Descriptor.PluginClassName;

            window.Loaded += (s, e) =>
            {
                SysUtil.SetDesktopLevelWindow(window);
            };
            window.Closing += (s, e) =>
            {
                plugin.Plugin?.Unregister();

                if (window.Style.CycleExecution)
                {
                    if (window.RefreshInfoTimer != null)
                    {
                        window.RefreshInfoTimer.Stop();
                        window.RefreshInfoTimer = null;
                    }
                }
            };

            LoadDetailItems(plugin, window);

            //是否周期执行
            if (window.Style.CycleExecution)
            {
                if (window.RefreshInfoTimer == null)
                {
                    window.RefreshInfoTimer = new DispatcherTimer()
                    {
                        Interval = TimeSpan.FromSeconds(window.Style.Inteval)
                    };

                    window.RefreshInfoTimer.Tick += (s, e) =>
                    {
                        UpdateDetailInfomations(plugin, window);
                    };

                    window.RefreshInfoTimer.Start();
                }
            }
        }

        /// <summary>
        /// 加载DetailWindow的列表项
        /// </summary>
        private void LoadDetailItems(LoadedPlugin plugin, DetailedTemplateWindow detailedWindow)
        {
            // 如果插件代码已加载,尝试获取插件的功能列表
            var dict = plugin.Plugin.FunctionDict as Dictionary<string, Func<PluginParameter[], object>>;
            if (dict != null && dict.Count > 0)
            {
                detailedWindow.ClearResourceItems();
                var style = detailedWindow.Style;

                int i = 0;
                foreach (var item in plugin.Plugin.FunctionDict)
                {
                    var barColor = style.BottomLineColor;
                    if (style.ItemColors != null && style.ItemColors.Count > 0)
                    {
                        barColor = style.ItemColors[i % style.ItemColors.Count];
                    }
                    object result = plugin.Plugin.Run(item.Key);
                    detailedWindow.AddResourceItem(item.Key, result, new SolidColorBrush(barColor));
                    i++;
                }
            }
            else
            {
                // 如果没有功能,添加默认项
                detailedWindow.AddResourceItem("无可用功能", 0, new SolidColorBrush(Colors.Gray));
            }
        }

        private void UpdateDetailInfomations(LoadedPlugin plugin, DetailedTemplateWindow detailedWindow)
        {
            foreach (var item in plugin.Plugin.FunctionDict)
            {
                object result = plugin.Plugin.Run(item.Key);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    detailedWindow.UpdateResourceItem(item.Key, result);
                });
            }
        }

        private PluginStyle LoadPluginStyle(string stylePath)
        {
            try
            {
                if (File.Exists(stylePath))
                {
                    string json = File.ReadAllText(stylePath);
                    return System.Text.Json.JsonSerializer.Deserialize<PluginStyle>(json);
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"解析样式文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return null;
        }

        #endregion


        private Window CreateFloatingWindowTemplate(LoadedPlugin plugin, PluginManager pluginManager)
        {
            // 使用FloatingWindow模板窗口，使用单例模式防止重复打开
            var floatingWindow = FloatingWindow.GetInstance(plugin.Descriptor);

            floatingWindow.OnSettingSaved += (s, e) =>
            {
                plugin.UpdateStyle(e.Style);
                pluginManager.SavePluginStyle(plugin.Descriptor.Id, e.Style);
            };

            // 尝试加载插件的样式文件
            try
            {
                // 根据插件内容动态填充菜单项
                if (plugin.IsCodeLoaded)
                {
                    LoadFloatingWindowMenu(plugin, floatingWindow);
                }
                else
                {
                    // 如果插件代码尚未加载，设置默认样式
                    floatingWindow.Style = plugin.Style ?? new PluginStyle();

                    // 异步加载插件代码
                    Task.Run(async () =>
                    {
                        await pluginManager.LoadPluginCode(plugin.Descriptor.Id);

                        // 在UI线程更新界面
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LoadFloatingWindowMenu(plugin, floatingWindow);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"加载插件样式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return floatingWindow;
        }

        /// <summary>
        /// 加载悬浮窗口的菜单项
        /// </summary>
        private void LoadFloatingWindowMenu(LoadedPlugin plugin, FloatingWindow window)
        {
            if (plugin == null || window == null)
            {
                return;
            }

            window.Style = plugin.Style ?? new PluginStyle();
            window.Key = plugin.Descriptor.PluginClassName;

            // 清除现有菜单项
            window.ClearMenuItems();

            // 如果插件代码已加载，尝试获取插件的功能列表
            var dict = plugin.Plugin.FunctionDict as Dictionary<string, Func<PluginParameter[], object>>;

            if (dict != null && dict.Count > 0)
            {
                var statusKey = dict.Keys.FirstOrDefault(k => k.Contains("占用率") || k.Contains("当前内存"));
                var detailsKey = dict.Keys.FirstOrDefault(k => k.Contains("详情"));
                var cleanupKey = dict.Keys.FirstOrDefault(k => k.Contains("内存整理") || k.Contains("完整整理") || k == "整理工作集");

                if (!string.IsNullOrEmpty(statusKey))
                {
                    window.ConfigureStatusProvider(
                        () => plugin.Plugin.Run(statusKey),
                        string.IsNullOrEmpty(detailsKey) ? null : () => plugin.Plugin.Run(detailsKey),
                        string.IsNullOrEmpty(cleanupKey) ? null : () => plugin.Plugin.Run(cleanupKey),
                        TimeSpan.FromSeconds(Math.Max(1, window.Style?.Inteval ?? 1)));
                }

                foreach (var key in dict.Keys)
                {
                    if (!string.IsNullOrEmpty(statusKey) &&
                        (key == statusKey || key == detailsKey || key == cleanupKey))
                    {
                        continue;
                    }

                    string iconPath = null;

                    // 尝试获取图标路径
                    if (plugin.IconDescriptor != null && plugin.IconDescriptor.IconPaths.TryGetValue(key, out var path))
                    {
                        iconPath = path;
                    }

                    // 添加菜单项
                    window.AddMenuItem(key, iconPath, () => RunFloatingWindowContextFunction(plugin.Descriptor.Id, key));
                }
            }
        }

        private void RunFloatingWindowContextFunction(string id, string key)
        {
            try
            {
                pluginManager.RunPluginFunction(id, key, null);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"执行功能失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Window CreateButtonListTemplate(LoadedPlugin plugin, PluginManager pluginManager)
        {
            var window = new Window
            {
                Title = plugin.Descriptor.Name,
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var mainPanel = new StackPanel { Orientation = Orientation.Vertical };

            // 顶部标题和启用开关
            var headerPanel = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };

            var header = new TextBlock
            {
                Text = plugin.Descriptor.Name,
                FontSize = 18,
                FontWeight = FontWeights.Bold
            };
            DockPanel.SetDock(header, Dock.Left);
            headerPanel.Children.Add(header);

            var enableSwitch = new CheckBox
            {
                Content = "启用",
                IsChecked = plugin.Descriptor.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            enableSwitch.Checked += (s, e) => pluginManager.SetPluginEnabled(plugin.Descriptor.Id, true);
            enableSwitch.Unchecked += (s, e) => pluginManager.SetPluginEnabled(plugin.Descriptor.Id, false);
            DockPanel.SetDock(enableSwitch, Dock.Right);
            headerPanel.Children.Add(enableSwitch);

            mainPanel.Children.Add(headerPanel);

            // 添加版本和作者信息
            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"版本: {plugin.Descriptor.Version}",
                Margin = new Thickness(0, 0, 15, 0),
                Foreground = Brushes.Gray
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"作者: {plugin.Descriptor.Author}",
                Foreground = Brushes.Gray
            });
            mainPanel.Children.Add(infoPanel);

            // 添加描述
            if (!string.IsNullOrEmpty(plugin.Descriptor.Description))
            {
                mainPanel.Children.Add(new TextBlock
                {
                    Text = plugin.Descriptor.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            // 功能按钮
            var buttonPanel = new StackPanel { Orientation = Orientation.Vertical };

            var dict = plugin.Plugin.FunctionDict as Dictionary<string, Func<PluginParameter[], object>>;
            if (dict != null)
            {
                foreach (var key in dict.Keys)
                {
                    var button = new Button
                    {
                        Margin = new Thickness(5),
                        Padding = new Thickness(10, 5, 10, 5)
                    };

                    // 添加图标(如果有)
                    if (plugin.IconDescriptor?.IconPaths.TryGetValue(key, out var iconPath) == true)
                    {
                        try
                        {
                            var image = new Image
                            {
                                Source = new BitmapImage(new Uri(iconPath, UriKind.Relative)),
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(0, 0, 5, 0)
                            };

                            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                            stackPanel.Children.Add(image);
                            stackPanel.Children.Add(new TextBlock { Text = key });
                            button.Content = stackPanel;
                        }
                        catch
                        {
                            button.Content = key;
                        }
                    }
                    else
                    {
                        button.Content = key;
                    }

                    var functionKey = key; // 捕获循环变量
                    button.Click += (s, e) =>
                    {
                        try
                        {
                            pluginManager.RunPluginFunction(plugin.Descriptor.Id, functionKey, null);
                        }
                        catch (Exception ex)
                        {
                            AppMessageBox.Show($"执行功能失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    buttonPanel.Children.Add(button);
                }
            }

            mainPanel.Children.Add(buttonPanel);

            var scrollViewer = new ScrollViewer
            {
                Content = mainPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var border = new Border
            {
                Child = scrollViewer,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(5),
                CornerRadius = new CornerRadius(5)
            };

            window.Content = border;
            return window;
        }

    }

    public enum TemplateWindowStyle
    {
        CardView = 1,           // 卡片窗口
        DetailedView = 2,       // 明细窗口
        FloatingWindow = 3,     // 悬浮窗口
    }
}
