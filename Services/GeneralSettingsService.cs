using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Layouter.Models;
using Microsoft.Win32;

namespace Layouter.Services
{
    /// <summary>
    /// 通用设置服务，管理应用程序的通用设置
    /// </summary>
    public class GeneralSettingsService
    {
        private static readonly Lazy<GeneralSettingsService> instance = new Lazy<GeneralSettingsService>(() => new GeneralSettingsService());
        public static GeneralSettingsService Instance => instance.Value;

        private readonly string settingsFolder;
        private readonly string settingsFilePath;
        private GeneralSettings settings;

        public event EventHandler<PartitionVisibilityChangedEventArgs> PartitionVisibilityChanged;

        private GeneralSettingsService()
        {
            settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Layouter");
            settingsFilePath = Path.Combine(settingsFolder, "generalSettings.json");
            settings = LoadSettings();
        }

        /// <summary>
        /// 加载通用设置
        /// </summary>
        private GeneralSettings LoadSettings()
        {
            try
            {
                if (!Directory.Exists(settingsFolder))
                {
                    Directory.CreateDirectory(settingsFolder);
                }

                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    GeneralSettings loaded = JsonSerializer.Deserialize<GeneralSettings>(json) ?? new GeneralSettings();

                    NormalizeHotkeys(loaded);

                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"加载通用设置失败: {ex.Message}");
            }

            return new GeneralSettings();
        }

        /// <summary>
        /// 规整快捷键配置
        /// </summary>
        /// <remarks>
        /// 兼容旧配置：2.9.0 之前搜索快捷键单独存在 <see cref="GeneralSettings.SearchHotkey"/>，
        /// 这里把它迁移进统一的快捷键表。
        /// </remarks>
        private static void NormalizeHotkeys(GeneralSettings loaded)
        {
            if (loaded.Hotkeys == null)
            {
                loaded.Hotkeys = new Dictionary<string, string>();
            }

            string searchKey = HotkeyAction.Search.ToString();

            if (!loaded.Hotkeys.ContainsKey(searchKey) && !string.IsNullOrEmpty(loaded.SearchHotkey))
            {
                loaded.Hotkeys[searchKey] = loaded.SearchHotkey;
            }
        }

