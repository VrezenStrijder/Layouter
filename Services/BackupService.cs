using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Layouter.Models;
using Layouter.Utility;
using Layouter.ViewModels;

namespace Layouter.Services
{
    /// <summary>
    /// 配置备份服务：负责创建 / 列举 / 还原 / 删除配置备份
    /// </summary>
    public class BackupService
    {
        private static readonly Lazy<BackupService> instance = new Lazy<BackupService>(() => new BackupService());
        public static BackupService Instance => instance.Value;

        /// <summary>备份包内清单文件的条目名</summary>
        private const string ManifestEntryName = "manifest.json";

        /// <summary>备份包内配置数据的目录前缀</summary>
        private const string ConfigEntryPrefix = "config/";

        /// <summary>还原前自动创建的安全快照文件名前缀</summary>
        private const string SafetySnapshotPrefix = "pre-restore_";

        /// <summary>
        /// 判断指定备份文件是否为「还原前自动快照」
        /// </summary>
        public static bool IsSafetySnapshotFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                return Path.GetFileName(filePath).StartsWith(SafetySnapshotPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private BackupService()
        {
        }

        /// <summary>
        /// 应用程序的配置目录（%LOCALAPPDATA%\Layouter）
        /// </summary>
        public static string ConfigDirectory
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Env.AppName);
                }
                catch (Exception ex)
                {
                    Log.Information($"获取配置目录失败: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// 获取当前配置的备份根目录
        /// </summary>
        public string GetBackupRootDirectory()
        {
            return GeneralSettingsService.Instance.GetBackupRootDirectory();
        }

        /// <summary>
        /// 设置配置备份的根目录
        /// </summary>
        public void SetBackupRootDirectory(string directory)
        {
            GeneralSettingsService.Instance.SetBackupRootDirectory(directory);
        }

        /// <summary>
        /// 在资源管理器中打开备份根目录
        /// </summary>
        public void OpenBackupRootFolder()
        {
            string root = GetBackupRootDirectory();
            try
            {
                if (!Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }

                Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Information($"打开备份目录失败: {ex.Message}");
                throw new InvalidOperationException($"无法打开备份目录：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 创建一个配置备份
        /// </summary>
        /// <param name="name">用户指定的备份名称</param>
        /// <param name="remark">用户填写的备注</param>
        /// <returns>生成的备份文件完整路径</returns>
        public string CreateBackup(string name, string remark)
        {
            string root = GetBackupRootDirectory();
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                Log.Information($"创建备份目录失败: {ex.Message}");
                throw new InvalidOperationException($"无法创建备份目录：{ex.Message}", ex);
            }

            // 先把内存中的分区状态落盘，确保备份的是最新配置
            try
            {
                PartitionDataService.Instance.SaveAllPartitions();
            }
            catch (Exception ex)
            {
                Log.Information($"创建备份前保存分区数据失败: {ex.Message}");
            }

            DateTime now = DateTime.Now;
            string fileName = $"{now:yyyyMMdd-HHmmss}_{SanitizeFileName(name)}.zip";
            string zipPath = Path.Combine(root, fileName);

            WriteArchive(zipPath, name, remark, root);
            Log.Information($"已创建配置备份: {zipPath}");
            return zipPath;
        }

        /// <summary>
        /// 列出备份根目录下的所有备份（按创建时间倒序）
        /// </summary>
        public List<BackupInfo> GetBackups()
        {
            var result = new List<BackupInfo>();
            string root = GetBackupRootDirectory();

            string[] files;
            try
            {
                if (!Directory.Exists(root))
                {
                    return result;
                }

                files = Directory.GetFiles(root, "*.zip");
            }
            catch (Exception ex)
            {
                Log.Information($"读取备份目录失败: {ex.Message}");
                return result;
            }

            foreach (string file in files)
            {
                result.Add(ReadBackupInfo(file));
            }

            result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return result;
        }

        /// <summary>
        /// 删除指定的备份文件
        /// </summary>
        public bool DeleteBackup(BackupInfo backup)
        {
            if (backup == null || string.IsNullOrWhiteSpace(backup.FilePath))
            {
                return false;
            }

            try
            {
                if (File.Exists(backup.FilePath))
                {
                    File.Delete(backup.FilePath);
                }

                Log.Information($"已删除配置备份: {backup.FilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"删除备份失败({backup.FilePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用备份包覆盖当前配置。还原前会自动生成一份安全快照。
        /// </summary>
        public RestoreResult RestoreBackup(BackupInfo backup)
        {
            var result = new RestoreResult();

            if (backup == null || string.IsNullOrWhiteSpace(backup.FilePath))
            {
                result.ErrorMessage = "未选择备份。";
                return result;
            }

            if (!File.Exists(backup.FilePath))
            {
                result.ErrorMessage = "备份文件已不存在。";
                return result;
            }

            if (!backup.IsValid)
            {
                result.ErrorMessage = "该备份无效或已损坏，无法还原。";
                return result;
            }

            string root = GetBackupRootDirectory();
            string configDirectory = ConfigDirectory;

            try
            {
                if (!Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                // 1) 还原前自动创建安全快照
                //    从「还原前自动快照」还原时不再重复生成快照，
                //    否则每次还原安全快照都会再产生一个同名快照，备份列表会越积越多。
                if (!backup.IsSafetySnapshot)
                {
                    try
                    {
                        string snapshotPath = Path.Combine(root, $"{SafetySnapshotPrefix}{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                        WriteArchive(snapshotPath, "还原前自动快照", "还原前自动创建的安全快照", root);
                        result.SnapshotPath = snapshotPath;
                        Log.Information($"已创建还原前安全快照: {snapshotPath}");
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"创建还原前安全快照失败: {ex.Message}");
                        result.ErrorMessage = $"创建还原前安全快照失败：{ex.Message}";
                        return result;
                    }
                }
                else
                {
                    Log.Information("还原目标本身是安全快照，跳过再次生成快照");
                }

                // 2) 读取归档内容
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

                // 还原前的隐藏存储索引快照（读取归档内容之前抓取）
                HiddenStoreIndex? preRestoreIndex = null;

                using (FileStream stream = new FileStream(backup.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            // 目录条目
                            continue;
                        }

                        if (!entry.FullName.StartsWith(ConfigEntryPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string relative = entry.FullName.Substring(ConfigEntryPrefix.Length)
                            .Replace('/', Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar);

                        if (relative.Length == 0 || relative.Contains(".."))
                        {
                            // 防止路径穿越
                            continue;
                        }

                        entries[relative] = entry;
                    }

                    if (entries.Count == 0)
                    {
                        result.ErrorMessage = "备份包中不包含配置数据。";
                        return result;
                    }

                    // 还原前的隐藏存储索引快照。
                    // 备份之后再隐藏的图标不在备份包里，若直接用备份的索引覆盖，
                    // 这些图标就会失去「原始路径 ↔ 存放路径」的映射，既无法回到桌面，
                    // 也无法在分区中正常显示，因此还原时必须与备份索引合并。
                    preRestoreIndex = SecureHiddenStore.Instance.GetIndexSnapshot();

                    // 3) 覆盖写入配置数据
                    foreach (KeyValuePair<string, ZipArchiveEntry> pair in entries)
                    {
                        string destination = Path.GetFullPath(Path.Combine(configDirectory, pair.Key));

                        if (!IsUnder(configDirectory, destination))
                        {
                            continue;
                        }

                        if (IsUnder(root, destination))
                        {
                            // 备份目录本身永远不被还原流程覆盖
                            continue;
                        }

                        string? parent = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        using (Stream source = pair.Value.Open())
                        {
                            // 隐藏存储中的文件可能带有「系统 / 只读」属性，
                            // Windows 会因此拒绝以写入方式打开，必须先解除
                            SecureHiddenStore.ClearProtectedAttributes(destination);

                            using (FileStream target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                source.CopyTo(target);
                            }
                        }

                        // 还原回隐藏存储的文件继续保持隐藏状态
                        if (IsHiddenStoreRelative(pair.Key))
                        {
                            TryHideRestoredFile(destination);
                        }
                    }
                }

                // 4) 删除归档中不存在的配置文件
                foreach (string file in EnumerateFilesSafe(configDirectory))
                {
                    if (IsUnder(root, file))
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(configDirectory, file);
                    if (entries.ContainsKey(relative))
                    {
                        continue;
                    }

                    // 隐藏存储目录整体保留：备份之后再隐藏的图标不能被旧备份删除，
                    // 否则这些图标既不在还原后的分区里、也回不到桌面，等于凭空消失
                    if (IsHiddenStoreRelative(relative))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        Log.Information($"还原时删除多余配置文件: {relative}");
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"还原时删除文件失败({relative}): {ex.Message}");
                    }
                }

                // 5) 清理空目录（备份目录除外）
                foreach (string directory in EnumerateDirectoriesSafe(configDirectory))
                {
                    if (IsUnder(root, directory))
                    {
                        continue;
                    }

                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"还原时清理空目录失败({directory}): {ex.Message}");
                    }
                }

                // 6) 合并隐藏存储索引（备份之后再隐藏的图标保留映射）
                MergeHiddenStoreIndex(preRestoreIndex);

                // 7) 重新加载隐藏存储索引（索引文件已随备份一起还原）
                SecureHiddenStore.Instance.ReloadIndex();

                // 8) 还原后不再被任何分区引用的隐藏图标，统统恢复到桌面，避免文件被永久藏起来
                result.RestoredToDesktopCount = RestoreOrphanedIcons();

                // 9) 让运行中的分区窗口与还原后的配置保持一致（消除残留的空壳图标）
                result.RemovedInvalidIconCount = ReloadPartitionWindows();

                result.Success = true;
                Log.Information($"已从备份还原配置: {backup.FilePath}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Information($"还原备份失败: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 把还原回隐藏存储的文件重新标记为隐藏
        /// </summary>
        private static void TryHideRestoredFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                FileAttributes attributes = File.GetAttributes(path);
                File.SetAttributes(path, attributes | FileAttributes.Hidden);
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>
        /// 判断相对路径是否位于隐藏存储目录之内
        /// </summary>
        private static bool IsHiddenStoreRelative(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            string storeFolder = Env.HiddenStoreFolderName + Path.DirectorySeparatorChar;

            return normalized.StartsWith(storeFolder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 合并隐藏存储索引
        /// </summary>
        /// <remarks>
        /// 还原时备份包中的索引会覆盖磁盘上的索引，但备份之后再隐藏的图标不在备份包里，
        /// 直接覆盖会让它们失去「原始路径 ↔ 存放路径」的映射，进而无法回到桌面。
        /// 这里把「还原前索引中、且存放文件仍然存在」的记录补回索引，
        /// 使这些图标在还原后能够被识别为孤立图标并自动回到桌面。
        /// </remarks>
        private void MergeHiddenStoreIndex(HiddenStoreIndex? preRestoreIndex)
        {
            try
            {
                var store = SecureHiddenStore.Instance;
                string indexPath = store.FallbackIndexPath;

                var merged = new HiddenStoreIndex
                {
                    Version = preRestoreIndex?.Version ?? 1
                };

                // 备份包中的索引（此时已随还原写入磁盘）
                if (File.Exists(indexPath))
                {
                    try
                    {
                        string json = File.ReadAllText(indexPath);
                        var loaded = JsonSerializer.Deserialize<HiddenStoreIndex>(json);
                        if (loaded?.Entries != null)
                        {
                            merged.Entries.AddRange(loaded.Entries);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"读取还原后的隐藏存储索引失败: {ex.Message}");
                    }
                }

                if (preRestoreIndex?.Entries != null)
                {
                    foreach (HiddenStoreEntry entry in preRestoreIndex.Entries)
                    {
                        if (merged.Entries.Any(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string storedPath = Path.Combine(store.StoreRoot, entry.StoredName);
                        if (!File.Exists(storedPath) && !Directory.Exists(storedPath))
                        {
                            // 存放文件已经不存在，无需保留映射
                            continue;
                        }

                        merged.Entries.Add(entry);
                    }
                }

                store.ReplaceIndex(merged);
                Log.Information($"已合并隐藏存储索引，共 {merged.Entries.Count} 条记录");
            }
            catch (Exception ex)
            {
                Log.Information($"合并隐藏存储索引失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 收集还原后各分区仍在引用的图标原始路径
        /// </summary>
        private static HashSet<string> CollectReferencedIconPaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string configDirectory = ConfigDirectory;

            if (string.IsNullOrEmpty(configDirectory) || !Directory.Exists(configDirectory))
            {
                return result;
            }

            var store = SecureHiddenStore.Instance;

            foreach (string file in EnumerateFilesSafe(configDirectory))
            {
                if (!Path.GetFileName(file).StartsWith("partition_", StringComparison.OrdinalIgnoreCase)
                    || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(file);
                    var data = JsonSerializer.Deserialize<PartitionDataDto>(json);
                    if (data?.Icons == null)
                    {
                        continue;
                    }

                    foreach (var icon in data.Icons)
                    {
                        if (string.IsNullOrEmpty(icon.IconPath))
                        {
                            continue;
                        }

                        // 统一解析成原始路径再比较，兼容旧数据中直接保存存放路径的情况
                        result.Add(store.ResolveOriginalPath(icon.IconPath));
                    }
                }
                catch (Exception ex)
                {
                    Log.Information($"读取分区配置失败({file}): {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 把还原后不再被任何分区引用的隐藏图标恢复到桌面
        /// </summary>
        /// <returns>恢复到桌面的图标数量</returns>
        private int RestoreOrphanedIcons()
        {
            try
            {
                var store = SecureHiddenStore.Instance;
                var referenced = CollectReferencedIconPaths();
                int count = 0;

                foreach (var entry in store.GetIndexSnapshot().Entries)
                {
                    if (string.IsNullOrEmpty(entry.OriginalPath))
                    {
                        continue;
                    }

                    if (referenced.Contains(entry.OriginalPath))
                    {
                        // 仍然被某个分区引用，保持隐藏
                        continue;
                    }

                    string storedPath = Path.Combine(store.StoreRoot, entry.StoredName);
                    if (store.Restore(storedPath, out string restoredPath))
                    {
                        count++;
                        Log.Information($"还原后已把不再被引用的图标恢复到桌面: {restoredPath}");
                    }
                }

                if (count > 0)
                {
                    DesktopIconService.Instance.RefreshDesktop();
                }

                return count;
            }
            catch (Exception ex)
            {
                Log.Information($"恢复孤立图标失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 让运行中的分区窗口与还原后的配置保持一致
        /// </summary>
        /// <returns>被清理的无效图标数量</returns>
        private int ReloadPartitionWindows()
        {
            int removed = 0;

            try
            {
                var windows = WindowManagerService.Instance.GetAllWindows();
                var metadata = PartitionDataService.Instance.GetMetadata();
                var restoredIds = new HashSet<string>(
                    metadata?.WindowIds ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                // 1) 关闭还原后已不存在的分区（关闭时不写回旧数据）
                foreach (var window in windows.ToList())
                {
                    string windowId = window.WindowId ?? string.Empty;
                    if (!string.IsNullOrEmpty(windowId) && restoredIds.Contains(windowId))
                    {
                        continue;
                    }

                    PartitionDataService.Instance.RemoveWindowMapping(window);
                    WindowManagerService.Instance.UnregisterWindow(window);
                    window.CloseWithoutSaving();
                    Log.Information($"还原后关闭了已不存在的分区: {windowId}");
                }

                // 2) 重新加载仍然存在的分区
                foreach (var window in WindowManagerService.Instance.GetAllWindows().ToList())
                {
                    if (string.IsNullOrEmpty(window.WindowId))
                    {
                        continue;
                    }

                    int before = (window.DataContext as DesktopManagerViewModel)?.Icons.Count ?? 0;
                    PartitionDataService.Instance.LoadPartitionData(window, window.WindowId);
                    int after = (window.DataContext as DesktopManagerViewModel)?.Icons.Count ?? 0;
                    if (before > after)
                    {
                        removed += before - after;
                    }
                }

                // 3) 创建还原后新增的分区
                foreach (string windowId in restoredIds)
                {
                    if (WindowManagerService.Instance.GetWindowById(windowId) == null)
                    {
                        PartitionDataService.Instance.ShowWindow(windowId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"还原后重载分区窗口失败: {ex.Message}");
            }

            return removed;
        }

        /// <summary>
        /// 写入一个备份包（manifest.json + config/...）
        /// </summary>
        private void WriteArchive(string zipPath, string name, string remark, string root)
        {
            List<string> files = CollectConfigFiles(root);
            if (files.Count == 0)
            {
                throw new InvalidOperationException("当前没有可备份的配置数据。");
            }

            var manifest = new BackupManifest
            {
                Id = Guid.NewGuid().ToString(),
                Name = string.IsNullOrWhiteSpace(name) ? "未命名备份" : name.Trim(),
                Remark = remark?.Trim() ?? string.Empty,
                CreatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AppVersion = GetAppVersion(),
                FileCount = files.Count,
                MachineId = GetMachineId()
            };

            string configDirectory = ConfigDirectory;

            // 先剔除已经不存在的文件，保证清单中的数量与实际写入一致
            var existing = new List<string>();
            foreach (string relative in files)
            {
                if (File.Exists(Path.Combine(configDirectory, relative)))
                {
                    existing.Add(relative);
                }
            }

            manifest.FileCount = existing.Count;

            using (FileStream stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                using (Stream manifestStream = manifestEntry.Open())
                using (var writer = new StreamWriter(manifestStream, new UTF8Encoding(false)))
                {
                    writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
                }

                foreach (string relative in existing)
                {
                    string source = Path.Combine(configDirectory, relative);
                    try
                    {
                        archive.CreateEntryFromFile(source, ConfigEntryPrefix + relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"备份文件失败({relative}): {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 收集配置目录下的所有配置文件（相对路径，统一使用 '/' 分隔）
        /// </summary>
        private List<string> CollectConfigFiles(string root)
        {
            var result = new List<string>();
            string configDirectory = ConfigDirectory;

            if (string.IsNullOrEmpty(configDirectory) || !Directory.Exists(configDirectory))
            {
                return result;
            }

            foreach (string file in EnumerateFilesSafe(configDirectory))
            {
                if (IsUnder(root, file))
                {
                    // 备份目录自身不参与备份
                    continue;
                }

                string relative = Path.GetRelativePath(configDirectory, file);
                if (relative.StartsWith("..", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// 读取单个备份文件的信息（损坏的备份不会抛出异常）
        /// </summary>
        private BackupInfo ReadBackupInfo(string filePath)
        {
            var info = new BackupInfo
            {
                FilePath = filePath,
                Name = Path.GetFileName(filePath),
                IsSafetySnapshot = IsSafetySnapshotFile(filePath)
            };

            try
            {
                var file = new FileInfo(filePath);
                info.FileSize = file.Length;
                info.CreatedAt = file.LastWriteTime;

                BackupManifest? manifest = null;
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryName);
                    if (entry == null)
                    {
                        throw new InvalidDataException("备份包中缺少 manifest.json");
                    }

                    using (Stream manifestStream = entry.Open())
                    using (var reader = new StreamReader(manifestStream))
                    {
                        string json = reader.ReadToEnd();
                        manifest = JsonSerializer.Deserialize<BackupManifest>(json);
                    }
                }

                if (manifest == null)
                {
                    throw new InvalidDataException("manifest.json 内容无效");
                }

                info.Id = manifest.Id ?? string.Empty;
                info.Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(filePath) : manifest.Name;
                info.Remark = manifest.Remark ?? string.Empty;
                info.AppVersion = manifest.AppVersion ?? string.Empty;
                info.FileCount = manifest.FileCount;
                info.MachineId = manifest.MachineId ?? string.Empty;

                // 清单中的 CreatedAt 为 ISO-8601 UTC 字符串，RoundtripKind 会保留 Utc 标记，
                // 再转换为本地时间即可。切勿与 AdjustToUniversal 组合使用（二者互斥，会抛 ArgumentException）。
                if (DateTime.TryParse(manifest.CreatedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime createdUtc))
                {
                    info.CreatedAt = createdUtc.Kind == DateTimeKind.Utc
                        ? createdUtc.ToLocalTime()
                        : DateTime.SpecifyKind(createdUtc, DateTimeKind.Local);
                }

                info.IsValid = true;
                info.IsFromThisMachine = !string.IsNullOrEmpty(info.MachineId)
                                         && string.Equals(info.MachineId, GetMachineId(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Information($"读取备份信息失败({Path.GetFileName(filePath)}): {ex.Message}");
                info.IsValid = false;
                info.IsFromThisMachine = false;
                info.Name = Path.GetFileName(filePath);
                info.Remark = "无法读取备份内容";
            }

            return info;
        }

        /// <summary>
        /// 递归列出目录下的所有文件
        /// </summary>
        private static List<string> EnumerateFilesSafe(string root)
        {
            var result = new List<string>();
            CollectFiles(root, result);
            return result;
        }

        private static void CollectFiles(string directory, List<string> result)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex)
            {
                Log.Information($"读取目录失败({directory}): {ex.Message}");
                return;
            }

            result.AddRange(files);

            // 只递归直接子目录，否则嵌套目录中的文件会被重复收集
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex)
            {
                Log.Information($"读取子目录失败({directory}): {ex.Message}");
                return;
            }

            foreach (string child in children)
            {
                CollectFiles(child, result);
            }
        }

        /// <summary>
        /// 递归列出目录下的所有子目录（由浅到深）
        /// </summary>
        private static List<string> EnumerateDirectoriesSafe(string root)
        {
            var result = new List<string>();

            try
            {
                foreach (string directory in Directory.GetDirectories(root))
                {
                    result.Add(directory);
                    result.AddRange(EnumerateDirectoriesSafe(directory));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"读取目录失败({root}): {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 判断路径是否位于指定目录之内（含目录自身）
        /// </summary>
        private static bool IsUnder(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string normalizedRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedPath = Path.GetFullPath(path);

                if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 去除文件名中的非法字符
        /// </summary>
        private static string SanitizeFileName(string? name)
        {
            string value = (name ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                value = "备份";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(invalid.Contains(c) ? '_' : c);
            }

            string sanitized = builder.ToString().TrimEnd('.', ' ');
            if (sanitized.Length == 0)
            {
                sanitized = "备份";
            }

            if (sanitized.Length > 60)
            {
                sanitized = sanitized.Substring(0, 60);
            }

            return sanitized;
        }

        /// <summary>
        /// 获取当前应用程序版本
        /// </summary>
        private static string GetAppVersion()
        {
            try
            {
                Version? version = Assembly.GetExecutingAssembly().GetName().Version;
                return version?.ToString() ?? "0.0.0.0";
            }
            catch (Exception)
            {
                return "0.0.0.0";
            }
        }

        /// <summary>
        /// 获取本机标识（机器名 + 用户名的哈希，不可反推）
        /// </summary>
        private static string GetMachineId()
        {
            try
            {
                string raw = $"{Environment.MachineName}|{Environment.UserName}";
                using SHA256 sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Log.Information($"计算机器标识失败: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
