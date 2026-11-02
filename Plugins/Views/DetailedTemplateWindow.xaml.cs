using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using Layouter.Utility;
using static System.Net.Mime.MediaTypeNames;
using Path = System.IO.Path;
using PluginEntry;

namespace Layouter.Plugins.Views
{
    public partial class DetailedTemplateWindow : Window
    {
        private string pluginId;
        private Dictionary<string, ResourceItemControls> resourceItems = new Dictionary<string, ResourceItemControls>();
        private double defaultOpacity = 1.0;
        private double hoverOpacity = 1.0;
        private static Dictionary<string, DetailedTemplateWindow> openWindows = new Dictionary<string, DetailedTemplateWindow>();

        private PluginStyle style;
        // 内容背景
        private SolidColorBrush contentBackground = new SolidColorBrush(Color.FromArgb(80, 51, 51, 51));
        // 标题栏背景
        private SolidColorBrush titleBackground = new SolidColorBrush(Color.FromArgb(120, 51, 51, 51));
        
        // 窗口卷起状态
        private bool isRolledUp = false;
        // 记录窗口原始高度
        private double originalHeight;
        // 标题栏高度
        private const double TitleBarHeight = 30;

        public event EventHandler<SettingSavedEventArgs> OnSettingSaved;


        public DetailedTemplateWindow()
        {
            InitializeComponent();
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
        }

