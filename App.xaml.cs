using System.Configuration;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Layouter.Logs;
using Layouter.Models;
using Layouter.Services;
using Layouter.ViewModels;
using Layouter.Views;
using Layouter.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Layouter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider serviceProvider;
        private TrayIconService trayIconService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            LogConfig.Init();

            // 配置服务
            var services = new ServiceCollection();
            ConfigureServices(services);

            //添加Serilog到依赖注入容器
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(dispose: true);
            });

            serviceProvider = services.BuildServiceProvider();
            Ioc.Default.ConfigureServices(serviceProvider);

            // 插件解析器
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            // 初始化自启动设置
            GeneralSettingsService.Instance.InitializeAutoStart();

            // 初始化安全隐藏存储（并把旧版桌面隐藏目录迁移走，桌面不再残留隐藏目录）
            SecureHiddenStore.Instance.EnsureInitialized();

            // 启用「双击桌面隐藏图标」时注册桌面双击事件
            if (GeneralSettingsService.Instance.GetEnableDesktopDoubleClickToggle())
            {
                DesktopService.Instance.RegisterDesktopDoubleClickEvent();
            }

            // 启用了全文件系统搜索时，后台预热本地文件索引
            // （缓存缺失或过期才重建，已有缓存可秒级加载）
            if (GeneralSettingsService.Instance.GetEnableFullFileSystemSearch())
            {
                LocalFileIndexService.Instance.StartEnsure();
            }

            // 启动托盘图标
            TrayIconService.Instance.Initialize();

            // 注册全局快捷键（搜索、新建分区、自动排列图标、分区管理）
            RegisterHotkeys();

            // 分区恢复与插件加载放到后台优先级执行：
            // 托盘图标先出现，程序立刻可用，分区随后陆续显示出来，
            // 整个过程界面不会卡住。
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(StartupDeferredWork));
        }

        /// <summary>
        /// 注册全局快捷键
        /// </summary>
        private void RegisterHotkeys()
        {
            try
            {
                GlobalHotkeyService.Instance.Triggered += (s, action) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => ExecuteHotkeyAction(action)));
                };

                GlobalHotkeyService.RegisterConfiguredHotkeys();
            }
            catch (Exception ex)
            {
                Log.Information($"注册全局快捷键失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行全局快捷键对应的功能
        /// </summary>
        private void ExecuteHotkeyAction(HotkeyAction action)
        {
            try
            {
                switch (action)
                {
                    case HotkeyAction.Search:
                        TrayMenuService.ShowGlobalSearch();
                        break;

                    case HotkeyAction.AddPartition:
                        TrayMenuService.ShowNewPartition();
                        break;

                    case HotkeyAction.ArrangeIcons:
                        WindowManagerService.Instance.ArrangeAllPartitionsIcons();
                        break;

                    case HotkeyAction.PartitionManager:
                        TrayMenuService.ShowPartitionManager();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"执行全局快捷键功能失败({action}): {ex.Message}");
            }
        }

        /// <summary>
        /// 启动后的延迟工作：恢复分区、加载插件
        /// </summary>
        private async void StartupDeferredWork()
        {
            try
            {
                // 清理过期的图标磁盘缓存
                IconDiskCache.TrimIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Information($"清理图标缓存时出错: {ex.Message}");
            }

            try
            {
                await PartitionDataService.Instance.RestoreWindowsAsync();
            }
            catch (Exception ex)
            {
                Log.Information($"恢复分区配置失败: {ex.Message}");
            }

            try
            {
                InitializePlugins();
            }
            catch (Exception ex)
            {
                Log.Error($"初始化插件失败: {ex.Message}");
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 注册服务
            services.AddSingleton<TrayIconService>();
            services.AddSingleton<TrayMenuService>();
            services.AddSingleton<GeneralSettingsService>();
            services.AddSingleton<DesktopIconService>();
            services.AddSingleton<PartitionDataService>();
            services.AddSingleton<WindowManagerService>();
            services.AddSingleton(sp =>
            {
                string pluginsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Env.AppName,
                    "Plugins");
                return new Plugins.PluginManager(pluginsDirectory);
            });

            services.AddSingleton<TrayIconViewModel>();
            services.AddSingleton<DesktopManagerViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 保存所有分区配置
            SaveAllPartitions();

            // 清理资源
            trayIconService?.Dispose();
            serviceProvider?.Dispose();

            // 注销桌面双击事件（避免残留的窗口过程钩子）
            DesktopService.Instance.UnregisterDesktopDoubleClickEvent();

            Log.CloseAndFlush();

            base.OnExit(e);
        }

        private void SaveAllPartitions()
        {
            try
            {
                // 保存所有分区配置
                PartitionDataService.Instance.SaveAllPartitions();
            }
            catch (System.Exception ex)
            {
                Log.Information($"保存分区配置失败: {ex.Message}");
            }
        }


        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // 获取请求的程序集名称（不含版本等信息）
                string assemblyName = new AssemblyName(args.Name).Name;

                // 1. 检查已提取的插件目录
                string pluginsExtractPath = Path.Combine(Path.GetTempPath(), "Plugins");
                if (Directory.Exists(pluginsExtractPath))
                {
                    // 搜索所有插件目录
                    foreach (var pluginDir in Directory.GetDirectories(pluginsExtractPath))
                    {
                        // 查找可能的DLL
                        string potentialDllPath = Path.Combine(pluginDir, $"{assemblyName}.dll");
                        if (File.Exists(potentialDllPath))
                        {
                            return Assembly.LoadFrom(potentialDllPath);
                        }

                        // 查找可能的libs子目录
                        string libsDir = Path.Combine(pluginDir, "libs");
                        if (Directory.Exists(libsDir))
                        {
                            potentialDllPath = Path.Combine(libsDir, $"{assemblyName}.dll");
                            if (File.Exists(potentialDllPath))
                            {
                                return Assembly.LoadFrom(potentialDllPath);
                            }
                        }
                    }
                }

                // 2. 检查应用程序目录的libs文件夹
                string appLibsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs");
                if (Directory.Exists(appLibsDir))
                {
                    string potentialDllPath = Path.Combine(appLibsDir, $"{assemblyName}.dll");
                    if (File.Exists(potentialDllPath))
                    {
                        return Assembly.LoadFrom(potentialDllPath);
                    }
                }

                Console.WriteLine($"无法解析程序集: {args.Name}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程序集解析过程中出错: {ex.Message}");
                return null;
            }
        }

        private void InitializePlugins()
        {
            try
            {
                var pluginManager = serviceProvider.GetRequiredService<Plugins.PluginManager>();

                // 加载插件元数据并自动显示插件窗口
                pluginManager.LoadPluginsMetadata(true);

                Log.Information($"已加载 {pluginManager.Plugins.Count} 个插件");
            }
            catch (Exception ex)
            {
                Log.Error($"初始化插件失败: {ex.Message}");
            }
        }
    }
}


