using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Layouter.Services
{
    /// <summary>
    /// 隐藏存储中的一条记录
    /// </summary>
    public class HiddenStoreEntry
    {
        /// <summary>
        /// 记录标识（原始路径的哈希）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 隐藏前的原始绝对路径
        /// </summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>
        /// 相对于存储根目录的存放名称
        /// </summary>
        public string StoredName { get; set; } = string.Empty;

        /// <summary>
        /// 原始显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 是否为目录
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// 隐藏时间
        /// </summary>
        public string CreatedAt { get; set; } = string.Empty;

        /// <summary>
        /// 占用字节数
        /// </summary>
        public long Size { get; set; }
    }

    /// <summary>
    /// 隐藏索引文件结构
    /// </summary>
    public class HiddenStoreIndex
    {
        public int Version { get; set; } = 1;
        public List<HiddenStoreEntry> Entries { get; set; } = new List<HiddenStoreEntry>();
    }

    /// <summary>
    /// 安全隐藏存储。
    /// <para>
    /// 早期版本把隐藏的桌面图标存放在桌面下的 <c>.layouterhidden</c> 目录中，
    /// 仅依靠「隐藏 / 系统」属性来隐藏目录；一旦用户在资源管理器中开启
    /// 「显示隐藏文件」，该目录就会暴露出来。
    /// </para>
    /// <para>
    /// 本类改为把隐藏实体的存储整体迁移到应用数据目录
    /// （<c>%LOCALAPPDATA%\Layouter\HiddenStore</c>），桌面命名空间中不再存在任何隐藏目录，
    /// 因此无论资源管理器如何设置隐藏文件选项都看不到它。在此基础上进一步做了三层加固：
    /// </para>
    /// <list type="number">
    /// <item>存放名称使用原始路径的 SHA-256 摘要（保留扩展名），目录列表中不暴露任何原始文件名；</item>
    /// <item>「原始路径 ↔ 存放路径」的索引写入宿主文件的 NTFS 备用数据流（ADS），资源管理器默认不显示数据流；</item>
    /// <item>存储目录本身附加「隐藏 + 系统」属性，并在卷不支持 ADS 时自动降级为普通索引文件。</item>
    /// </list>
    /// </summary>
    public class SecureHiddenStore
    {
        private static readonly Lazy<SecureHiddenStore> instance = new Lazy<SecureHiddenStore>(() => new SecureHiddenStore());

        /// <summary>
        /// 单例实例
        /// </summary>
        public static SecureHiddenStore Instance => instance.Value;

        private readonly object syncRoot = new object();
        private HiddenStoreIndex index = new HiddenStoreIndex();
        private bool initialized;
        private bool adsAvailable;

        private SecureHiddenStore()
        {
        }

        /// <summary>
        /// 存储根目录
        /// </summary>
        public string StoreRoot => GeneralSettingsService.Instance.GetHiddenStoreDirectory();

        /// <summary>
        /// 存放隐藏实体的宿主文件（其上挂载 ADS 索引）
        /// </summary>
        public string HostFilePath => Path.Combine(StoreRoot, Env.HiddenStoreHostFileName);

        /// <summary>
        /// ADS 索引数据流名称
        /// </summary>
        public string IndexStreamName => Env.HiddenStoreIndexStreamName;

        /// <summary>
        /// 降级使用的普通索引文件路径
        /// </summary>
        public string FallbackIndexPath => Path.Combine(StoreRoot, Env.HiddenStoreIndexFileName);

        /// <summary>
        /// 当前卷是否支持备用数据流
        /// </summary>
        public bool AdsAvailable
        {
            get
            {
                EnsureInitialized();
                return adsAvailable;
            }
        }

        /// <summary>
        /// 存储中的记录数量
        /// </summary>
        public int EntryCount
        {
            get
            {
                EnsureInitialized();
                return index.Entries.Count;
            }
        }

        /// <summary>
        /// 初始化存储目录并执行旧版隐藏目录迁移
        /// </summary>
        public void EnsureInitialized()
        {
            lock (syncRoot)
            {
                if (initialized)
                {
                    return;
                }

                initialized = true;

                try
                {
                    EnsureStoreDirectory();
                    adsAvailable = AlternateDataStream.IsSupportedInDirectory(StoreRoot);
                    LoadIndex();
                    MigrateLegacyStore();
                    TryRecoverOrphanEntries();
                }
                catch (Exception ex)
                {
                    Log.Information($"初始化安全隐藏存储失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 确保存储目录存在并应用隐藏属性
        /// </summary>
        private void EnsureStoreDirectory()
        {
            try
            {
                if (!Directory.Exists(StoreRoot))
                {
                    Directory.CreateDirectory(StoreRoot);
                }

                ApplyHiddenAttributes(StoreRoot, isDirectory: true);
            }
            catch (Exception ex)
            {
                Log.Information($"创建隐藏存储目录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 为路径附加「隐藏」属性（目录额外附加「系统」属性）
        /// </summary>
        /// <remarks>
        /// 注意：Windows 会对带「系统」属性的文件拒绝以写入方式打开，
        /// 这会让备份还原时无法覆盖隐藏存储中的文件（Access denied）。
        /// 因此「系统」属性只用于目录，文件一律只设置「隐藏」。
        /// </remarks>
        private static void ApplyHiddenAttributes(string path, bool isDirectory)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return;
                }

                FileAttributes attributes = File.GetAttributes(path);
                attributes |= FileAttributes.Hidden;

                if (isDirectory)
                {
                    attributes |= FileAttributes.System;
                }
                else
                {
                    // 解除历史遗留的「系统」属性，否则该文件无法被覆盖写入
                    attributes &= ~FileAttributes.System;
                    attributes &= ~FileAttributes.ReadOnly;
                }

                File.SetAttributes(path, attributes);
            }
            catch
            {
                // 属性设置失败不影响主流程
            }
        }

        /// <summary>
        /// 解除「隐藏 / 系统 / 只读」属性，便于覆盖写入或删除
        /// </summary>
        internal static void ClearProtectedAttributes(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return;
                }

                FileAttributes attributes = File.GetAttributes(path);
                FileAttributes masked = attributes
                    & ~(FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);

                if (masked != attributes)
                {
                    File.SetAttributes(path, masked);
                }
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>
        /// 安全地覆盖写入文件
        /// </summary>
        /// <remarks>
        /// Windows 会对带「隐藏 / 系统」属性的文件拒绝以 <see cref="FileMode.Create"/> 打开，
        /// 因此写入前必须先解除这些属性，否则索引无法更新、备份也无法还原。
        /// </remarks>
        internal static void WriteAllBytesSafe(string path, byte[] payload)
        {
            ClearProtectedAttributes(path);
            File.WriteAllBytes(path, payload);
        }

        #region 索引读写

        /// <summary>
        /// 读取索引（以普通索引文件为主，ADS 作为兼容旧版本的备用来源）
        /// </summary>
        /// <remarks>
        /// 索引必须以「普通文件」形式保存：ADS 无法被目录枚举，
        /// 备份时不会被打包、还原时也会被空文件覆盖导致索引丢失，
        /// 分区图标会因此变成无法使用的空壳。ADS 仅作为镜像保留一份。
        /// </remarks>
        private void LoadIndex()
        {
            index = new HiddenStoreIndex();

            try
            {
                byte[] payload = null;

                if (File.Exists(FallbackIndexPath))
                {
                    payload = File.ReadAllBytes(FallbackIndexPath);
                }

                if ((payload == null || payload.Length == 0) && adsAvailable)
                {
                    payload = AlternateDataStream.TryRead(HostFilePath, IndexStreamName);
                }

                if (payload == null || payload.Length == 0)
                {
                    return;
                }

                string json = Encoding.UTF8.GetString(payload);
                var loaded = JsonSerializer.Deserialize<HiddenStoreIndex>(json);
                if (loaded?.Entries != null)
                {
                    index = loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"读取隐藏存储索引失败: {ex.Message}");
                index = new HiddenStoreIndex();
            }
        }

        /// <summary>
        /// 保存索引
        /// </summary>
        /// <remarks>
        /// 索引主体写入普通文件（可被备份 / 还原），同时尽力写一份 ADS 镜像。
        /// </remarks>
        private void SaveIndex()
        {
            try
            {
                EnsureStoreDirectory();

                byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));

                // 索引主体：普通文件，保证能被备份与还原
                WriteAllBytesSafe(FallbackIndexPath, payload);

                // ADS 镜像：仅作冗余备份，失败不影响主流程
                if (adsAvailable)
                {
                    AlternateDataStream.TryWrite(HostFilePath, IndexStreamName, payload);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"保存隐藏存储索引失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从磁盘重新加载索引（还原备份后调用，使索引与还原后的存储目录保持一致）
        /// </summary>
        public void ReloadIndex()
        {
            lock (syncRoot)
            {
                initialized = false;
                EnsureInitialized();
                Log.Information($"已重新加载隐藏存储索引，记录数: {index.Entries.Count}");
            }
        }

        /// <summary>
        /// 用新的索引覆盖当前索引并立即保存
        /// </summary>
        /// <remarks>
        /// 还原备份时需要把「备份中的索引」与「还原前索引中仍然有效的记录」合并，
        /// 合并结果通过本方法写回磁盘，再调用 <see cref="ReloadIndex"/> 重新载入。
        /// </remarks>
        public void ReplaceIndex(HiddenStoreIndex newIndex)
        {
            lock (syncRoot)
            {
                EnsureInitialized();

                index = newIndex ?? new HiddenStoreIndex();
                SaveIndex();
            }
        }

        /// <summary>
        /// 获取当前索引的快照（供备份 / 诊断使用）
        /// </summary>
        public HiddenStoreIndex GetIndexSnapshot()
        {
            EnsureInitialized();
            lock (syncRoot)
            {
                return new HiddenStoreIndex
                {
                    Version = index.Version,
                    Entries = index.Entries.Select(e => new HiddenStoreEntry
                    {
                        Id = e.Id,
                        OriginalPath = e.OriginalPath,
                        StoredName = e.StoredName,
                        DisplayName = e.DisplayName,
                        IsDirectory = e.IsDirectory,
                        CreatedAt = e.CreatedAt,
                        Size = e.Size
                    }).ToList()
                };
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 忽略
            }
        }

        #endregion

        #region 路径计算

        /// <summary>
        /// 计算原始路径对应的记录标识
        /// </summary>
        public static string ComputeId(string originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return string.Empty;
            }

            string normalized = NormalizePath(originalPath);

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < 12; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// 规范化路径（统一大小写与分隔符，保证同一路径得到同一标识）
        /// </summary>
        private static string NormalizePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            }
            catch
            {
                return path.ToUpperInvariant();
            }
        }

        /// <summary>
        /// 计算存放名称（哈希 + 保留扩展名，保证图标解析正常）
        /// </summary>
        public string GetStoredName(string originalPath, bool isDirectory)
        {
            string id = ComputeId(originalPath);
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            if (isDirectory)
            {
                return id;
            }

            string extension;
            try
            {
                extension = Path.GetExtension(originalPath) ?? string.Empty;
            }
            catch
            {
                extension = string.Empty;
            }

            return id + extension;
        }

        /// <summary>
        /// 计算存放路径
        /// </summary>
        public string GetStoredPath(string originalPath, bool isDirectory)
        {
            string storedName = GetStoredName(originalPath, isDirectory);
            if (string.IsNullOrEmpty(storedName))
            {
                return string.Empty;
            }

            return Path.Combine(StoreRoot, storedName);
        }

        /// <summary>
        /// 判断路径是否位于存储目录内
        /// </summary>
        public bool IsStorePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string root = Path.GetFullPath(StoreRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(path);
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 查询

        /// <summary>
        /// 按原始路径查找记录
        /// </summary>
        public bool TryGetEntryByOriginalPath(string originalPath, out HiddenStoreEntry entry)
        {
            entry = null;
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return false;
            }

            string normalized = NormalizePath(originalPath);
            entry = index.Entries.FirstOrDefault(e =>
                string.Equals(NormalizePath(e.OriginalPath), normalized, StringComparison.OrdinalIgnoreCase));
            return entry != null;
        }

        /// <summary>
        /// 按存放路径查找记录
        /// </summary>
        public bool TryGetEntryByStoredPath(string storedPath, out HiddenStoreEntry entry)
        {
            entry = null;
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return false;
            }

            string storedName;
            try
            {
                storedName = Path.GetFileName(storedPath);
            }
            catch
            {
                return false;
            }

            entry = index.Entries.FirstOrDefault(e =>
                string.Equals(e.StoredName, storedName, StringComparison.OrdinalIgnoreCase));
            return entry != null;
        }

        /// <summary>
        /// 解析存储路径对应的原始路径（非存储路径时原样返回）
        /// </summary>
        public string ResolveOriginalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            EnsureInitialized();

            if (TryGetEntryByStoredPath(path, out HiddenStoreEntry entry))
            {
                return entry.OriginalPath;
            }

            return path;
        }

        /// <summary>
        /// 解析原始路径对应的存放路径（未隐藏时返回原路径）
        /// </summary>
        public string ResolveStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            EnsureInitialized();

            if (TryGetEntryByOriginalPath(path, out HiddenStoreEntry entry))
            {
                return Path.Combine(StoreRoot, entry.StoredName);
            }

            return path;
        }

        /// <summary>
        /// 判断指定路径对应的内容是否已处于隐藏状态
        /// </summary>
        public bool IsHidden(string originalPath)
        {
            EnsureInitialized();

            if (!TryGetEntryByOriginalPath(originalPath, out HiddenStoreEntry entry))
            {
                return false;
            }

            string storedPath = Path.Combine(StoreRoot, entry.StoredName);
            return File.Exists(storedPath) || Directory.Exists(storedPath);
        }

        #endregion

        #region 隐藏与还原

        /// <summary>
        /// 将文件或目录移入安全存储
        /// </summary>
        /// <param name="originalPath">原始路径</param>
        /// <param name="storedPath">存放路径</param>
        /// <returns>是否成功</returns>
        public bool Hide(string originalPath, out string storedPath)
        {
            storedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return false;
            }

            EnsureInitialized();

            try
            {
                bool isFile = File.Exists(originalPath);
                bool isDirectory = Directory.Exists(originalPath);

                if (!isFile && !isDirectory)
                {
                    return false;
                }

                string storedName = GetStoredName(originalPath, isDirectory);
                if (string.IsNullOrEmpty(storedName))
                {
                    return false;
                }

                string target = Path.Combine(StoreRoot, storedName);

                // 已经处于隐藏状态
                if (TryGetEntryByOriginalPath(originalPath, out HiddenStoreEntry existing))
                {
                    storedPath = Path.Combine(StoreRoot, existing.StoredName);
                    if (File.Exists(storedPath) || Directory.Exists(storedPath))
                    {
                        return true;
                    }
                }

                // 目标位置残留时先清理
                if (Directory.Exists(target))
                {
                    ClearProtectedAttributes(target);
                    Directory.Delete(target, true);
                }
                else if (File.Exists(target))
                {
                    ClearProtectedAttributes(target);
                    File.Delete(target);
                }

                EnsureStoreDirectory();

                if (isDirectory)
                {
                    MoveDirectory(originalPath, target);
                }
                else
                {
                    MoveFile(originalPath, target);
                }

                ApplyHiddenAttributes(target, isDirectory);

                var entry = new HiddenStoreEntry
                {
                    Id = ComputeId(originalPath),
                    OriginalPath = originalPath,
                    StoredName = storedName,
                    DisplayName = isDirectory
                        ? new DirectoryInfo(originalPath).Name
                        : Path.GetFileName(originalPath),
                    IsDirectory = isDirectory,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Size = isDirectory ? GetDirectorySize(target) : GetFileSize(target)
                };

                index.Entries.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                index.Entries.Add(entry);
                SaveIndex();

                storedPath = target;
                Log.Information($"已隐藏: {originalPath} -> {storedName}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"隐藏文件失败({originalPath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将内容从安全存储还原到原始路径
        /// </summary>
        /// <param name="path">原始路径或存放路径</param>
        /// <param name="restoredPath">还原后的路径</param>
        /// <returns>是否成功</returns>
        public bool Restore(string path, out string restoredPath)
        {
            restoredPath = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            EnsureInitialized();

            try
            {
                HiddenStoreEntry entry = null;

                if (IsStorePath(path))
                {
                    TryGetEntryByStoredPath(path, out entry);
                }
                else
                {
                    TryGetEntryByOriginalPath(path, out entry);
                }

                if (entry == null)
                {
                    return false;
                }

                string storedPath = Path.Combine(StoreRoot, entry.StoredName);
                if (!File.Exists(storedPath) && !Directory.Exists(storedPath))
                {
                    Log.Information($"隐藏存储中不存在内容: {entry.StoredName}");
                    return false;
                }

                string target = entry.OriginalPath;

                // 原始位置已被占用时不覆盖
                if (File.Exists(target) || Directory.Exists(target))
                {
                    Log.Information($"还原目标已存在，跳过: {target}");
                    return false;
                }

                string parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                if (entry.IsDirectory)
                {
                    MoveDirectory(storedPath, target);
                }
                else
                {
                    MoveFile(storedPath, target);
                }

                // 还原后清除隐藏属性
                ClearHiddenAttributes(target, entry.IsDirectory);

                index.Entries.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                SaveIndex();

                restoredPath = target;
                Log.Information($"已还原: {entry.StoredName} -> {target}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"还原文件失败({path}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移动文件，跨卷时退化为复制 + 删除
        /// </summary>
        private static void MoveFile(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
            }
            catch (IOException)
            {
                File.Copy(source, destination, true);
                File.Delete(source);
            }
        }

        /// <summary>
        /// 移动目录，跨卷时退化为递归复制 + 删除
        /// </summary>
        private static void MoveDirectory(string source, string destination)
        {
            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        /// <summary>
        /// 清除隐藏 / 系统属性
        /// </summary>
        private static void ClearHiddenAttributes(string path, bool isDirectory)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return;
                }

                FileAttributes attributes = File.GetAttributes(path);
                attributes &= ~FileAttributes.Hidden;
                attributes &= ~FileAttributes.System;

                if (isDirectory)
                {
                    attributes |= FileAttributes.Directory;
                }

                File.SetAttributes(path, attributes);
            }
            catch
            {
                // 忽略
            }
        }

        private static long GetFileSize(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                long total = 0;
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    total += GetFileSize(file);
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region 孤立条目恢复

        /// <summary>
        /// 为隐藏存储中「没有索引记录」的孤立文件重新建立映射
        /// </summary>
        /// <remarks>
        /// 早期版本把索引只写在宿主文件的备用数据流里，备份 / 还原一旦覆盖宿主文件，
        /// 索引就会彻底丢失，隐藏存储中的文件从此既不知道原始路径，也无法回到桌面，
        /// 分区里只会留下一个打不开的「默认图标」。
        /// <para>
        /// 存放名称是原始路径哈希的前缀，因此可以反过来在桌面上重新匹配：
        /// 对桌面上每个条目计算同样的哈希，命中即说明该文件就是被隐藏的那一个，
        /// 从而重建索引记录，让图标恢复正常显示或回到桌面。
        /// </para>
        /// </remarks>
        /// <returns>恢复的记录数量</returns>
        public int TryRecoverOrphanEntries()
        {
            lock (syncRoot)
            {
                EnsureInitialized();

                try
                {
                    if (!Directory.Exists(StoreRoot))
                    {
                        return 0;
                    }

                    var orphans = new List<string>();
                    foreach (string file in Directory.GetFiles(StoreRoot))
                    {
                        if (!IsStoreMetadataFile(file))
                        {
                            orphans.Add(file);
                        }
                    }

                    foreach (string directory in Directory.GetDirectories(StoreRoot))
                    {
                        orphans.Add(directory);
                    }

                    if (orphans.Count == 0)
                    {
                        return 0;
                    }

                    var candidates = BuildDesktopPathCandidates();
                    if (candidates.Count == 0)
                    {
                        return 0;
                    }

                    int recovered = 0;

                    foreach (string orphan in orphans)
                    {
                        string storedName = Path.GetFileName(orphan.TrimEnd(Path.DirectorySeparatorChar));
                        string id = Path.GetFileNameWithoutExtension(storedName);

                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        if (index.Entries.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (!candidates.TryGetValue(id, out string originalPath))
                        {
                            continue;
                        }

                        bool isDirectory = Directory.Exists(orphan);

                        // 扩展名必须一致，避免哈希前缀偶然相同导致误配
                        if (!isDirectory && !string.Equals(
                                Path.GetExtension(orphan),
                                Path.GetExtension(originalPath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var entry = new HiddenStoreEntry
                        {
                            Id = id,
                            OriginalPath = originalPath,
                            StoredName = storedName,
                            DisplayName = isDirectory
                                ? new DirectoryInfo(originalPath).Name
                                : Path.GetFileName(originalPath),
                            IsDirectory = isDirectory,
                            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Size = isDirectory ? GetDirectorySize(orphan) : GetFileSize(orphan)
                        };

                        index.Entries.Add(entry);
                        recovered++;

                        Log.Information($"已从桌面重新匹配到孤立文件: {storedName} -> {originalPath}");
                    }

                    if (recovered > 0)
                    {
                        SaveIndex();
                        Log.Information($"已恢复 {recovered} 条孤立隐藏记录的索引");
                    }

                    return recovered;
                }
                catch (Exception ex)
                {
                    Log.Information($"恢复孤立隐藏记录失败: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// 判断文件是否为隐藏存储自身的元数据文件（索引 / 宿主文件）
        /// </summary>
        private bool IsStoreMetadataFile(string path)
        {
            string name = Path.GetFileName(path);
            return string.Equals(name, Env.HiddenStoreIndexFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, Env.HiddenStoreHostFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 扫描桌面上的所有条目，建立「哈希前缀 → 原始路径」的候选表
        /// </summary>
        private Dictionary<string, string> BuildDesktopPathCandidates()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var desktopDirectories = new List<string>();
            try
            {
                desktopDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                desktopDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            }
            catch
            {
                // 忽略
            }

            foreach (string desktop in desktopDirectories.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct())
            {
                foreach (string file in SafeEnumerateFiles(desktop))
                {
                    string id = ComputeId(file);
                    if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                    {
                        result[id] = file;
                    }
                }

                foreach (string directory in SafeEnumerateDirectories(desktop))
                {
                    string id = ComputeId(directory);
                    if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                    {
                        result[id] = directory;
                    }
                }
            }

            return result;
        }

        private static List<string> SafeEnumerateFiles(string root)
        {
            var result = new List<string>();

            try
            {
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    result.Add(file);
                }
            }
            catch
            {
                // 忽略无权限的目录
            }

            return result;
        }

        private static List<string> SafeEnumerateDirectories(string root)
        {
            var result = new List<string>();

            try
            {
                foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    result.Add(directory);
                }
            }
            catch
            {
                // 忽略无权限的目录
            }

            return result;
        }

        #endregion

        #region 旧版迁移

        /// <summary>
        /// 旧版会把隐藏的桌面图标放在桌面下的 <c>.layouterhidden</c> 目录中，
        /// 该目录即使设置了隐藏属性也可能被「显示隐藏文件」选项暴露。
        /// 此处把其中的内容迁移到安全存储，并删除桌面上的旧目录。
        /// </summary>
        public void MigrateLegacyStore()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string legacyFolder = Path.Combine(desktopPath, Env.HiddenFolderName);

                if (!Directory.Exists(legacyFolder))
                {
                    return;
                }

                Log.Information("检测到旧版隐藏目录，开始迁移至安全隐藏存储");

                foreach (string file in Directory.GetFiles(legacyFolder))
                {
                    string fileName = Path.GetFileName(file);

                    // 元数据文件不参与迁移，稍后统一清理
                    if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string metadataPath = Path.Combine(legacyFolder, fileName + ".meta");
                    string originalPath = Path.Combine(desktopPath, fileName);

                    if (File.Exists(metadataPath))
                    {
                        try
                        {
                            string recorded = File.ReadAllText(metadataPath).Trim();
                            if (!string.IsNullOrEmpty(recorded))
                            {
                                originalPath = recorded;
                            }
                        }
                        catch
                        {
                            // 保留默认路径
                        }
                    }

                    bool isDirectory = Directory.Exists(file);
                    string storedName = GetStoredName(originalPath, isDirectory);
                    if (string.IsNullOrEmpty(storedName))
                    {
                        continue;
                    }

                    string target = Path.Combine(StoreRoot, storedName);

                    try
                    {
                        if (Directory.Exists(target))
                        {
                            Directory.Delete(target, true);
                        }
                        else if (File.Exists(target))
                        {
                            File.Delete(target);
                        }

                        if (isDirectory)
                        {
                            MoveDirectory(file, target);
                        }
                        else
                        {
                            MoveFile(file, target);
                        }

                        ApplyHiddenAttributes(target, isDirectory);

                        var entry = new HiddenStoreEntry
                        {
                            Id = ComputeId(originalPath),
                            OriginalPath = originalPath,
                            StoredName = storedName,
                            DisplayName = isDirectory ? new DirectoryInfo(originalPath).Name : fileName,
                            IsDirectory = isDirectory,
                            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Size = isDirectory ? GetDirectorySize(target) : GetFileSize(target)
                        };

                        index.Entries.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                        index.Entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"迁移旧版隐藏文件失败({fileName}): {ex.Message}");
                    }
                }

                SaveIndex();

                // 删除桌面上的旧隐藏目录
                try
                {
                    ClearHiddenAttributes(legacyFolder, true);
                    Directory.Delete(legacyFolder, true);
                    Log.Information("旧版隐藏目录已移除，桌面不再残留隐藏目录");
                }
                catch (Exception ex)
                {
                    Log.Information($"清理旧版隐藏目录失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Information($"迁移旧版隐藏存储失败: {ex.Message}");
            }
        }

        #endregion
    }
}