        public DetailedTemplateWindow(PluginDescriptor pd) : this()
        {
            pluginId = pd.Id;
            string key = pd.Key;

            // 从插件描述文件中获取Name值作为窗口标题
            try
            {
                string pluginsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Utility.Env.AppName, "Plugins");
                string pluginPath = Path.Combine(pluginsDirectory, $"{key}.plug");

                if (File.Exists(pluginPath))
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        ZipFile.ExtractToDirectory(pluginPath, tempDir);
                        string pluginJsonPath = Path.Combine(tempDir, key, "plugin.json");

                        if (File.Exists(pluginJsonPath))
                        {
                            string json = File.ReadAllText(pluginJsonPath);
                            using (JsonDocument doc = JsonDocument.Parse(json))
                            {
                                if (doc.RootElement.TryGetProperty("Name", out JsonElement nameElement))
                                {
                                    this.Title = nameElement.GetString();
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Create DetailedTemplateWindow Error: {ex.Message}");
            }
        }

        public DispatcherTimer RefreshInfoTimer { get; set; }

        public string Key { get; set; }

        public PluginStyle Style
        {
            get { return style; }
            set
            {
                style = value;
                if (style != null)
                {
                    ApplyStyle();
                }
            }
        }

        public SolidColorBrush ContentBackground
        {
            get { return contentBackground; }
            set { contentBackground = value; }
        }

        public SolidColorBrush TitleBackground
        {
            get { return titleBackground; }
            set { titleBackground = value; }
        }


        // 应用样式到窗口
        private void ApplyStyle()
        {
            if (style == null)
            {
                return;
            }

            var wp = style.WindowPosition;
            this.Left = wp.Left;
            this.Top = wp.Top;
            this.Width = wp.Width;
            this.Height = wp.Height;
            
            // 保存原始高度，用于卷起/展开功能
            originalHeight = wp.Height;

            defaultOpacity = style.Opacity;
            hoverOpacity = Math.Min(1.0, style.Opacity + 0.3);
            this.Opacity = defaultOpacity;

            // 设置内容区域背景色（半透明）
            byte contentAlpha = (byte)(255 * 0.5);
            byte titleAlpha = (byte)(255 * 0.8);
            Color bgColor = style.BackgroundColor;
            ContentBackground = new SolidColorBrush(Color.FromArgb(contentAlpha, bgColor.R, bgColor.G, bgColor.B));
            TitleBackground = new SolidColorBrush(Color.FromArgb(titleAlpha, bgColor.R, bgColor.G, bgColor.B));
            
            // 应用卷起状态
            isRolledUp = style.IsRolledUp;
            if (isRolledUp)
            {
                RollUp();
            }
        }

        // 单例模式获取窗口实例
        public static DetailedTemplateWindow GetInstance(PluginDescriptor pd)
        {
            var pluginId = pd.Id;
            if (openWindows.TryGetValue(pluginId, out var existingWindow))
            {
                if (IsWindowOpen(existingWindow))
                {
                    existingWindow.Activate();
                    if (existingWindow.WindowState == WindowState.Minimized)
                    {
                        existingWindow.WindowState = WindowState.Normal;
                    }
                    return existingWindow;
                }
                else
                {
                    openWindows.Remove(pluginId);
                }
            }

            var window = new DetailedTemplateWindow(pd);
            openWindows[pluginId] = window;
            window.Closed += (s, e) => openWindows.Remove(pluginId);
            return window;
        }

        private static bool IsWindowOpen(Window window)
        {
            try
            {
                return (window != null) && (PresentationSource.FromVisual(window) != null);
            }
            catch
            {
                return false;
            }
        }


        #region 窗口拖动和交互

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }


        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            this.Opacity = hoverOpacity;
            
            // 如果窗口处于卷起状态，鼠标进入时临时展开
            if (isRolledUp)
            {
                TemporarilyUnroll();
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            this.Opacity = defaultOpacity;
            
            // 如果窗口处于卷起状态，鼠标离开时恢复卷起
            if (isRolledUp)
            {
                RollUp();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // 创建右键菜单
            ContextMenu menu = new ContextMenu();
            
            // 添加窗口设置菜单项
            MenuItem settingsItem = new MenuItem() { Header = "窗口设置" };
            settingsItem.Click += (s, args) => CreateStyleSettingDialog();
            menu.Items.Add(settingsItem);
            
            // 添加卷起窗口菜单项
            MenuItem rollUpItem = new MenuItem() { Header = isRolledUp ? "展开窗口" : "卷起窗口" };
            rollUpItem.Click += (s, args) => ToggleRollUp();
            menu.Items.Add(rollUpItem);
            
            // 显示菜单
            menu.IsOpen = true;
        }

        //private void WindowSettings_Click(object sender, RoutedEventArgs e)
        //{
        //    SettingsPopup.IsOpen = false;
        //    CreateStyleSettingDialog();
        //}


        private void CreateStyleSettingDialog()
        {
            // 创建设置窗口
            var settingsWindow = new Window
            {
                Title = "窗口设置",
                Width = 400,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            // 创建滚动面板
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            };

            // 创建主面板
            var mainPanel = new StackPanel
            {
                Margin = new Thickness(10)
            };

            // 创建临时样式对象用于编辑
            var editStyle = new PluginStyle
            {
                WindowPosition = Style.WindowPosition,
                Opacity = Style.Opacity,
                ItemHeight = Style.ItemHeight,
                ItemFontSize = Style.ItemFontSize,
                BackgroundColor = Style.BackgroundColor,
                ForegroundColor = Style.ForegroundColor,
                BottomLineHeight = Style.BottomLineHeight,
                BottomLineColor = Style.BottomLineColor,
                PercentageMode = Style.PercentageMode,
                CycleExecution = Style.CycleExecution,
                Inteval = Style.Inteval
            };

            // 添加数值型属性编辑控件
            AddNumberEditor(mainPanel, "窗口宽度", editStyle.WindowPosition.Width, (value) => editStyle.WindowPosition.Width = value);
            AddNumberEditor(mainPanel, "窗口高度", editStyle.WindowPosition.Height, (value) => editStyle.WindowPosition.Height = value);
            AddNumberEditor(mainPanel, "透明度", editStyle.Opacity, (value) => editStyle.Opacity = value, 0, 1, 0.1);
            AddNumberEditor(mainPanel, "列表项高度", editStyle.ItemHeight, (value) => editStyle.ItemHeight = value);
            AddNumberEditor(mainPanel, "字体大小", editStyle.ItemFontSize, (value) => editStyle.ItemFontSize = value);
            AddNumberEditor(mainPanel, "底部线高度", editStyle.BottomLineHeight, (value) => editStyle.BottomLineHeight = value, 0, 10, 0.5);
            AddNumberEditor(mainPanel, "刷新间隔(s)", editStyle.Inteval, (value) => editStyle.Inteval = value, 0.1, 60, 0.1);

            // 添加颜色选择器
            AddColorEditor(mainPanel, "背景颜色", editStyle.BackgroundColor, (color) => editStyle.BackgroundColor = color);
            AddColorEditor(mainPanel, "前景颜色", editStyle.ForegroundColor, (color) => editStyle.ForegroundColor = color);
            AddColorEditor(mainPanel, "底部线颜色", editStyle.BottomLineColor, (color) => editStyle.BottomLineColor = color);

            // 添加布尔值选择器
            AddBooleanEditor(mainPanel, "百分比模式", editStyle.PercentageMode, (value) => editStyle.PercentageMode = value);
            AddBooleanEditor(mainPanel, "周期执行", editStyle.CycleExecution, (value) => editStyle.CycleExecution = value);
            AddBooleanEditor(mainPanel, "卷起窗口", editStyle.IsRolledUp, (value) => editStyle.IsRolledUp = value);

            // 添加按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            // 保存按钮
            var saveButton = new Button
            {
                Content = "保存",
                Padding = new Thickness(15, 5, 15, 5),
                Margin = new Thickness(5)
            };
            saveButton.Click += (s, args) =>
            {
                // 保存设置
                editStyle.WindowPosition.Left = this.Left;
                editStyle.WindowPosition.Top = this.Top;
                SaveSettings(editStyle);
                settingsWindow.Close();
            };

            // 取消按钮
            var cancelButton = new Button
            {
                Content = "取消",
                Padding = new Thickness(15, 5, 15, 5),
                Margin = new Thickness(5)
            };
            cancelButton.Click += (s, args) => settingsWindow.Close();

            buttonPanel.Children.Add(saveButton);
            buttonPanel.Children.Add(cancelButton);
            mainPanel.Children.Add(buttonPanel);

            scrollViewer.Content = mainPanel;
            settingsWindow.Content = scrollViewer;
            settingsWindow.ShowDialog();

        }

        // 添加数值编辑器
        private void AddNumberEditor(StackPanel panel, string label, double value, Action<double> setter, double min = 0, double max = 1000, double step = 1)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 5, 0, 5)
            };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var editor = new Grid();
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                SmallChange = step,
                LargeChange = step * 5,
                TickFrequency = step,
                IsSnapToTickEnabled = true
            };

