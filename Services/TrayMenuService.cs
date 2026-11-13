using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Layouter.Plugins;
using Layouter.ViewModels;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using FluentIcons.Wpf;
using Hardcodet.Wpf.TaskbarNotification;
using Layouter.Views;
using System.Reflection;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Windows.Media.Media3D;
using System.IO;
using PluginEntry;

namespace Layouter.Services
{
    /// <summary>
    /// 托盘菜单管理器
    /// </summary>
    public class TrayMenuService
    {
        private static readonly Lazy<TrayMenuService> instance = new Lazy<TrayMenuService>(() => new TrayMenuService());
        public static TrayMenuService Instance => instance.Value;

        private ContextMenu trayMenu;
        private Dictionary<string, MenuItem> menuItems;
        private TrayIconViewModel viewModel;

        private TrayMenuService()
        {
            menuItems = new Dictionary<string, MenuItem>();
        }

        /// <summary>
        /// 初始化托盘菜单
        /// </summary>
        /// <param name="viewModel">托盘图标视图模型</param>
        public void Initialize(TrayIconViewModel vm)
        {
            this.viewModel = vm;
            CreateTrayMenu();
        }

        /// <summary>
        /// 创建托盘菜单
        /// </summary>
        private void CreateTrayMenu()
        {
            trayMenu = new ContextMenu();
            Style menuStyle = Application.Current.FindResource("TrayMenuStyle") as Style;
            if (menuStyle != null)
            {
                trayMenu.Style = menuStyle;
            }

            // 创建基本菜单项
            CreateBasicMenuItems();

            // 创建插件菜单项
            CreatePluginMenuItems();

            // 创建关于和退出菜单项
            CreateAboutAndExitMenuItems();
        }

        /// <summary>
        /// 创建基本菜单项
        /// </summary>
        private void CreateBasicMenuItems()
        {
            // 新建分区菜单项
            var newPartitionItem = CreateMenuItem("新建分区", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.AddCircle, Colors.DodgerBlue));
            newPartitionItem.Click += (s, e) => viewModel.NewWindowCommand.Execute(null);
            trayMenu.Items.Add(newPartitionItem);
            menuItems["NewPartition"] = newPartitionItem;

            // 分区设置菜单项
            var partitionSettingsItem = CreateMenuItem("分区管理", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Settings, Colors.DodgerBlue));
            partitionSettingsItem.Click += (s, e) => viewModel.WindowSettingCommand.Execute(null);
            trayMenu.Items.Add(partitionSettingsItem);
            menuItems["PartitionSettings"] = partitionSettingsItem;