        /// <summary>
        /// 保存通用设置
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(settingsFolder))
                {
                    Directory.CreateDirectory(settingsFolder);
                }

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Log.Information($"保存通用设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取自启动状态
        /// </summary>
        public bool GetAutoStartEnabled()
        {
            return settings.AutoStartEnabled;
        }

        /// <summary>
        /// 设置自启动状态
        /// </summary>
        /// <remarks>
        /// 只有在系统启动项写入成功后才写入配置，
        /// 否则界面状态会和真实启动项不一致，出现"显示已开启但开机不启动"。
        /// </remarks>
        /// <returns>系统启动项是否已按预期写入或删除</returns>
        public bool SetAutoStartEnabled(bool enabled)
        {
            if (!ApplyAutoStartSetting(enabled))
            {
                return false;
            }

            if (settings.AutoStartEnabled != enabled)
            {
                settings.AutoStartEnabled = enabled;
                SaveSettings();
            }

            return true;
        }

        /// <summary>
        /// 应用自启动设置到系统
        /// </summary>
        private bool ApplyAutoStartSetting(bool enabled)
        {
            const string appName = "Layouter";
            const string runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string approvedKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

            try
            {
                if (!enabled)
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
                    {
                        runKey?.DeleteValue(appName, false);
                    }

                    // 一并清掉任务管理器的启用记录，避免下次开启时继承"已禁用"标记
                    using (RegistryKey approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true))
                    {
                        approvedKey?.DeleteValue(appName, false);
                    }

                    return true;
                }

                string exePath = ResolveExecutablePath();

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    throw new InvalidOperationException("无法确定程序的可执行文件路径");
                }

                // 统一加引号，路径含空格时不会被拆成多个参数
                string command = "\"" + exePath + "\"";

                using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(runKeyPath, true))
                {
                    runKey?.SetValue(appName, command, RegistryValueKind.String);
                }

                // 用户在任务管理器里禁用过启动项时，StartupApproved 会留下 03 开头的禁用标记，
                // 只删掉该标记，启动项才会重新显示为"已启用"并真正执行。
                using (RegistryKey approvedKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true))
                {
                    approvedKey?.DeleteValue(appName, false);
                }

                // 回读校验：被安全软件拦截时 SetValue 可能静默失败
                string actual = null;

                using (RegistryKey checkKey = Registry.CurrentUser.OpenSubKey(runKeyPath))
                {
                    actual = checkKey?.GetValue(appName) as string;
                }

                if (!string.Equals(actual, command, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("启动项写入后校验失败，可能被安全软件拦截");
                }

                Log.Information($"已写入开机自启动项: {command}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"应用自启动设置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析当前程序的可执行文件路径
        /// </summary>
        /// <remarks>
        /// 单文件发布（PublishSingleFile）下 Assembly.Location 返回空字符串，
        /// 用它写启动项会得到一条空命令，任务管理器看不到、开机也不会执行。
        /// </remarks>
        private static string ResolveExecutablePath()
        {
            string processPath = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(processPath) &&
                !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            // 以 dotnet Layouter.dll 方式启动时，退回程序集位置
            string assemblyPath = Assembly.GetEntryAssembly()?.Location;

            if (!string.IsNullOrEmpty(assemblyPath))
            {
                return Path.ChangeExtension(assemblyPath, ".exe");
            }

            return string.Empty;
        }

        /// <summary>
        /// 初始化自启动设置
        /// </summary>
        /// <remarks>
        /// 每次启动都校正一次，顺带修复被手工删除、或指向旧路径的启动项。
        /// </remarks>
        public void InitializeAutoStart()
        {
            // 确保系统中的自启动设置与配置文件一致
            ApplyAutoStartSetting(settings.AutoStartEnabled);
        }

        /// <summary>
        /// 获取全局样式启用状态
        /// </summary>
        public bool GetEnableGlobalStyle()
        {
            return settings.EnableGlobalStyle;
        }

        /// <summary>
        /// 设置全局样式启用状态
        /// </summary>
        public void SetEnableGlobalStyle(bool enabled)
        {
            if (settings.EnableGlobalStyle != enabled)
            {
                settings.EnableGlobalStyle = enabled;
                SaveSettings();
            }
        }

        /// <summary>
        /// 获取分区窗口显示状态
        /// </summary>
        public bool GetPartitionVisibility(string partitionId)
        {
            if (settings.PartitionVisibility.TryGetValue(partitionId, out bool isVisible))
            {
                return isVisible;
            }

            return true;
        }

        /// <summary>
        /// 设置分区窗口显示状态
        /// </summary>
        public void SetPartitionVisibility(string partitionId, bool isVisible)
        {
            //新增项
            if (!settings.PartitionVisibility.ContainsKey(partitionId))
            {
                settings.PartitionVisibility.Add(partitionId, isVisible);
                SaveSettings();
                return;
            }

            bool oldValue = GetPartitionVisibility(partitionId);

            if (oldValue != isVisible)
            {
                settings.PartitionVisibility[partitionId] = isVisible;
                SaveSettings();

                PartitionVisibilityChanged?.Invoke(this, new PartitionVisibilityChangedEventArgs(partitionId, isVisible));
            }
        }

        /// <summary>
        /// 批量设置分区窗口显示状态
        /// </summary>
        public void SetPartitionVisibilityBatch(IEnumerable<string> partitionIds, bool isVisible)
        {
            bool hasChanges = false;

            foreach (var partitionId in partitionIds)
            {
                bool oldValue = GetPartitionVisibility(partitionId);

                if (oldValue != isVisible)
                {
                    settings.PartitionVisibility[partitionId] = isVisible;
                    hasChanges = true;

                    PartitionVisibilityChanged?.Invoke(this, new PartitionVisibilityChangedEventArgs(partitionId, isVisible));
                }
            }

            if (hasChanges)
            {
                SaveSettings();
            }
        }

        /// <summary>
        /// 获取所有分区窗口显示状态
        /// </summary>
        public Dictionary<string, bool> GetAllPartitionVisibility()
        {
            return new Dictionary<string, bool>(settings.PartitionVisibility);
        }

        /// <summary>
        /// 移除分区窗口的显示状态
        /// </summary>
        public void RemovePartitionVisibility(string partitionId)
        {
            if (settings.PartitionVisibility.ContainsKey(partitionId))
            {
                settings.PartitionVisibility.Remove(partitionId);
                SaveSettings();
            }
        }

        /// <summary>
        /// 获取配置备份的默认根目录
        /// </summary>
        public string GetBackupRootDirectory()
        {
            if (!string.IsNullOrWhiteSpace(settings.BackupRootDirectory))
            {
                return settings.BackupRootDirectory;
            }

            // 默认放在「文档」目录下，避免被系统清理或重装应用时一并删除
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LayouterBackups");
        }

        /// <summary>
        /// 设置配置备份的默认根目录
        /// </summary>
        public void SetBackupRootDirectory(string directory)
        {
            string value = directory?.Trim() ?? string.Empty;
            if (settings.BackupRootDirectory == value)
            {
                return;
            }

            settings.BackupRootDirectory = value;
            SaveSettings();
        }

        /// <summary>
        /// 获取隐藏存储目录
        /// </summary>
        public string GetHiddenStoreDirectory()
        {
            if (!string.IsNullOrWhiteSpace(settings.HiddenStoreDirectory))
            {
                return settings.HiddenStoreDirectory;
            }

            // 默认放在应用数据目录下，桌面将不再出现任何隐藏目录
            return Path.Combine(settingsFolder, "HiddenStore");
        }

        /// <summary>
        /// 设置隐藏存储目录
        /// </summary>
        public void SetHiddenStoreDirectory(string directory)
        {
            string value = directory?.Trim() ?? string.Empty;
            if (settings.HiddenStoreDirectory == value)
            {
                return;
            }

            settings.HiddenStoreDirectory = value;
            SaveSettings();
        }

        /// <summary>
        /// 获取「双击桌面隐藏图标」功能是否启用
        /// </summary>
        public bool GetEnableDesktopDoubleClickToggle()
        {
            return settings.EnableDesktopDoubleClickToggle;
        }

        /// <summary>
        /// 设置「双击桌面隐藏图标」功能是否启用
        /// </summary>
        public void SetEnableDesktopDoubleClickToggle(bool enabled)
        {
            if (settings.EnableDesktopDoubleClickToggle == enabled)
            {
                return;
            }

            settings.EnableDesktopDoubleClickToggle = enabled;
            SaveSettings();
        }

        /// <summary>
        /// 获取「全文件系统搜索」是否启用
        /// </summary>
        public bool GetEnableFullFileSystemSearch()
        {
            return settings.EnableFullFileSystemSearch;
        }

        /// <summary>
        /// 设置「全文件系统搜索」是否启用
        /// </summary>
        public void SetEnableFullFileSystemSearch(bool enabled)
        {
            if (settings.EnableFullFileSystemSearch == enabled)
            {
                return;
            }

            settings.EnableFullFileSystemSearch = enabled;
            SaveSettings();
        }

        /// <summary>
        /// 获取触发全盘文件搜索所需的最少字符数
        /// </summary>
        /// <remarks>
        /// 配置文件可能被手工改坏（0 或很大的值），这里统一钳到 1~5，
        /// 避免出现「永远搜不到」或「任何输入都不触发」的怪状态。
        /// </remarks>
        public int GetMinSearchLength()
        {
            return Math.Clamp(settings.MinSearchLength, MinSearchLengthLimit, MaxSearchLengthLimit);
        }

        /// <summary>
        /// 设置触发全盘文件搜索所需的最少字符数
        /// </summary>
        public void SetMinSearchLength(int length)
        {
            int value = Math.Clamp(length, MinSearchLengthLimit, MaxSearchLengthLimit);

            if (settings.MinSearchLength == value)
            {
                return;
            }

            settings.MinSearchLength = value;
            SaveSettings();
        }

        /// <summary>最少字符数的下限</summary>
        public const int MinSearchLengthLimit = 1;

        /// <summary>最少字符数的上限</summary>
        public const int MaxSearchLengthLimit = 5;

        /// <summary>
        /// 获取桌面图标当前是否处于隐藏状态
        /// </summary>
        public bool GetDesktopIconsHidden()
        {
            return settings.DesktopIconsHidden;
        }

        /// <summary>
        /// 记录桌面图标当前是否处于隐藏状态
        /// </summary>
        public void SetDesktopIconsHidden(bool hidden)
        {
            if (settings.DesktopIconsHidden == hidden)
            {
                return;
            }

            settings.DesktopIconsHidden = hidden;
            SaveSettings();
        }

        /// <summary>
        /// 获取全局搜索的快捷键描述
        /// </summary>
        public string GetSearchHotkey()
        {
            return settings.SearchHotkey ?? string.Empty;
        }

        /// <summary>
        /// 设置全局搜索的快捷键描述
        /// </summary>
        /// <param name="gesture">如 "Ctrl+Alt+S"，传空字符串表示取消快捷键</param>
        public void SetSearchHotkey(string gesture)
        {
            SetHotkey(HotkeyAction.Search, gesture);
        }

        /// <summary>
        /// 获取某个功能的快捷键描述
        /// </summary>
        /// <param name="action">功能</param>
        /// <returns>快捷键描述；未设置过时返回该功能的默认值</returns>
        public string GetHotkey(HotkeyAction action)
        {
            if (settings.Hotkeys == null)
            {
                settings.Hotkeys = new Dictionary<string, string>();
            }

            string key = action.ToString();

            if (settings.Hotkeys.TryGetValue(key, out string? gesture))
            {
                return gesture ?? string.Empty;
            }

            return HotkeyActionInfo.GetDefaultGesture(action);
        }

        /// <summary>
        /// 设置某个功能的快捷键描述
        /// </summary>
        /// <param name="action">功能</param>
        /// <param name="gesture">快捷键描述；传空字符串表示取消该功能的快捷键</param>
        public void SetHotkey(HotkeyAction action, string gesture)
        {
            if (settings.Hotkeys == null)
            {
                settings.Hotkeys = new Dictionary<string, string>();
            }

            string value = gesture?.Trim() ?? string.Empty;
            string key = action.ToString();

            if (settings.Hotkeys.TryGetValue(key, out string? old) && string.Equals(old, value, StringComparison.Ordinal))
            {
                return;
            }

            settings.Hotkeys[key] = value;

            // 搜索快捷键同时写回旧字段，兼容旧版本配置读取
            if (action == HotkeyAction.Search)
            {
                settings.SearchHotkey = value;
            }

            SaveSettings();
        }

        /// <summary>
        /// 获取全部功能的快捷键配置
        /// </summary>
        public Dictionary<HotkeyAction, string> GetAllHotkeys()
        {
            var result = new Dictionary<HotkeyAction, string>();

            foreach (HotkeyAction action in HotkeyActionInfo.All)
            {
                result[action] = GetHotkey(action);
            }

            return result;
        }
    }


}
