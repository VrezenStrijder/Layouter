using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Layouter.Utility
{
    /// <summary>
    /// 图标提取工具。
    /// <para>
    /// 早期实现只调用 <c>SHGetFileInfo(SHGFI_LARGEICON)</c>，拿到的是 32×32 的小图标，
    /// 在 48 / 64 像素的显示尺寸下会被放大，表现为图标「不清晰」。
    /// 现在改为通过系统图像列表（<c>IImageList</c>）按需获取原生尺寸的图标：
    /// 32 → SHIL_LARGE，48 → SHIL_EXTRALARGE，64 及以上 → SHIL_JUMBO（256×256），
    /// 并统一使用 <c>CreateBitmapSourceFromHIcon</c> 转换，保留 32 位 ARGB 的透明通道。
    /// </para>
    /// </summary>
    public class ShortCutUtil
    {
        #region 常量

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeOf, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_SMALLICON = 0x1;
        /// <summary>获取 48×48 大图标</summary>
        private const uint SHGFI_ICONLARGE = 0x20;
        /// <summary>获取系统图像列表中的索引</summary>
        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        private const int SHIL_SMALL = 1;
        private const int SHIL_LARGE = 2;
        private const int SHIL_EXTRALARGE = 3;
        private const int SHIL_JUMBO = 4;

        /// <summary>默认图标边长</summary>
        public const int DefaultIconSize = 48;

        #endregion

        #region COM 接口

        /// <summary>
        /// 系统图像列表。方法顺序必须与原生接口的虚表顺序一致，
        /// <c>GetIcon</c> 位于第 8 个（索引 7）。
        /// </summary>
        [ComImport, Guid("46EB5926-582E-4017-9FDC-EFC8B7D7F0C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int cx, int cy, out IntPtr phicon);
        }

        #endregion

        #region 缓存

        /// <summary>
        /// 图标缓存：键为「路径 + 边长」，避免重复调用 Shell 接口造成卡顿。
        /// </summary>
        private static readonly ConcurrentDictionary<string, BitmapSource> iconCache = new ConcurrentDictionary<string, BitmapSource>();

        /// <summary>
        /// 清空图标缓存（桌面内容发生变化后调用）
        /// </summary>
        public static void ClearIconCache()
        {
            try
            {
                iconCache.Clear();
            }
            catch (Exception ex)
            {
                Log.Information($"清理图标缓存失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// 根据快捷方式路径解析目标路径
        /// </summary>
        /// <param name="shortcutPath">快捷方式路径</param>
        public static string ResolveShortcut(string shortcutPath)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                var shortcut = shell.CreateShortcut(shortcutPath);
                string target = shortcut.TargetPath;

                // 目标路径里可能带有环境变量（如 %SystemRoot%\...），需要展开后再使用
                if (!string.IsNullOrEmpty(target))
                {
                    target = Environment.ExpandEnvironmentVariables(target);
                }

                return target;
            }
            catch (Exception ex)
            {
                Log.Information($"解析快捷方式失败({shortcutPath}): {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 尝试用「快捷方式目标文件」的图标替换快捷方式图标。
        /// </summary>
        /// <remarks>
        /// Windows 资源管理器为 <c>.lnk</c> 绘制的图标会叠加一个蓝色快捷方式箭头，
        /// 只有取目标文件自身的图标才能彻底消除该箭头。
        /// </remarks>
        private static bool TryGetShortcutTargetIcon(string shortcutPath, int size, out BitmapSource source)
        {
            source = null;

            try
            {
                string targetPath = ResolveShortcut(shortcutPath);
                if (string.IsNullOrEmpty(targetPath))
                {
                    return false;
                }

                targetPath = targetPath.Trim().Trim('"');

                bool isDir = Directory.Exists(targetPath);
                if (!isDir && !File.Exists(targetPath))
                {
                    return false;
                }

                // 目标本身也是快捷方式时不再继续解析，避免循环引用
                if (!isDir && IsShortcutPath(targetPath))
                {
                    return false;
                }

                if (TryGetImageListBitmap(targetPath, size, out BitmapSource viaImageList))
                {
                    source = viaImageList;
                    return true;
                }

                BitmapSource viaFile = isDir
                    ? GetIconFromFolder(targetPath, size)
                    : GetIconFromFileCore(targetPath, size);

                if (viaFile == null)
                {
                    return false;
                }

                source = viaFile;
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"获取快捷方式目标图标失败({shortcutPath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取适合显示的图标（默认 48 像素）
        /// </summary>
        public static BitmapSource GetSuitableIcon(string filePath)
        {
            return GetSuitableIcon(filePath, DefaultIconSize);
        }

        /// <summary>
        /// 获取适合显示的图标
        /// </summary>
        /// <param name="filePath">文件 / 目录 / Shell 路径</param>
        /// <param name="size">期望的图标边长（像素）</param>
        public static BitmapSource GetSuitableIcon(string filePath, int size)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return GetDefaultIcon(size);
            }

            int targetSize = NormalizeSize(size);
            string cacheKey = $"{targetSize}|{filePath}";

            if (iconCache.TryGetValue(cacheKey, out BitmapSource cached))
            {
                return cached;
            }

            BitmapSource source = null;
            try
            {
                source = LoadIcon(filePath, targetSize);
            }
            catch (Exception ex)
            {
                Log.Information($"获取图标失败({filePath}): {ex.Message}");
            }

            if (source == null)
            {
                source = GetDefaultIcon(targetSize);
            }

            if (source != null)
            {
                try
                {
                    if (!source.IsFrozen)
                    {
                        source.Freeze();
                    }
                }
                catch
                {
                    // 忽略冻结失败
                }

                iconCache[cacheKey] = source;
            }

            return source;
        }

        /// <summary>
        /// 规范化尺寸到 Shell 支持的档位（32 / 48 / 64 / 128 / 256）
        /// </summary>
        private static int NormalizeSize(int size)
        {
            if (size <= 32) return 32;
            if (size <= 48) return 48;
            if (size <= 64) return 64;
            if (size <= 128) return 128;
            return 256;
        }

        /// <summary>
        /// 实际加载图标
        /// </summary>
        private static BitmapSource LoadIcon(string filePath, int size)
        {
            // 1) 快捷方式：系统图像列表中的快捷方式图标自带「快捷方式箭头」叠加，
            //    这里直接解析出目标并使用目标图标，从根本上消除箭头。
            if (IsShortcutPath(filePath)
                && TryGetShortcutTargetIcon(filePath, size, out BitmapSource viaShortcutTarget))
            {
                return viaShortcutTarget;
            }

            // 2) 优先使用系统图像列表，可拿到原生高分辨率图标
            if (TryGetImageListBitmap(filePath, size, out BitmapSource viaImageList))
            {
                return viaImageList;
            }

            // 3) 目录
            if (Directory.Exists(filePath))
            {
                return GetIconFromFolder(filePath, size);
            }

            // 4) 普通文件
            if (File.Exists(filePath))
            {
                return GetIconFromFile(filePath, size);
            }

            // 5) Shell 命名空间路径
            return ShellUtil.GetIconFromShellPath(filePath, true);
        }

        /// <summary>
        /// 通过系统图像列表获取图标
        /// </summary>
        private static bool TryGetImageListBitmap(string path, int targetSize, out BitmapSource source)
        {
            source = null;
            IntPtr hIcon = IntPtr.Zero;

            try
            {
                if (!TryGetImageListIcon(path, targetSize, out hIcon, out int nativeSize))
                {
                    return false;
                }

                BitmapSource raw = ConvertIconToBitmapSource(hIcon, nativeSize);
                DestroyIcon(hIcon);
                hIcon = IntPtr.Zero;

                if (raw == null)
                {
                    return false;
                }

                source = ScaleTo(raw, targetSize);
                return source != null;
            }
            catch (Exception ex)
            {
                Log.Information($"读取系统图像列表图标失败({path}): {ex.Message}");
                return false;
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        DestroyIcon(hIcon);
                    }
                    catch
                    {
                        // 忽略
                    }
                }
            }
        }

        /// <summary>
        /// 从系统图像列表取出图标句柄
        /// </summary>
        private static bool TryGetImageListIcon(string path, int targetSize, out IntPtr hIcon, out int nativeSize)
        {
            hIcon = IntPtr.Zero;
            nativeSize = 0;

            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES;
                IntPtr res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf(shfi), flags);

                if (res == IntPtr.Zero || shfi.iIcon == 0)
                {
                    return false;
                }

                // 按目标尺寸挑选图像列表，并按从高到低的顺序回退
                int[] listIds;
                if (targetSize > 48)
                {
                    listIds = new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE };
                }
                else if (targetSize > 32)
                {
                    listIds = new[] { SHIL_EXTRALARGE, SHIL_JUMBO, SHIL_LARGE };
                }
                else
                {
                    listIds = new[] { SHIL_LARGE, SHIL_EXTRALARGE };
                }

                foreach (int listId in listIds)
                {
                    int listSize = GetImageListSize(listId);

                    Guid iid = typeof(IImageList).GUID;
                    IImageList imageList = null;

                    try
                    {
                        int hr = SHGetImageList(listId, ref iid, out imageList);
                        if (hr != 0 || imageList == null)
                        {
                            continue;
                        }

                        IntPtr h = IntPtr.Zero;
                        hr = imageList.GetIcon(shfi.iIcon, listSize, listSize, out h);

                        if (hr == 0 && h != IntPtr.Zero)
                        {
                            hIcon = h;
                            nativeSize = listSize;
                            return true;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(imageList);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Information($"获取系统图像列表句柄失败({path}): {ex.Message}");
                return false;
            }
        }

        private static int GetImageListSize(int listId)
        {
            switch (listId)
            {
                case SHIL_JUMBO: return 256;
                case SHIL_EXTRALARGE: return 48;
                case SHIL_LARGE: return 32;
                case SHIL_SMALL: return 16;
                default: return 32;
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
            catch
            {
                // 忽略释放失败
            }
        }

        /// <summary>
        /// 把图标句柄转换为保留透明通道的位图源
        /// </summary>
        private static BitmapSource ConvertIconToBitmapSource(IntPtr hIcon, int expectedSize)
        {
            try
            {
                using (Icon icon = Icon.FromHandle(hIcon))
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    if (source == null)
                    {
                        return null;
                    }

                    source.Freeze();

                    // CreateBitmapSourceFromHIcon 偶尔会回退到 32×32，
                    // 此时用原生尺寸做一次缩放补偿，避免放大后模糊。
                    if (expectedSize > 0 && source.PixelWidth > 0 && source.PixelWidth < expectedSize)
                    {
                        return ScaleTo(source, expectedSize);
                    }

                    return source;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"图标句柄转换失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 缩放位图源（保持比例，居中裁剪到正方形）
        /// </summary>
        private static BitmapSource ScaleTo(BitmapSource source, int size)
        {
            if (source == null || size <= 0)
            {
                return source;
            }

            if (source.PixelWidth == size && source.PixelHeight == size)
            {
                return source;
            }

            try
            {
                double scale = (double)size / Math.Max(source.PixelWidth, source.PixelHeight);
                var transform = new ScaleTransform(scale, scale);
                transform.Freeze();

                var scaled = new TransformedBitmap(source, transform);
                scaled.Freeze();
                return scaled;
            }
            catch (Exception ex)
            {
                Log.Information($"缩放图标失败: {ex.Message}");
                return source;
            }
        }

        public static BitmapSource GetIconFromFile(string filePath)
        {
            return GetIconFromFile(filePath, DefaultIconSize);
        }

        /// <summary>
        /// 获取文件图标
        /// </summary>
        /// <remarks>
        /// 快捷方式（<c>.lnk</c>）会优先返回目标文件的图标，避免出现左下角的「快捷方式箭头」。
        /// </remarks>
        public static BitmapSource GetIconFromFile(string filePath, int size)
        {
            if (!string.IsNullOrEmpty(filePath) && IsShortcutPath(filePath))
            {
                if (TryGetShortcutTargetIcon(filePath, size, out BitmapSource viaTarget))
                {
                    return viaTarget;
                }
            }

            return GetIconFromFileCore(filePath, size);
        }

        /// <summary>
        /// 获取文件图标（不解析快捷方式）
        /// </summary>
        private static BitmapSource GetIconFromFileCore(string filePath, int size)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return GetDefaultIcon(size);
                }

                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_ICONLARGE;

                // 直接获取文件图标
                if (File.Exists(filePath))
                {
                    SHGetFileInfo(filePath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                }
                // 如果文件不存在，则根据扩展名获取图标
                else
                {
                    string ext = Path.GetExtension(filePath);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        string tempFileName = "temp" + ext;
                        SHGetFileInfo(tempFileName, FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf(shfi), flags | SHGFI_USEFILEATTRIBUTES);
                    }
                    else
                    {
                        return GetDefaultIcon(size);
                    }
                }

                if (shfi.hIcon == IntPtr.Zero)
                {
                    return GetDefaultIcon(size);
                }

                using (Icon icon = Icon.FromHandle(shfi.hIcon))
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // 释放图标句柄
                    DestroyIcon(shfi.hIcon);

                    if (bitmapSource == null)
                    {
                        return GetDefaultIcon(size);
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取文件图标失败: {ex.Message}");
                return GetDefaultIcon(size);
            }
        }

        /// <summary>
        /// 获取文件夹图标
        /// </summary>
        public static BitmapSource GetIconFromFolder(string folderPath)
        {
            return GetIconFromFolder(folderPath, DefaultIconSize);
        }

        /// <summary>
        /// 获取文件夹图标
        /// </summary>
        public static BitmapSource GetIconFromFolder(string folderPath, int size)
        {
            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_ICONLARGE;

                // 直接获取目录图标
                if (Directory.Exists(folderPath))
                {
                    SHGetFileInfo(folderPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                }
                // 目录不存在则获取通用目录图标
                else
                {
                    SHGetFileInfo("", FILE_ATTRIBUTE_DIRECTORY, ref shfi, (uint)Marshal.SizeOf(shfi), flags | SHGFI_USEFILEATTRIBUTES);
                }

                if (shfi.hIcon == IntPtr.Zero)
                {
                    return GetDefaultIcon(size);
                }

                using (Icon icon = Icon.FromHandle(shfi.hIcon))
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // 释放图标句柄
                    DestroyIcon(shfi.hIcon);

                    if (bitmapSource == null)
                    {
                        return GetDefaultIcon(size);
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取文件夹图标失败: {ex.Message}");
                return GetDefaultIcon(size);
            }
        }

        /// <summary>
        /// 获取系统图标
        /// </summary>
        public static BitmapSource GetSystemIcon(string path)
        {
            return GetSystemIcon(path, DefaultIconSize);
        }

        /// <summary>
        /// 获取系统图标
        /// </summary>
        public static BitmapSource GetSystemIcon(string path, int size)
        {
            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_ICONLARGE | SHGFI_USEFILEATTRIBUTES);

                if (shfi.hIcon == IntPtr.Zero)
                {
                    return GetDefaultIcon(size);
                }

                using (Icon icon = Icon.FromHandle(shfi.hIcon))
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    DestroyIcon(shfi.hIcon);

                    if (bitmapSource == null)
                    {
                        return GetDefaultIcon(size);
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取系统图标失败: {ex.Message}");
                return GetDefaultIcon(size);
            }
        }

        /// <summary>
        /// 获取默认图标
        /// </summary>
        public static BitmapSource GetDefaultIcon()
        {
            return GetDefaultIcon(DefaultIconSize);
        }

        /// <summary>
        /// 获取默认图标
        /// </summary>
        public static BitmapSource GetDefaultIcon(int size)
        {
            try
            {
                // 获取系统默认的未知文件类型图标
                SHFILEINFO shfi = new SHFILEINFO();
                SHGetFileInfo("unknown", FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_ICONLARGE | SHGFI_USEFILEATTRIBUTES);

                if (shfi.hIcon == IntPtr.Zero)
                {
                    // 如果获取失败，尝试获取系统文件图标
                    string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string defaultIconPath = Path.Combine(systemDir, "shell32.dll");

                    // 使用 shell32.dll 中的默认图标
                    SHGetFileInfo(defaultIconPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_ICONLARGE);

                    if (shfi.hIcon == IntPtr.Zero)
                    {
                        return null;
                    }
                }

                using (Icon icon = Icon.FromHandle(shfi.hIcon))
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    DestroyIcon(shfi.hIcon);

                    if (bitmapSource == null)
                    {
                        return null;
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取默认图标失败: {ex.Message}");
                return null;
            }
        }

        public static bool IsShortcutPath(string shortcutPath)
        {
            if (string.IsNullOrEmpty(shortcutPath))
            {
                return false;
            }

            string path = shortcutPath.ToLower();
            if (path.EndsWith(".lnk") || path.EndsWith(".url"))
            {
                return true;
            }
            return false;
        }
    }
}
