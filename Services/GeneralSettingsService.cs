using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    return JsonSerializer.Deserialize<GeneralSettings>(json) ?? new GeneralSettings();
                }
            }
            catch (Exception ex)
            {
                Log.Information($"加载通用设置失败: {ex.Message}");
            }

            return new GeneralSettings();
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
        public void SetAutoStartEnabled(bool enabled)
        {
            if (settings.AutoStartEnabled != enabled)
            {
                settings.AutoStartEnabled = enabled;
                SaveSettings();
                ApplyAutoStartSetting(enabled);
            }
        }

        /// <summary>
        /// 应用自启动设置到系统
        /// </summary>
        private void ApplyAutoStartSetting(bool enabled)
        {
            try
            {
                string appName = "Layouter";
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (enabled)
                {
                    // 获取应用程序路径
                    string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    // 确保是exe文件路径
                    if (appPath.EndsWith(".dll"))
                    {
                        appPath = appPath.Replace(".dll", ".exe");
                    }

                    // 添加到启动项
                    key?.SetValue(appName, appPath);
                }
                else
                {
                    // 从启动项中移除
                    key?.DeleteValue(appName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"应用自启动设置失败: {ex.Message}");
                AppMessageBox.Show($"设置开机自启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化自启动设置
        /// </summary>
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
    }


}
