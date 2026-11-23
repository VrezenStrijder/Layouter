using System;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Layouter.Utility;

namespace Layouter.Models
{
    /// <summary>
    /// 搜索结果的种类
    /// </summary>
    public enum SearchResultKind
    {
        /// <summary>分区内的桌面图标</summary>
        Partition,

        /// <summary>整个文件系统（Windows 搜索索引）中的文件/文件夹</summary>
        FileSystem
    }

    /// <summary>
    /// 搜索结果条目
    /// </summary>
    /// <remarks>
    /// 一条结果可以是「某个分区里的某个图标」，也可以是「文件系统里的某个文件/文件夹」。
    /// 图标预览是异步填充的：图标位图要从系统里提取（较慢），
    /// 这里先给出占位图，提取完成后再回填，避免搜索框卡顿。
    /// </remarks>
    public class SearchResultItem : ObservableObject
    {
        private BitmapSource? iconImage;

        /// <summary>「文件系统」分组使用的固定键，避免与真实分区名冲突</summary>
        public const string FileSystemGroupKey = "__filesystem_results__";

        /// <summary>「文件系统」分组标题</summary>
        public const string FileSystemGroupTitle = "文件系统";

        /// <summary>
        /// 构造一条「分区图标」结果
        /// </summary>
        public SearchResultItem(DesktopIcon icon, string windowId, string partitionName, int score)
        {
            Kind = SearchResultKind.Partition;
            Icon = icon;
            WindowId = windowId ?? string.Empty;
            PartitionName = partitionName ?? string.Empty;
            Score = score;

            GroupKey = "p:" + WindowId;
            GroupTitle = string.IsNullOrEmpty(PartitionName) ? "未命名分区" : PartitionName;

            ResolveTarget();
        }

        /// <summary>
        /// 构造一条「文件系统」结果
        /// </summary>
        /// <param name="filePath">文件的真实本地绝对路径</param>
        /// <param name="displayName">用于显示的文件名</param>
        /// <param name="typeText">Windows 索引给出的类型说明（可为空）</param>
        /// <param name="isDirectory">是否为文件夹</param>
        /// <param name="score">排序得分</param>
        public SearchResultItem(string filePath, string displayName, string typeText, bool isDirectory, int score)
        {
            Kind = SearchResultKind.FileSystem;
            Icon = null;
            WindowId = string.Empty;
            PartitionName = string.Empty;
            Score = score;

            FilePath = filePath ?? string.Empty;
            FileSystemName = displayName ?? Path.GetFileName(FilePath);
            TypeText = typeText ?? string.Empty;
            IsDirectory = isDirectory;

            GroupKey = FileSystemGroupKey;
            GroupTitle = FileSystemGroupTitle;

            TargetPath = FilePath;
            try
            {
                if (!isDirectory)
                {
                    string? folder = Path.GetDirectoryName(FilePath);
                    FolderPath = string.IsNullOrEmpty(folder) ? null : folder;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"计算文件系统结果所在目录失败({FilePath}): {ex.Message}");
                FolderPath = null;
            }
        }

        /// <summary>结果种类</summary>
        public SearchResultKind Kind { get; }

        /// <summary>是否为文件系统结果</summary>
        public bool IsFileSystem => Kind == SearchResultKind.FileSystem;

        /// <summary>是否为分区图标结果</summary>
        public bool IsPartitionIcon => Kind == SearchResultKind.Partition;

        /// <summary>对应的图标（文件系统结果为 null）</summary>
        public DesktopIcon? Icon { get; }

        /// <summary>所属分区窗口的标识（文件系统结果为空）</summary>
        public string WindowId { get; }

        /// <summary>所属分区名称（文件系统结果为空）</summary>
        public string PartitionName { get; }

        /// <summary>文件系统的真实路径（分区图标结果为空）</summary>
        public string FilePath { get; } = string.Empty;

        /// <summary>文件系统结果的文件名</summary>
        public string FileSystemName { get; } = string.Empty;

        /// <summary>文件系统结果的类型说明</summary>
        public string TypeText { get; } = string.Empty;

        /// <summary>文件系统结果是否为文件夹</summary>
        public bool IsDirectory { get; }

        /// <summary>匹配得分（越大越靠前）</summary>
        public int Score { get; }

        /// <summary>分组键（用于 ICollectionView 分组）</summary>
        public string GroupKey { get; }

        /// <summary>分组小标题文字</summary>
        public string GroupTitle { get; }

        /// <summary>是否为「文件系统」分组（用于区块分隔样式）</summary>
        public bool IsFileSystemGroup => Kind == SearchResultKind.FileSystem;

        /// <summary>
        /// 是否为所在区块的第一条（用于在该条上方绘制区块小标题）
        /// </summary>
        /// <remarks>由 ViewModel 在生成结果时设置，设置发生在绑定之前，无需变更通知。</remarks>
        public bool IsGroupStart { get; set; }

        /// <summary>分组内序号（用于稳定排序，文件系统分组恒排最后）</summary>
        public int GroupOrder { get; set; }