            var textBox = new TextBox
            {
                Text = value.ToString("F2"),
                Width = 60,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            slider.ValueChanged += (s, e) =>
            {
                textBox.Text = slider.Value.ToString("F2");
                setter(slider.Value);
            };

            textBox.TextChanged += (s, e) =>
            {
                if (double.TryParse(textBox.Text, out double newValue))
                {
                    if (newValue < min) newValue = min;
                    if (newValue > max) newValue = max;

                    slider.Value = newValue;
                    setter(newValue);
                }
            };

            Grid.SetColumn(slider, 0);
            Grid.SetColumn(textBox, 1);

            editor.Children.Add(slider);
            editor.Children.Add(textBox);

            container.Children.Add(editor);
            panel.Children.Add(container);
        }

        // 添加颜色编辑器
        private void AddColorEditor(StackPanel panel, string label, Color color, Action<Color> setter)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 5, 0, 5)
            };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 5)
            });

            // 创建颜色预览和选择按钮的水平布局
            var colorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // 颜色预览矩形
            var colorPreview = new Rectangle
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(5),
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Fill = new SolidColorBrush(color)
            };

            // 选择颜色按钮
            var colorButton = new Button
            {
                Content = "选择颜色",
                Margin = new Thickness(5)
            };

            colorButton.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.ColorDialog
                {
                    Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B),
                    FullOpen = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var newColor = Color.FromArgb(
                        dialog.Color.A,
                        dialog.Color.R,
                        dialog.Color.G,
                        dialog.Color.B);

                    colorPreview.Fill = new SolidColorBrush(newColor);
                    setter(newColor);
                }
            };

            colorPanel.Children.Add(colorPreview);
            colorPanel.Children.Add(colorButton);

            container.Children.Add(colorPanel);
            panel.Children.Add(container);
        }



        // 添加布尔值编辑器
        private void AddBooleanEditor(StackPanel panel, string label, bool value, Action<bool> setter)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 5, 0, 5)
            };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var checkBox = new CheckBox
            {
                IsChecked = value,
                Content = value ? "是" : "否",
                Margin = new Thickness(0, 0, 0, 0)
            };

            checkBox.Checked += (s, e) =>
            {
                checkBox.Content = "是";
                setter(true);
            };

            checkBox.Unchecked += (s, e) =>
            {
                checkBox.Content = "否";
                setter(false);
            };

            container.Children.Add(checkBox);
            panel.Children.Add(container);
        }
        
        #region 窗口卷起功能
        
        // 切换窗口卷起状态
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
            if (style != null)
            {
                style.IsRolledUp = isRolledUp;
                SaveSettings(style);
            }
        }
        
        // 卷起窗口（只显示标题栏）
        private void RollUp()
        {
            // 设置窗口高度为标题栏高度
            this.Height = TitleBarHeight;
            
            // 隐藏内容区域
            if (listPanel != null && listPanel.Parent is FrameworkElement parent)
            {
                parent.Visibility = Visibility.Collapsed;
            }
            
            // 隐藏设置按钮
            if (SettingsButton != null)
            {
                SettingsButton.Visibility = Visibility.Collapsed;
            }
        }
        
        // 展开窗口
        private void Unroll()
        {
            // 恢复窗口原始高度
            this.Height = originalHeight;
            
            // 显示内容区域
            if (listPanel != null && listPanel.Parent is FrameworkElement parent)
            {
                parent.Visibility = Visibility.Visible;
            }
            
            // 显示设置按钮
            if (SettingsButton != null)
            {
                SettingsButton.Visibility = Visibility.Visible;
            }
        }
        
        // 临时展开窗口（鼠标悬停时）
        private void TemporarilyUnroll()
        {
            // 显示内容区域
            if (listPanel != null && listPanel.Parent is FrameworkElement parent)
            {
                parent.Visibility = Visibility.Visible;
            }
            
            // 显示设置按钮
            if (SettingsButton != null)
            {
                SettingsButton.Visibility = Visibility.Visible;
            }
            
            // 恢复窗口原始高度
            this.Height = originalHeight;
        }
        
        #endregion

        // 保存设置
        private void SaveSettings(PluginStyle style)
        {
            try
            {
                Style = style;
                OnSettingSaved?.Invoke(this, new SettingSavedEventArgs(style));
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        public void AddResourceItem(string name, object value, Brush progressBarColor)
        {
            if (value == null)
            {
                return;
            }

            var itemControls = new ResourceItemControls();

            var itemBorder = new Border
            {
                Padding = new Thickness(0, 5, 0, 5),
                Background = Brushes.Transparent
            };

            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Background = Brushes.Transparent
            };

            // 资源名称和使用率
            var headerPanel = new Grid
            {
                Background = Brushes.Transparent
            };
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Style.ForegroundColor),
                FontSize = Style.ItemFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(nameText, 0);
            headerPanel.Children.Add(nameText);

            var valueText = new TextBlock
            {
                Text = Style.PercentageMode ? $"{value}%" : value.ToString(),
                Foreground = new SolidColorBrush(Style.ForegroundColor),
                FontSize = Style.ItemFontSize,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent
            };
            Grid.SetColumn(valueText, 1);
            headerPanel.Children.Add(valueText);

            itemPanel.Children.Add(headerPanel);

            itemControls.ValueText = valueText;

            // 百分比进度条
            if (Style.PercentageMode)
            {
                // 使用半透明背景
                byte alpha = (byte)(255 * 0.2); // 20%透明度
                Color bgColor = Style.BackgroundColor;
                var progressBarBackground = new SolidColorBrush(Color.FromArgb(alpha, 30, 30, 30));

                var progressBarBorder = new Border
                {
                    Background =  progressBarBackground,
                    Height = Math.Max(2, Math.Floor(Style.ItemFontSize / 2)),
                    Margin = new Thickness(0, 5, 0, 0)
                };

                var progressBar = new Border
                {
                    Background = progressBarColor,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = (Convert.ToDouble(value.ToString()) / 100) * (Width - 20),
                    Height = Math.Max(2, Math.Floor(Style.ItemFontSize / 2)),
                };
                progressBarBorder.Child = progressBar;
                itemPanel.Children.Add(progressBarBorder);

                itemControls.ProgressBar = progressBar;
            }

            // 底部线
            if (Style.BottomLineHeight > 0)
            {
                var bottomLine = new Rectangle
                {
                    Height = Style.BottomLineHeight,
                    Fill = new SolidColorBrush(Style.BottomLineColor),
                    Margin = new Thickness(0, 1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                itemPanel.Children.Add(bottomLine);

                itemBorder.Child = itemPanel;
                listPanel.Children.Add(itemBorder);

                itemControls.BottomLine = bottomLine;
            }

            // 存储控件引用
            resourceItems[name] = itemControls;
        }

        public void UpdateResourceItem(string name, object value)
        {
            if (value == null)
            {
                return;
            }

            if (resourceItems.TryGetValue(name, out var controls))
            {
                controls.ValueText.Text = Style.PercentageMode ? $"{value}%" : value.ToString();

                if (controls.ProgressBar != null)
                {
                    controls.ProgressBar.Width = (Convert.ToDouble(value.ToString()) / 100) * (Width - 20);
                }

                if (controls.BottomLine != null)
                {
                    double val = Convert.ToDouble(value.ToString());

                    // 根据使用率改变底部线的颜色
                    if (val > 80)
                    {
                        controls.BottomLine.Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)); // 红色
                    }
                    else if (val > 60)
                    {
                        controls.BottomLine.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // 橙色
                    }
                    else
                    {
                        controls.BottomLine.Fill = new SolidColorBrush(Style.BottomLineColor);
                    }
                }

            }
        }

        public void ClearResourceItems()
        {
            listPanel.Children.Clear();
            resourceItems.Clear();
        }

        /// <summary>
        /// 用于存储资源项的控件引用
        /// </summary>
        private class ResourceItemControls
        {
            public TextBlock ValueText { get; set; }
            public Border? ProgressBar { get; set; }
            public Rectangle? BottomLine { get; set; }
        }

    }

    public class SettingSavedEventArgs : EventArgs
    {
        public PluginStyle Style { get; }

        public SettingSavedEventArgs(PluginStyle style)
        {
            Style = style;
        }
    }
}