            // 备份与还原菜单项
            var backupManagerItem = CreateMenuItem("备份与还原", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Save, Colors.DodgerBlue));
            backupManagerItem.Click += (s, e) => ShowBackupManager();
            trayMenu.Items.Add(backupManagerItem);
            menuItems["BackupManager"] = backupManagerItem;

            // 设置菜单项
            var settingsMenuItem = CreateMenuItem("设置", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Settings, Colors.DodgerBlue));
            trayMenu.Items.Add(settingsMenuItem);
            menuItems["Settings"] = settingsMenuItem;

            CreateSettingsMenuItems(settingsMenuItem);

            // 添加分隔符
            trayMenu.Items.Add(new Separator());
        }

        /// <summary>
        /// 创建设置子菜单（自启动、双击桌面隐藏元素、全局分区设置）
        /// </summary>
        private void CreateSettingsMenuItems(MenuItem settingsMenuItem)
        {
            // 开机自启动菜单项
            var autoStartMenuItem = CreateMenuItem("开机自启动", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Power, Colors.DodgerBlue));
            settingsMenuItem.Items.Add(autoStartMenuItem);
            menuItems["AutoStart"] = autoStartMenuItem;

            // 自启动状态切换菜单项
            bool isAutoStartEnabled = GeneralSettingsService.Instance.GetAutoStartEnabled();
            var autoStartIcon = isAutoStartEnabled ? IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Bookmark, Colors.DodgerBlue) : IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.BookmarkOff, Colors.DodgerBlue);
            var autoStartToggleItem = CreateMenuItem(isAutoStartEnabled ? "开启" : "停止", autoStartIcon);
            autoStartToggleItem.Click += (s, e) => ToggleAutoStart(autoStartToggleItem);
            autoStartMenuItem.Items.Add(autoStartToggleItem);
            menuItems["AutoStartToggle"] = autoStartToggleItem;

            // 双击桌面隐藏元素菜单项
            var desktopToggleMenuItem = CreateMenuItem("双击桌面隐藏元素", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Eye, Colors.DodgerBlue));
            settingsMenuItem.Items.Add(desktopToggleMenuItem);
            menuItems["DesktopDoubleClickToggle"] = desktopToggleMenuItem;

            bool isDesktopToggleEnabled = GeneralSettingsService.Instance.GetEnableDesktopDoubleClickToggle();
            var desktopToggleIcon = isDesktopToggleEnabled ? IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Bookmark, Colors.DodgerBlue) : IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.BookmarkOff, Colors.DodgerBlue);
            var desktopToggleItem = CreateMenuItem(isDesktopToggleEnabled ? "开启" : "停止", desktopToggleIcon);
            desktopToggleItem.Click += (s, e) => ToggleDesktopDoubleClick(desktopToggleItem);
            desktopToggleMenuItem.Items.Add(desktopToggleItem);
            menuItems["DesktopDoubleClickToggleItem"] = desktopToggleItem;

            // 全局分区设置菜单项
            var globalSettingsItem = CreateMenuItem("全局分区设置", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.PaintBrush, Colors.DodgerBlue));
            globalSettingsItem.Click += (s, e) => ShowGlobalPartitionSettings();
            settingsMenuItem.Items.Add(globalSettingsItem);
            menuItems["GlobalPartitionSettings"] = globalSettingsItem;
        }

        /// <summary>
        /// 切换「双击桌面隐藏元素」功能
        /// </summary>
        private void ToggleDesktopDoubleClick(MenuItem menuItem)
        {
            try
            {
                bool currentState = GeneralSettingsService.Instance.GetEnableDesktopDoubleClickToggle();
                bool newState = !currentState;

                GeneralSettingsService.Instance.SetEnableDesktopDoubleClickToggle(newState);

                if (newState)
                {
                    DesktopService.Instance.RegisterDesktopDoubleClickEvent();
                }
                else
                {
                    DesktopService.Instance.UnregisterDesktopDoubleClickEvent();
                }

                // 更新菜单项文本与图标
                menuItem.Header = newState ? "开启" : "停止";
                menuItem.Icon = newState
                    ? IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Bookmark, Colors.DodgerBlue)
                    : IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.BookmarkOff, Colors.DodgerBlue);

                // 显示提示
                string message = newState
                    ? "已开启双击桌面隐藏图标，双击桌面空白处即可切换"
                    : "已停止双击桌面隐藏图标";
                TrayIconService.Instance.ShowBalloonTip("桌面双击设置", message, BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Information($"切换双击桌面隐藏图标失败: {ex.Message}");
                TrayIconService.Instance.ShowBalloonTip("错误", $"设置失败: {ex.Message}", BalloonIcon.Error);
            }
        }

        /// <summary>
        /// 打开全局分区设置窗口
        /// </summary>
        private void ShowGlobalPartitionSettings()
        {
            try
            {
                PartitionManagerViewModel.ShowGlobalPartitionSettings();
            }
            catch (Exception ex)
            {
                Log.Information($"打开全局分区设置失败: {ex.Message}");
                AppMessageBox.Show($"打开全局分区设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 切换自启动状态
        /// </summary>
        private void ToggleAutoStart(MenuItem menuItem)
        {
            try
            {
                bool currentState = GeneralSettingsService.Instance.GetAutoStartEnabled();
                bool newState = !currentState;

                // 更新设置
                GeneralSettingsService.Instance.SetAutoStartEnabled(newState);

                // 更新菜单项文本
                menuItem.Header = newState ? "开启" : "停止";
                menuItem.Icon = newState ? IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.Bookmark, Colors.DodgerBlue) : IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.BookmarkOff, Colors.DodgerBlue);

                // 显示提示
                string message = newState ? "已设置开机自启动" : "已取消开机自启动";
                TrayIconService.Instance.ShowBalloonTip("自启动设置", message, BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Information($"切换自启动状态失败: {ex.Message}");
                TrayIconService.Instance.ShowBalloonTip("错误", $"设置自启动失败: {ex.Message}", BalloonIcon.Error);
            }
        }

        /// <summary>
        /// 创建插件菜单项
        /// </summary>
        private void CreatePluginMenuItems()
        {
            // 插件子菜单
            var pluginsMenuItem = CreateMenuItem("插件", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.PlugConnected, Colors.DodgerBlue));
            trayMenu.Items.Add(pluginsMenuItem);
            menuItems["Plugins"] = pluginsMenuItem;

            // 插件管理菜单项
            var pluginManagerItem = CreateMenuItem("插件管理", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.PlugConnectedSettings, Colors.DodgerBlue));
            pluginManagerItem.Click += (s, e) => ShowPluginManager();
            pluginsMenuItem.Items.Add(pluginManagerItem);

            // 创建插件菜单项
            var createPluginItem = CreateMenuItem("创建插件", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.PlugConnectedAdd, Colors.DodgerBlue));
            createPluginItem.Click += (s, e) => CreateNewPlugin();
            pluginsMenuItem.Items.Add(createPluginItem);

            // 添加分隔符
            trayMenu.Items.Add(new Separator());
        }

        /// <summary>
        /// 创建退出菜单项
        /// </summary>
        private void CreateAboutAndExitMenuItems()
        {
            // 关于菜单项
            var aboutMenuItem = CreateMenuItem("关于", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.ErrorCircle, Colors.DodgerBlue));
            trayMenu.Items.Add(aboutMenuItem);
            menuItems["About"] = aboutMenuItem;

            var versionMenuItem = CreateMenuItem($"当前版本({Assembly.GetExecutingAssembly().GetName().Version})");
            aboutMenuItem.Items.Add(versionMenuItem);

            var checkNewMenuItem = CreateMenuItem($"检测新版本", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.New, Colors.DodgerBlue));
            aboutMenuItem.Items.Add(checkNewMenuItem);

            checkNewMenuItem.Click += (s, e) =>
            {

                UpdateWindow updateWindow = new UpdateWindow();
                updateWindow.ShowDialog();
            };

            var contactMenuItem = CreateMenuItem($"欢迎建议 (@VrezenStrijder)", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.ContactCardLink, Colors.DodgerBlue));
            contactMenuItem.Click += (s, e) =>
            {
                Process.Start(new ProcessStartInfo("https://github.com/VrezenStrijder/Layouter")
                {
                    UseShellExecute = true
                });
            };
            aboutMenuItem.Items.Add(contactMenuItem);

            // 退出菜单项
            var exitItem = CreateMenuItem("退出", IconUtil.CreateMenuItemIcon(FluentIcons.Common.Symbol.ArrowExit, Colors.DodgerBlue));
            exitItem.Click += (s, e) => viewModel.ExitCommand.Execute(null);
            trayMenu.Items.Add(exitItem);
        }

        /// <summary>
        /// 获取托盘菜单
        /// </summary>
        public ContextMenu GetTrayMenu()
        {
            return trayMenu;
        }

        /// <summary>
        /// 创建菜单项并应用样式和图标
        /// </summary>
        /// <param name="header">菜单项标题</param>
        /// <param name="icon">图标</param>
        /// <returns>创建的菜单项</returns>
        private MenuItem CreateMenuItem(string header, SymbolIcon icon = null)
        {
            var menuItem = new MenuItem { Header = header };

            // 应用菜单项样式
            Style menuItemStyle = Application.Current.FindResource("TrayMenuItemStyle") as Style;
            if (menuItemStyle != null)
            {
                menuItem.Style = menuItemStyle;
            }

            // 添加图标
            if (icon != null)
            {
                try
                {
                    #region 从文件中加载图标
                    //string iconPath = $"/Layouter;component/Resources/Icons/{iconName}";
                    //var iconUri = new Uri(iconPath, UriKind.Relative);
                    //var iconImage = new Image
                    //{
                    //    Source = new BitmapImage(iconUri),
                    //    Width = 16,
                    //    Height = 16
                    //};
                    #endregion

                    menuItem.Icon = icon;
                }
                catch (Exception ex)
                {
                    Log.Information($"加载菜单[{header}]的图标时出错: {ex.Message}");
                }
            }

            return menuItem;
        }

        /// <summary>
        /// 显示插件管理窗口
        /// </summary>
        private void ShowPluginManager()
        {
            try
            {
                var createFunc = () =>
                {
                    return new PluginsManagerWindow();
                };

                SingletonWindowManager.Instance.ShowWindow<PluginsManagerWindow>(createFunc, false);
            }
            catch (Exception ex)
            {
                Log.Information($"显示插件管理窗口时出错: {ex.Message}");
                AppMessageBox.Show($"显示插件管理窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 显示备份与还原窗口
        /// </summary>
        private void ShowBackupManager()
        {
            try
            {
                SingletonWindowManager.Instance.ShowWindow<BackupManagerWindow>(() => new BackupManagerWindow(), false);
            }
            catch (Exception ex)
            {
                Log.Information($"显示备份与还原窗口时出错: {ex.Message}");
                AppMessageBox.Show($"显示备份与还原窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateNewPlugin()
        {
            try
            {
                var createFunc = () =>
                {
                    var window = new PluginsManagerWindow();
                    window.CreatePluginCommand.Execute(null);

                    return window;
                };

                SingletonWindowManager.Instance.ShowWindow<PluginsManagerWindow>(createFunc, false);
            }
            catch (Exception ex)
            {
                Log.Information($"显示创建插件窗口时出错: {ex.Message}");
                AppMessageBox.Show($"显示创建插件窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

    }
}


