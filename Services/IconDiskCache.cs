using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Layouter.Services
{
    /// <summary>
    /// 图标磁盘缓存
    /// </summary>
    /// <remarks>
    /// 从系统里提取图标位图非常昂贵（每个图标几十到上百毫秒），
    /// 分区里几十个图标就能让启动卡住好几秒。
    /// 这里把提取结果按「来源文件 + 尺寸」缓存到磁盘，
    /// 后续启动直接读缓存文件，启动时间可以降到一秒以内。
    /// <para>
    /// 有效性判断采用「缓存文件的最后写入时间 = 来源文件的最后写入时间」，
    /// 来源文件变了缓存自然失效，不需要额外的索引文件。
    /// 任何异常都按「未命中」处理，最坏情况只是退回实时提取。
    /// </para>
    /// </remarks>
    public static class IconDiskCache
    {
        private const int MaxCacheFiles = 4000;

        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Layouter",
            "IconCache");

        /// <summary>
        /// 读取缓存的图标
        /// </summary>
        /// <param name="sourcePath">图标来源文件（快捷方式取其目标文件）</param>
        /// <param name="size">图标边长</param>
        /// <param name="source">缓存的图标</param>
        /// <returns>是否命中缓存</returns>
        public static bool TryGet(string sourcePath, int size, out BitmapSource? source)
        {
            source = null;

            try
            {
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                {
                    return false;
                }

                string cacheFile = GetCacheFilePath(sourcePath, size);
                if (!File.Exists(cacheFile))
                {
                    return false;
                }

                DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
                DateTime cacheTime = File.GetLastWriteTimeUtc(cacheFile);

                // 允许 1 秒的误差，避免文件系统时间精度导致缓存频繁失效
                if (Math.Abs((cacheTime - sourceTime).TotalSeconds) > 1)
                {
                    return false;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(cacheFile);
                bitmap.EndInit();
                bitmap.Freeze();

                source = bitmap;
                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"读取图标磁盘缓存失败({sourcePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 写入图标缓存
        /// </summary>
        /// <param name="sourcePath">图标来源文件</param>
        /// <param name="size">图标边长</param>
        /// <param name="source">图标位图</param>
        public static void Save(string sourcePath, int size, BitmapSource? source)
        {
            if (source == null || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(CacheDirectory);

                string cacheFile = GetCacheFilePath(sourcePath, size);
                string tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));

                using (FileStream stream = File.Create(tempFile))
                {
                    encoder.Save(stream);
                }

                // 用来源文件的修改时间标记缓存有效性
                File.SetLastWriteTimeUtc(tempFile, File.GetLastWriteTimeUtc(sourcePath));

                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }

                File.Move(tempFile, cacheFile);
            }
            catch (Exception ex)
            {
                Log.Information($"写入图标磁盘缓存失败({sourcePath}): {ex.Message}");
            }
        }

        /// <summary>
        /// 清理磁盘缓存
        /// </summary>
        public static void Clear()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"清理图标磁盘缓存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算缓存文件路径
        /// </summary>
        /// <remarks>
        /// 缓存键里带一个版本号：图标提取逻辑调整后（例如 2.8.1 改为优先使用
        /// 快捷方式自带的图标位置），旧版本的缓存需要整体作废，
        /// 加上版本号即可让旧缓存自然失效，不必手工清理缓存目录。
        /// <para>
        /// v3：修复「存在的文件也传 SHGFI_USEFILEATTRIBUTES」导致
        /// 无扩展名图标文件（MSI ProductIcon）被缓存成通用空白图标的问题。
        /// v4：改为直接按 ICO 内容读取，并增加「取到通用图标则回退目标程序」的兜底，
        /// 需要让 v3 期间可能写入的通用图标缓存整体失效。
        /// </para>
        /// </remarks>
        private static string GetCacheFilePath(string sourcePath, int size)
        {
            string key = $"v4|{size}|{sourcePath}";
            string hash = ComputeHash(key);
            return Path.Combine(CacheDirectory, hash + ".png");
        }

        private static string ComputeHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// 缓存文件过多时清理最旧的一批
        /// </summary>
        public static void TrimIfNeeded()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    return;
                }

                var files = new DirectoryInfo(CacheDirectory).GetFiles("*.png");
                if (files.Length <= MaxCacheFiles)
                {
                    return;
                }

                foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxCacheFiles))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // 忽略单个文件删除失败
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"清理过期图标缓存失败: {ex.Message}");
            }
        }
    }
}