        /// <summary>是否绘制「分区区块」小标题</summary>
        public bool IsPartitionGroupStart => IsGroupStart && Kind == SearchResultKind.Partition;

        /// <summary>是否绘制「文件系统区块」小标题（带上分隔线）</summary>
        public bool IsFileSystemGroupStart => IsGroupStart && Kind == SearchResultKind.FileSystem;

        /// <summary>显示名称</summary>
        public string DisplayName => IsFileSystem ? FileSystemName : (Icon?.Name ?? string.Empty);

        /// <summary>显示路径</summary>
        public string DisplayPath => IsFileSystem ? FilePath : (Icon?.DisplayPath ?? string.Empty);

        /// <summary>类型对应的中文说明</summary>
        public string TypeName
        {
            get
            {
                if (IsFileSystem)
                {
                    if (!string.IsNullOrEmpty(TypeText))
                    {
                        return TypeText;
                    }

                    return IsDirectory ? "文件夹" : "文件";
                }

                switch (Icon?.IconType)
                {
                    case IconType.Shortcut: return "快捷方式";
                    case IconType.Folder: return "文件夹";
                    case IconType.Shell: return "系统";
                    case IconType.File: return "文件";
                    default: return string.Empty;
                }
            }
        }

        /// <summary>
        /// 结果实际指向的目标路径
        /// </summary>
        /// <remarks>
        /// 快捷方式会解析成它真正指向的程序或文件；
        /// 文件系统结果就是它本身。供「打开」「管理员打开」「打开所在目录」使用。
        /// </remarks>
        public string TargetPath { get; private set; } = string.Empty;

        /// <summary>目标所在目录（目标本身是目录时为 null）</summary>
        public string? FolderPath { get; private set; }

        /// <summary>是否提供「打开所在目录」功能</summary>
        public bool CanOpenFolder => !string.IsNullOrEmpty(FolderPath);

        /// <summary>是否提供「定位」功能（仅分区图标结果）</summary>
        public bool CanLocate => Kind == SearchResultKind.Partition;

        /// <summary>是否提供「添加到分区」功能（仅文件系统结果）</summary>
        public bool CanAddToPartition => Kind == SearchResultKind.FileSystem;

        /// <summary>
        /// 是否提供「以管理员身份运行」功能
        /// </summary>
        /// <remarks>
        /// 只对可执行文件 / 快捷方式 / 脚本等有意义，文件夹与文档不显示该按钮。
        /// </remarks>
        public bool CanRunAsAdmin
        {
            get
            {
                string path = TargetPath;
                if (string.IsNullOrEmpty(path) || IsDirectory)
                {
                    return false;
                }

                string ext;
                try
                {
                    ext = Path.GetExtension(path);
                }
                catch
                {
                    return false;
                }

                if (string.IsNullOrEmpty(ext))
                {
                    return false;
                }

                switch (ext.ToLowerInvariant())
                {
                    case ".exe":
                    case ".lnk":
                    case ".bat":
                    case ".cmd":
                    case ".msi":
                    case ".msix":
                    case ".ps1":
                    case ".vbs":
                    case ".com":
                    case ".scr":
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// 用于「打开 / 以管理员运行」的路径
        /// </summary>
        /// <remarks>
        /// 分区图标优先使用它的原始图标路径（快捷方式里记录了参数、工作目录、图标，
        /// 直接交给 ShellExecute 才能完整复现双击行为）；解析目标仅作兜底。
        /// 文件系统结果就是它自身路径。
        /// </remarks>
        public string OpenPath
        {
            get
            {
                if (IsFileSystem)
                {
                    return FilePath;
                }

                string iconPath = Icon?.IconPath ?? string.Empty;
                return string.IsNullOrEmpty(iconPath) ? TargetPath : iconPath;
            }
        }

        /// <summary>
        /// 解析分区图标的目标路径与所属目录
        /// </summary>
        private void ResolveTarget()
        {
            string? rawPath = Icon?.IconPath;

            if (string.IsNullOrEmpty(rawPath))
            {
                return;
            }

            string target = rawPath;

            // 快捷方式要解析出真正的目标程序，才能知道它装在哪个目录
            try
            {
                if (ShortCutUtil.IsShortcutPath(rawPath))
                {
                    string resolved = ShortCutUtil.ResolveShortcut(rawPath);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        target = resolved;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"解析搜索结果目标路径失败({rawPath}): {ex.Message}");
            }

            TargetPath = target;

            try
            {
                // 目标本身就是目录时，不提供「打开所在目录」
                if (string.IsNullOrEmpty(target) || Directory.Exists(target))
                {
                    FolderPath = null;
                    return;
                }

                string? folder = Path.GetDirectoryName(target);
                FolderPath = string.IsNullOrEmpty(folder) ? null : folder;
            }
            catch (Exception ex)
            {
                Log.Information($"计算搜索结果所在目录失败({target}): {ex.Message}");
                FolderPath = null;
            }
        }

        /// <summary>图标预览</summary>
        public BitmapSource? IconImage
        {
            get => iconImage;
            set => SetProperty(ref iconImage, value);
        }
    }
}
