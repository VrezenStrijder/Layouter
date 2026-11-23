using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Layouter.Services;

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

        /// <summary>
        /// 快捷方式 COM 对象（ShellLink）
        /// </summary>
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass
        {
        }

        /// <summary>
        /// 快捷方式接口。方法顺序必须与原生虚表一致，不可调整。
        /// </summary>
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        /// <summary>
        /// 把文件读入 COM 对象（快捷方式需要用它来载入 .lnk）
        /// </summary>
        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        #endregion

        #region 缓存

        /// <summary>
        /// 图标缓存：键为「路径 + 边长」，避免重复调用 Shell 接口造成卡顿。
        /// </summary>
        private static readonly ConcurrentDictionary<string, BitmapSource> iconCache = new ConcurrentDictionary<string, BitmapSource>();

        /// <summary>
        /// 系统默认（通用文件）图标缓存，按尺寸。用于判断提取结果是否只是通用图标。
        /// </summary>
        private static readonly ConcurrentDictionary<int, BitmapSource> defaultIconCache = new ConcurrentDictionary<int, BitmapSource>();

        /// <summary>
        /// 快捷方式目标路径缓存（解析一次即可，避免反复调用 COM）
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> shortcutTargetCache = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// 快捷方式图标来源缓存：快捷方式路径 → 图标来源文件
        /// </summary>
        /// <remarks>
        /// 快捷方式被拖进分区后，文件本身会被搬进「安全隐藏存储」，
        /// 原来的路径随即失效。此时如果再去解析快捷方式就取不到图标了。
        /// 这里把「图标来源」提前解析并缓存下来：来源是快捷方式自带的图标文件
        /// 或它指向的目标程序，这两个文件都不会随快捷方式搬家，
        /// 因此无论快捷方式搬到哪里，图标都能正确取到。
        /// </remarks>
        private static readonly ConcurrentDictionary<string, string> shortcutIconSourceCache = new ConcurrentDictionary<string, string>();

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
        /// <remarks>
        /// 这里使用原生 <c>IShellLinkW</c> 而不是 <c>WScript.Shell</c>：
        /// 后者每次都要创建一个脚本宿主 COM 对象，实测单次解析约 5ms，
        /// 而 <c>IShellLink</c> 约 2ms 且结果完全一致。
        /// 分区里几十个快捷方式同时解析时，这个差距会被放大成明显的启动卡顿。
        /// </remarks>
        public static string ResolveShortcut(string shortcutPath)
        {
            try
            {
                if (string.IsNullOrEmpty(shortcutPath))
                {
                    return string.Empty;
                }

                // 同一个快捷方式会被反复解析（渲染、磁盘缓存判定都会用到），结果缓存一下
                if (shortcutTargetCache.TryGetValue(shortcutPath, out string? cachedTarget))
                {
                    return cachedTarget;
                }

                string target;

                // 网络快捷方式（.url）不是 ShellLink 格式，
                // 用 IShellLink 载入会直接失败，需要单独解析文本内容
                if (shortcutPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    target = ResolveInternetShortcut(shortcutPath);
                }
                else
                {
                    var link = (IShellLinkW)new ShellLinkCoClass();
                    ((IPersistFile)link).Load(shortcutPath, 0);
                    link.Resolve(IntPtr.Zero, 0);

                    var buffer = new StringBuilder(1024);
                    link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);

                    target = buffer.ToString();

                    // 目标路径里可能带有环境变量（如 %SystemRoot%\...），需要展开后再使用
                    if (!string.IsNullOrEmpty(target))
                    {
                        target = Environment.ExpandEnvironmentVariables(target);
                    }
                }

                shortcutTargetCache[shortcutPath] = target;
                return target;
            }
            catch (Exception ex)
            {
                Log.Information($"解析快捷方式失败({shortcutPath}): {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 解析快捷方式的图标来源文件
        /// </summary>
        /// <remarks>
        /// 资源管理器显示快捷方式图标时，优先使用快捷方式自身记录的图标位置
        /// （<c>IShellLink.GetIconLocation</c>），没有记录时才退回目标程序的图标。
        /// 这里保持同样的顺序，保证分区里的图标和桌面上的图标长得一样。
        /// <para>
        /// 解析结果会被缓存下来：快捷方式被搬进「安全隐藏存储」后原路径随即失效，
        /// 但缓存里的来源文件依然有效，图标不会因此变成空白。
        /// </para>
        /// </remarks>
        /// <param name="shortcutPath">快捷方式路径</param>
        /// <returns>图标来源文件路径；无法解析时返回空字符串</returns>
        public static string ResolveShortcutIconSource(string shortcutPath)
        {
            if (string.IsNullOrEmpty(shortcutPath))
            {
                return string.Empty;
            }

            if (shortcutIconSourceCache.TryGetValue(shortcutPath, out string cachedSource))
            {
                return cachedSource;
            }

            string source = string.Empty;

            try
            {
                // 网络快捷方式指向的是网址，没有可用的图标文件
                if (!shortcutPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    var link = (IShellLinkW)new ShellLinkCoClass();
                    ((IPersistFile)link).Load(shortcutPath, 0);

                    // 1) 快捷方式自带的图标位置
                    var iconLocation = new StringBuilder(1024);
                    int iconIndex = 0;
                    link.GetIconLocation(iconLocation, iconLocation.Capacity, out iconIndex);

                    source = NormalizeIconLocationPath(iconLocation.ToString());

                    // 2) 退回快捷方式指向的目标程序
                    if (source.Length == 0)
                    {
                        var buffer = new StringBuilder(1024);
                        link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);

                        string target = Environment.ExpandEnvironmentVariables(buffer.ToString().Trim().Trim('"'));
                        if (File.Exists(target) || Directory.Exists(target))
                        {
                            source = target;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"解析快捷方式图标来源失败({shortcutPath}): {ex.Message}");
            }

            if (source.Length > 0)
            {
                shortcutIconSourceCache[shortcutPath] = source;
            }

            return source;
        }

        /// <summary>
        /// 规整图标位置字符串（去引号、去索引后缀、展开环境变量）
        /// </summary>
        private static string NormalizeIconLocationPath(string iconLocation)
        {
            if (string.IsNullOrWhiteSpace(iconLocation))
            {
                return string.Empty;
            }

            string value = iconLocation.Trim().Trim('"');
            value = Environment.ExpandEnvironmentVariables(value);

            // 图标位置可能带有索引后缀（例如 app.exe,3）
            int commaIndex = value.LastIndexOf(',');
            if (commaIndex > 0)
            {
                string candidate = value.Substring(0, commaIndex).Trim();
                if (candidate.Length > 0 && (File.Exists(candidate) || Directory.Exists(candidate)))
                {
                    value = candidate;
                }
            }

            if (File.Exists(value) || Directory.Exists(value))
            {
                return value;
            }

            return string.Empty;
        }

        /// <summary>
        /// 解析网络快捷方式（.url 文件）
        /// </summary>
        /// <remarks>
        /// .url 是一个文本文件，里面用 <c>URL=...</c> 记录真正的地址。
        /// </remarks>
        private static string ResolveInternetShortcut(string urlFilePath)
        {
            try
            {
                if (!File.Exists(urlFilePath))
                {
                    return string.Empty;
                }

                foreach (string line in File.ReadAllLines(urlFilePath))
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring(4).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"解析网络快捷方式失败({urlFilePath}): {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// 批量预热图标缓存
        /// </summary>
        /// <remarks>
        /// 从系统中提取图标（<c>SHGetFileInfo</c> + <c>IImageList</c>）非常慢，
        /// 单个图标可达上百毫秒。串行执行时，启动阶段几十个图标就会把界面卡住数秒。
        /// 这里放到后台线程并行执行，界面线程在此期间完全空闲，
        /// 缓存热了之后首次渲染就不会再卡。
        /// </remarks>
        /// <param name="items">图标路径与尺寸的组合</param>
        public static void WarmupIconCache(IEnumerable<(string Path, int Size)> items)
        {
            if (items == null)
            {
                return;
            }

            try
            {
                var list = items
                    .Where(i => !string.IsNullOrEmpty(i.Path))
                    .GroupBy(i => (i.Path, i.Size))
                    .Select(g => g.Key)
                    .ToList();

                if (list.Count == 0)
                {
                    return;
                }

                int degree = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
                Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = degree }, item =>
                {
                    try
                    {
                        GetSuitableIcon(item.Path, item.Size);
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"预热图标缓存失败({item.Path}): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Information($"预热图标缓存时出错: {ex.Message}");
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
                    ? GetIconFromFolderCore(targetPath, size)
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
        /// 按快捷方式的图标来源提取图标
        /// </summary>
        /// <remarks>
        /// 图标来源优先取快捷方式自带的图标位置（与资源管理器一致），
        /// 其次取它指向的目标程序。
        /// </remarks>
        private static bool TryGetShortcutIconSourceBitmap(string shortcutPath, int size, out BitmapSource source)
        {
            source = null;

            try
            {
                string iconSource = ResolveShortcutIconSource(shortcutPath);
                if (string.IsNullOrEmpty(iconSource))
                {
                    return false;
                }

                // 图标来源本身就是 ICO 文件时直接按图标文件读取。
                // MSI 安装器生成的 ProductIcon 没有扩展名、内容却是标准 ICO，
                // Shell 只按扩展名识别，SHGetFileInfo 会返回通用文件图标。
                if (TryGetIconFileBitmap(iconSource, size, out BitmapSource viaIconFile))
                {
                    source = viaIconFile;
                    return true;
                }

                BitmapSource? candidate;

                if (TryGetImageListBitmap(iconSource, size, out BitmapSource viaImageList))
                {
                    candidate = viaImageList;
                }
                else
                {
                    candidate = Directory.Exists(iconSource)
                        ? GetIconFromFolderCore(iconSource, size)
                        : GetIconFromFileCore(iconSource, size);
                }

                if (candidate == null)
                {
                    return false;
                }

                // 取到的仍是系统通用文件图标时视为失败，
                // 让调用方回退到快捷方式指向的目标程序，避免显示一片空白。
                if (IsGenericFileIcon(candidate, size))
                {
                    return false;
                }

                source = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"获取快捷方式图标来源失败({shortcutPath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 直接从 ICO 文件读取图标（不依赖文件扩展名）
        /// </summary>
        /// <remarks>
        /// MSI 安装器生成的 <c>ProductIcon</c> 是标准 ICO 内容却没有扩展名，
        /// Shell 无法按扩展名识别，<c>SHGetFileInfo</c> 只会返回通用文件图标。
        /// 这里先读文件头确认格式，再直接交给 <see cref="Icon"/> 读取。
        /// </remarks>
        private static bool TryGetIconFileBitmap(string path, int targetSize, out BitmapSource source)
        {
            source = null;

            try
            {
                if (!File.Exists(path) || !IsIconFile(path))
                {
                    return false;
                }

                using (var icon = new Icon(path, targetSize, targetSize))
                {
                    BitmapSource raw = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    if (raw == null)
                    {
                        return false;
                    }

                    raw.Freeze();
                    source = ScaleTo(raw, targetSize);
                    return source != null;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"直接读取 ICO 文件失败({path}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断文件是否为 ICO 格式（读取文件头）
        /// </summary>
        private static bool IsIconFile(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[4];
                if (stream.Read(header) < 4)
                {
                    return false;
                }

                // ICO 文件头固定为 00 00 01 00
                return header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断图标是否与系统「通用文件图标」完全一致
        /// </summary>
        private static bool IsGenericFileIcon(BitmapSource candidate, int size)
        {
            try
            {
                if (candidate == null)
                {
                    return true;
                }

                BitmapSource? generic = GetDefaultIcon(size);
                return generic != null && ArePixelsEqual(candidate, generic);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 逐像素比较两张位图是否完全一致
        /// </summary>
        private static bool ArePixelsEqual(BitmapSource left, BitmapSource right)
        {
            try
            {
                var leftConverted = new FormatConvertedBitmap(left, PixelFormats.Bgra32, null, 0);
                var rightConverted = new FormatConvertedBitmap(right, PixelFormats.Bgra32, null, 0);

                if (leftConverted.PixelWidth != rightConverted.PixelWidth ||
                    leftConverted.PixelHeight != rightConverted.PixelHeight)
                {
                    return false;
                }

                int stride = leftConverted.PixelWidth * 4;
                byte[] leftPixels = new byte[stride * leftConverted.PixelHeight];
                byte[] rightPixels = new byte[stride * rightConverted.PixelHeight];

                leftConverted.CopyPixels(leftPixels, stride, 0);
                rightConverted.CopyPixels(rightPixels, stride, 0);

                return leftPixels.AsSpan().SequenceEqual(rightPixels);
            }
            catch
            {
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

            // 磁盘缓存：图标提取非常慢（单个可达上百毫秒），
            // 图标来源没变时直接读缓存，启动时间能压到一秒以内
            string cacheSource = ResolveCacheSource(filePath);
            if (cacheSource != null && IconDiskCache.TryGet(cacheSource, targetSize, out BitmapSource? diskCached))
            {
                iconCache[cacheKey] = diskCached;
                return diskCached;
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
                // 提取失败时只返回占位图标，绝不写进任何缓存。
                // 否则一次失败就会把「空白图标」当成正式结果缓存下来，
                // 之后每次启动都直接读到它，这个图标就再也恢复不了。
                Log.Information($"未能提取到图标，改用占位图标: {filePath}");
                return GetDefaultIcon(targetSize);
            }

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

            if (cacheSource != null)
            {
                IconDiskCache.Save(cacheSource, targetSize, source);
            }

            return source;
        }

        /// <summary>
        /// 取得图标缓存的来源文件
        /// </summary>
        /// <remarks>
        /// 快捷方式的图标实际来自它自身记录的图标位置，或者它指向的目标程序，
        /// 因此缓存要以这个「图标来源」为准：来源换了缓存才会失效。
        /// 另外，快捷方式被搬进隐藏存储后路径会变，来源文件却不会变，
        /// 用来源文件做缓存键可以让搬前搬后共用同一份缓存。
        /// 网络快捷方式指向的是网址而不是文件，不参与磁盘缓存。
        /// </remarks>
        private static string? ResolveCacheSource(string filePath)
        {
            try
            {
                if (!IsShortcutPath(filePath))
                {
                    return File.Exists(filePath) ? filePath : null;
                }

                if (filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string iconSource = ResolveShortcutIconSource(filePath);
                return File.Exists(iconSource) ? iconSource : null;
            }
            catch
            {
                return null;
            }
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
            // 1) 快捷方式：优先使用快捷方式自带的图标位置（与资源管理器一致），
            //    这样分区里的图标和桌面上的图标完全相同；
            //    没有记录图标位置时才使用目标程序的图标。
            if (IsShortcutPath(filePath))
            {
                if (TryGetShortcutIconSourceBitmap(filePath, size, out BitmapSource viaShortcutSource))
                {
                    return viaShortcutSource;
                }

                if (TryGetShortcutTargetIcon(filePath, size, out BitmapSource viaShortcutTarget))
                {
                    return viaShortcutTarget;
                }
            }

            // 2) 文件本身是 ICO（含无扩展名的 MSI ProductIcon）：直接按内容读取。
            //    Shell 对没有扩展名的文件只会按扩展名给出通用文件图标。
            if (TryGetIconFileBitmap(filePath, size, out BitmapSource viaIconFile))
            {
                return viaIconFile;
            }

            // 3) 优先使用系统图像列表，可拿到原生高分辨率图标。
            //    Shell 只给出通用文件图标时说明没识别出文件内容，继续往下尝试。
            if (TryGetImageListBitmap(filePath, size, out BitmapSource viaImageList)
                && !IsGenericFileIcon(viaImageList, size))
            {
                return viaImageList;
            }

            // 4) 目录
            if (Directory.Exists(filePath))
            {
                return GetIconFromFolderCore(filePath, size);
            }

            // 5) 普通文件
            if (File.Exists(filePath))
            {
                BitmapSource viaFile = GetIconFromFileCore(filePath, size);
                if (viaFile != null && !IsGenericFileIcon(viaFile, size))
                {
                    return viaFile;
                }
            }

            // 6) Shell 命名空间路径
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

                // 路径真实存在时必须让 Shell 去读文件本身的图标资源。
                // 如果对存在的文件也传 SHGFI_USEFILEATTRIBUTES，Shell 会忽略文件内容，
                // 只按扩展名返回通用图标：MSI 生成的 ProductIcon（无扩展名、实为 ICO）
                // 就会变成一片空白的通用文件图标；而且因为「取到了图标」，
                // 后续不会再回退到快捷方式指向的目标程序，图标就此永久丢失。
                // 只有路径不存在时，才用 USEFILEATTRIBUTES 按扩展名兜底。
                bool exists = File.Exists(path) || Directory.Exists(path);
                uint flags = SHGFI_SYSICONINDEX | (exists ? 0u : SHGFI_USEFILEATTRIBUTES);
                uint attributes = exists ? 0u : FILE_ATTRIBUTE_NORMAL;

                IntPtr res = SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);

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

            return GetIconFromFileCore(filePath, size) ?? GetDefaultIcon(size);
        }

        /// <summary>
        /// 获取文件图标（不解析快捷方式）
        /// </summary>
        /// <remarks>
        /// 提取失败时返回 null，由调用方决定如何兜底，
        /// 这样调用方才能区分「真的没有图标」和「取到了占位图标」，
        /// 避免把占位图标当成正式结果缓存下来。
        /// </remarks>
        private static BitmapSource GetIconFromFileCore(string filePath, int size)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
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
                        return null;
                    }
                }

                if (shfi.hIcon == IntPtr.Zero)
                {
                    return null;
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
                        return null;
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取文件图标失败: {ex.Message}");
                return null;
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
            return GetIconFromFolderCore(folderPath, size) ?? GetDefaultIcon(size);
        }

        /// <summary>
        /// 获取文件夹图标（提取失败时返回 null）
        /// </summary>
        private static BitmapSource GetIconFromFolderCore(string folderPath, int size)
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
                    return null;
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
                        return null;
                    }

                    bitmapSource.Freeze();
                    return ScaleTo(bitmapSource, NormalizeSize(size));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"获取文件夹图标失败: {ex.Message}");
                return null;
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
            int targetSize = NormalizeSize(size);

            if (defaultIconCache.TryGetValue(targetSize, out BitmapSource? cached))
            {
                return cached;
            }

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
                    BitmapSource result = ScaleTo(bitmapSource, targetSize);

                    if (result != null)
                    {
                        defaultIconCache[targetSize] = result;
                    }

                    return result;
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
