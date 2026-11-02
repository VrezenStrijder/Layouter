using System;
using System.Globalization;

namespace Layouter.Models
{
    /// <summary>
    /// 备份清单：备份包内 manifest.json 的内容
    /// </summary>
    public class BackupManifest
    {
        /// <summary>备份唯一标识</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>用户指定的备份名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>用户填写的备注</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>创建时间（ISO-8601 UTC 字符串）</summary>
        public string CreatedAt { get; set; } = string.Empty;

        /// <summary>创建备份时的应用程序版本</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>备份中包含的配置文件数量</summary>
        public int FileCount { get; set; }

        /// <summary>创建备份的机器标识（不可反推的哈希值）</summary>
        public string MachineId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 备份列表项：清单信息 + 运行时附加信息（文件路径、大小、有效性等）
    /// </summary>
    public class BackupInfo
    {
        /// <summary>备份唯一标识</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>备份名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>备份备注</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>创建时间（本地时间）</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>创建备份时的应用程序版本</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>备份中包含的配置文件数量</summary>
        public int FileCount { get; set; }

        /// <summary>创建备份的机器标识</summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>备份文件在磁盘上的完整路径</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>备份文件大小（字节）</summary>
        public long FileSize { get; set; }

        /// <summary>备份是否可正常读取</summary>
        public bool IsValid { get; set; }

        /// <summary>备份是否来自本机</summary>
        public bool IsFromThisMachine { get; set; }

        /// <summary>创建时间（显示用）</summary>
        public string CreatedAtText => CreatedAt == default ? "-" : CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>文件大小（显示用）</summary>
        public string FileSizeText
        {
            get
            {
                if (FileSize <= 0)
                {
                    return "-";
                }

                if (FileSize < 1024)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0} B", FileSize);
                }

                if (FileSize < 1024 * 1024)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0:0.#} KB", FileSize / 1024.0);
                }

                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", FileSize / 1024.0 / 1024.0);
            }
        }

        /// <summary>文件数量（显示用）</summary>
        public string FileCountText => IsValid ? FileCount.ToString(CultureInfo.InvariantCulture) : "-";

        /// <summary>有效性（显示用）</summary>
        public string ValidText => IsValid ? "有效" : "无效";

        /// <summary>来源（显示用）</summary>
        public string SourceText => !IsValid ? "-" : (IsFromThisMachine ? "来自本机" : "其他设备");

        /// <summary>备注（显示用）</summary>
        public string RemarkText => string.IsNullOrWhiteSpace(Remark) ? "（无备注）" : Remark;
    }

    /// <summary>
    /// 还原操作的结果
    /// </summary>
    public class RestoreResult
    {
        /// <summary>是否还原成功</summary>
        public bool Success { get; set; }

        /// <summary>失败原因</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>还原前自动创建的安全快照路径</summary>
        public string SnapshotPath { get; set; } = string.Empty;
    }
}
