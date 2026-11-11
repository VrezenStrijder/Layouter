using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Layouter.Models;
using Layouter.Utility;

namespace Layouter.Services
{
    public class DesktopIconService
    {
        private static readonly Lazy<DesktopIconService> instance = new Lazy<DesktopIconService>(() => new DesktopIconService());
        public static DesktopIconService Instance => instance.Value;

        // 获取桌面路径
        public string GetDesktopPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        // 获取桌面上的所有图标
        public List<DesktopIcon> GetDesktopIcons()
        {
            List<DesktopIcon> icons = new List<DesktopIcon>();
            string desktopPath = GetDesktopPath();

            try
            {
                foreach (string file in Directory.GetFiles(desktopPath))
                {
                    // 排除隐藏文件
                    if ((File.GetAttributes(file) & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }
                    string fileName = Path.GetFileName(file);
                    icons.Add(new DesktopIcon
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        IconPath = file,
                        Position = new Point(0, 0), // 默认位置
                        Size = new Size(64, 64)     // 默认大小
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取桌面图标时出错: {ex.Message}");
            }

            return icons;
        }

        /// <summary>
        /// 刷新桌面
        /// </summary>
        public void RefreshDesktop()
        {
            try
            {
                // 获取桌面窗口和视图
                IntPtr hDesktop = Win32.FindWindow("Progman", "Program Manager");
                IntPtr hWorkerW = IntPtr.Zero;
                IntPtr hDefView = Win32.FindWindowEx(hDesktop, IntPtr.Zero, "SHELLDLL_DefView", null);

                if (hDefView == IntPtr.Zero)
                {
                    // 如果没有找到默认视图，尝试通过WorkerW查找
                    while (hDefView == IntPtr.Zero)
                    {
                        hWorkerW = Win32.FindWindowEx(IntPtr.Zero, hWorkerW, "WorkerW", null);
                        if (hWorkerW == IntPtr.Zero) break;
                        hDefView = Win32.FindWindowEx(hWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    }
                }

                if (hDefView != IntPtr.Zero)
                {
                    // 通知Shell刷新桌面
                    IntPtr hListView = Win32.FindWindowEx(hDefView, IntPtr.Zero, "SysListView32", "FolderView");
                    if (hListView != IntPtr.Zero)
                    {
                        const int WM_COMMAND = 0x0111;
                        const int REFRESH_COMMAND = 0x7103;

                        // 使用 SendMessage 代替 HandleMessage
                        Win32.SendMessage(hListView, WM_COMMAND, (IntPtr)REFRESH_COMMAND, IntPtr.Zero);
                    }
                }

                // 使用SHChangeNotify通知系统更改
                Win32.SHChangeNotify(Win32.SHCNE_CREATE, Win32.SHCNF_IDLIST | Win32.SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Information($"刷新桌面时出错: {ex.Message}");
            }
        }



        /// <summary>
        /// 创建桌面快捷方式
        /// </summary>
        /// <param name="targetPath"></param>
        /// <param name="shortcutName"></param>
        /// <returns></returns>
        public bool CreateShortcutOnDesktop(string targetPath, string shortcutName)
        {
            try
            {
                string desktopPath = GetDesktopPath();
                string shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");

                if (File.Exists(shortcutPath))
                {
                    // 快捷方式已存在
                    return false;
                }

                Type shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                var link = (Win32.IShellLink)Activator.CreateInstance(shellLinkType);

                link.SetPath(targetPath);
                link.SetDescription($"Shortcut to {Path.GetFileName(targetPath)}");

                // 设置图标
                link.SetIconLocation(targetPath, 0);

                // 获取IPersistFile接口保存文件
                var file = (Win32.IPersistFile)link;
                file.Save(shortcutPath, true);

                // 刷新桌面
                RefreshDesktop();

                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"创建桌面快捷方式时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据DesktopIcon对象创建桌面快捷方式
        /// </summary>
        /// <param name="icon"></param>
        /// <returns></returns>
        public bool CreateShortcutOnDesktop(DesktopIcon icon)
        {
            try
            {
                if (icon == null || string.IsNullOrEmpty(icon.IconPath))
                {
                    Log.Information("创建快捷方式失败：图标或路径为空");
                    return false;
                }

                // 获取文件名和目标路径
                string desktopPath = GetDesktopPath();
                string iconFileName = Path.GetFileName(icon.IconPath);
                string shortcutName = Path.GetFileNameWithoutExtension(icon.IconPath);
                string shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                string targetPath = icon.IconPath;

                // 如果源是快捷方式，直接复制到桌面
                bool isSourceShortcut = icon.IconPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                if (isSourceShortcut)
                {
                    Log.Information($"源是快捷方式，直接复制到桌面: {icon.IconPath}");
                    return CopyFileToDesktop(icon.IconPath);
                }

                // 检查是否已存在同名快捷方式
                if (File.Exists(shortcutPath))
                {
                    int counter = 1;
                    string baseShortcutName = shortcutName;

                    // 生成不重复的快捷方式名称
                    do
                    {
                        shortcutName = $"{baseShortcutName} ({counter})";
                        shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                        counter++;
                    } while (File.Exists(shortcutPath));
                }

                Log.Information($"创建快捷方式：{shortcutPath} -> {targetPath}");

                // 创建快捷方式
                Type shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                var link = (Win32.IShellLink)Activator.CreateInstance(shellLinkType);

                link.SetPath(targetPath);
                link.SetDescription($"Shortcut to {Path.GetFileName(targetPath)}");

                // 设置图标
                link.SetIconLocation(targetPath, 0);

                // 获取IPersistFile接口保存文件
                var file = (Win32.IPersistFile)link;
                file.Save(shortcutPath, true);

                // 刷新桌面
                RefreshDesktop();

                Log.Information("快捷方式创建成功");
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"创建桌面快捷方式时出错: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 复制文件到桌面
        /// </summary>
        /// <param name="sourceFilePath"></param>
        /// <param name="newFileName"></param>
        /// <returns></returns>
        public bool CopyFileToDesktop(string sourceFilePath, string newFileName = null)
        {
            try
            {
                string fileName = newFileName ?? Path.GetFileName(sourceFilePath);
                string desktopPath = GetDesktopPath();
                string targetPath = Path.Combine(desktopPath, fileName);

                // 如果目标文件存在，则使用新名称
                if (File.Exists(targetPath))
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string extension = Path.GetExtension(fileName);
                    int counter = 1;

                    do
                    {
                        fileName = $"{fileNameWithoutExt} ({counter}){extension}";
                        targetPath = Path.Combine(desktopPath, fileName);
                        counter++;
                    } while (File.Exists(targetPath));
                }

                // 尝试复制文件 - 如果源文件存在则复制，否则只返回目标路径
                if (File.Exists(sourceFilePath))
                {
                    File.Copy(sourceFilePath, targetPath);
                    Log.Information($"已将文件复制到桌面: {targetPath}");
                }
                else
                {
                    Log.Information($"源文件不存在，无法复制: {sourceFilePath}");
                    return false;
                }

                // 刷新桌面以显示新文件
                RefreshDesktop();

                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"复制文件到桌面时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从分区拖回到桌面
        /// </summary>
        /// <param name="icon"></param>
        /// <returns></returns>
        public bool RestoreIconToDesktop(DesktopIcon icon)
        {
            try
            {
                if (icon == null || string.IsNullOrEmpty(icon.IconPath))
                {
                    return false;
                }

                // 检查文件是否存在
                if (!File.Exists(icon.IconPath))
                {
                    return false;
                }

                // 检查文件是否已经在桌面上
                string desktopPath = GetDesktopPath();
                string fileName = Path.GetFileName(icon.IconPath);
                string desktopFilePath = Path.Combine(desktopPath, fileName);

                if (string.Equals(icon.IconPath, desktopFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // 文件已经在桌面上，不需要额外操作
                    return true;
                }

                // 如果是快捷方式，则创建快捷方式
                if (Path.GetExtension(icon.IconPath).ToLower() == ".lnk")
                {
                    // 获取快捷方式指向的文件
                    Type shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                    var link = (Win32.IShellLink)Activator.CreateInstance(shellLinkType);
                    ((Win32.IPersistFile)link).Load(icon.IconPath, 0);

                    System.Text.StringBuilder targetPath = new System.Text.StringBuilder(260);
                    link.GetPath(targetPath, targetPath.Capacity, out IntPtr _, 0);

                    // 在桌面上创建新的快捷方式
                    return CreateShortcutOnDesktop(targetPath.ToString(), icon.Name);
                }
                else
                {
                    // 否则复制文件到桌面
                    return CopyFileToDesktop(icon.IconPath);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"将图标恢复到桌面时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 隐藏桌面上的文件图标 - 使用增强版隐藏方法
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool HideDesktopIcon(string filePath)
        {
            return HideDesktopIcon(filePath, out _);
        }

        /// <summary>
        /// 隐藏桌面上的文件图标，并返回其在安全存储中的真实存放路径
        /// </summary>
        /// <param name="filePath">桌面上的原始路径</param>
        /// <param name="storedPath">隐藏后在安全存储中的存放路径（系统图标返回原路径）</param>
        /// <returns>是否隐藏成功</returns>
        public bool HideDesktopIcon(string filePath, out string storedPath)
        {
            storedPath = filePath;

            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return false;
                }

                bool isFile = File.Exists(filePath);
                bool isFolder = Directory.Exists(filePath);
                var shellIcon = ShellUtil.GetSystemIconType(filePath);

                if (!isFile && !isFolder && !shellIcon.HasValue)
                {
                    Log.Information($"无法隐藏不存在的文件: {filePath}");
                    return false;
                }

                if (shellIcon.HasValue)
                {
                    ShellUtil.SetShellIconVisibility(filePath, false);
                    return true;
                }

                // 移入安全隐藏存储：桌面命名空间中不再留下任何隐藏目录，
                // 存放名称使用哈希摘要，路径映射索引写入宿主文件的索引文件。
                if (SecureHiddenStore.Instance.Hide(filePath, out string hiddenPath))
                {
                    storedPath = hiddenPath;
                    Log.Information($"成功隐藏桌面图标: {filePath} -> {Path.GetFileName(hiddenPath)}");
                    RefreshDesktop();
                    return true;
                }

                Log.Information($"隐藏桌面图标失败: {filePath}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Information($"隐藏桌面图标时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 显示桌面上的文件图标
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool ShowDesktopIcon(string filePath)
        {
            try
            {
                // 确保文件路径有效
                if (string.IsNullOrEmpty(filePath))
                {
                    Log.Information("无法显示空路径的图标");
                    return false;
                }

                var systemIcon = ShellUtil.GetSystemIconType(filePath);
                //系统图标
                if (systemIcon.HasValue)
                {
                    ShellUtil.SetShellIconVisibility(filePath, true);
                    return true;
                }

                // 从安全隐藏存储还原（filePath 可能是原始路径，也可能是存放路径）
                if (SecureHiddenStore.Instance.Restore(filePath, out string restoredPath))
                {
                    Log.Information($"成功显示桌面图标: {restoredPath}");
                    RefreshDesktop();
                    return true;
                }

                // 兼容旧版桌面隐藏目录
                if (RestoreFromLegacyFolder(filePath))
                {
                    RefreshDesktop();
                    return true;
                }

                Log.Information($"未找到可还原的隐藏内容: {filePath}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Information($"显示桌面图标时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从旧版桌面隐藏目录（.layouterhidden）还原文件，用于兼容升级前的数据
        /// </summary>
        private bool RestoreFromLegacyFolder(string filePath)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    return false;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string legacyFolderPath = Path.Combine(desktopPath, Env.HiddenFolderName);

                if (!Directory.Exists(legacyFolderPath))
                {
                    return false;
                }

                string legacyFilePath = Path.Combine(legacyFolderPath, fileName);
                string metadataPath = Path.Combine(legacyFolderPath, $"{fileName}.meta");

                if (!File.Exists(legacyFilePath) && !Directory.Exists(legacyFilePath))
                {
                    return false;
                }

                string originalPath = RemoveHiddenPathInIconPath(filePath);
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

                if (File.Exists(originalPath) || Directory.Exists(originalPath))
                {
                    Log.Information($"目标位置已存在文件: {originalPath}，无法恢复");
                    return false;
                }

                if (File.Exists(legacyFilePath))
                {
                    File.Move(legacyFilePath, originalPath);
                }
                else if (Directory.Exists(legacyFilePath))
                {
                    Directory.Move(legacyFilePath, originalPath);
                }

                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"从旧版隐藏目录还原失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据DesktopIcon对象隐藏或显示桌面图标
        /// </summary>
        /// <param name="icon"></param>
        /// <param name="hide"></param>
        /// <returns></returns>
        public bool ToggleDesktopIconVisibility(DesktopIcon icon, bool hide)
        {
            if (icon == null || string.IsNullOrEmpty(icon.IconPath))
            {
                return false;
            }

            return hide ? HideDesktopIcon(icon.IconPath) : ShowDesktopIcon(icon.IconPath);
        }

        /// <summary>
        /// 移除路径中的隐藏目录标识
        /// </summary>
        /// <remarks>
        /// 对于存放在安全隐藏存储中的图标，返回其原始路径；
        /// 对于旧版桌面隐藏目录，移除目录标识片段。
        /// </remarks>
        public string RemoveHiddenPathInIconPath(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath))
            {
                return iconPath;
            }

            var store = SecureHiddenStore.Instance;

            if (store.IsStorePath(iconPath))
            {
                return store.ResolveOriginalPath(iconPath);
            }

            if (iconPath.Contains(Env.HiddenFolderName))
            {
                return iconPath.Replace($"{Env.HiddenFolderName}{Path.DirectorySeparatorChar}", "");
            }
            return iconPath;
        }

        /// <summary>
        /// 为路径添加隐藏目录标识
        /// </summary>
        /// <remarks>
        /// 返回该文件在安全隐藏存储中的存放路径。隐藏操作是确定性的
        /// （存放名称由原始路径的哈希推导），因此在隐藏之前即可得到正确结果。
        /// </remarks>
        public string CombineHiddenPathWithIconPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            var store = SecureHiddenStore.Instance;

            if (store.IsStorePath(filePath))
            {
                return filePath;
            }

            // 已经处于隐藏状态
            string stored = store.ResolveStoredPath(filePath);
            if (!string.Equals(stored, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return stored;
            }

            bool isDirectory = Directory.Exists(filePath);
            string predicted = store.GetStoredPath(filePath, isDirectory);

            return string.IsNullOrEmpty(predicted) ? filePath : predicted;
        }

        /// <summary>
        /// 获取可用的图标路径
        /// </summary>
        /// <param name="iconPath"></param>
        /// <returns></returns>
        public string GetAvailableIconPath(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath))
            {
                return iconPath;
            }

            var store = SecureHiddenStore.Instance;

            // 已经是安全存储中的存放路径
            if (store.IsStorePath(iconPath))
            {
                return iconPath;
            }

            if (File.Exists(iconPath) || Directory.Exists(iconPath))
            {
                return iconPath;
            }

            // 原始路径不可见（已被隐藏）→ 解析到存放路径
            string stored = store.ResolveStoredPath(iconPath);
            if (!string.Equals(stored, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                return stored;
            }

            // 旧版隐藏目录兼容
            string purePath = string.Empty;
            string hiddenPath = string.Empty;

            if (iconPath.Contains(Env.HiddenFolderName))
            {
                purePath = RemoveHiddenPathInIconPath(iconPath);
                hiddenPath = iconPath;
            }
            else
            {
                purePath = iconPath;
                hiddenPath = CombineHiddenPathWithIconPath(iconPath);
            }

            if (File.Exists(purePath))
            {
                return purePath;
            }
            else if (File.Exists(hiddenPath))
            {
                return hiddenPath;
            }
            return iconPath;
        }
    }
}
