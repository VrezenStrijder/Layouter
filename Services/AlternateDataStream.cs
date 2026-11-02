using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Layouter.Services
{
    /// <summary>
    /// NTFS 备用数据流（Alternate Data Stream，简称 ADS）读写助手。
    /// <para>
    /// ADS 是 NTFS 的原生特性：可以在一个普通文件的"命名流"之外附加额外的命名数据流，
    /// 这些数据流在资源管理器中默认不可见，也不会占用目录项。本类用于把隐藏存储的
    /// 索引（原始路径映射）等敏感元数据写入备用数据流，从而避免其以普通文件形式暴露。
    /// </para>
    /// <para>
    /// 注意：ADS 仅在 NTFS 卷上可用。当卷不支持时，所有方法会返回失败，
    /// 调用方需要退回到普通文件的实现。
    /// </para>
    /// </summary>
    internal static class AlternateDataStream
    {
        private const int GENERIC_READ = unchecked((int)0x80000000);
        private const int GENERIC_WRITE = 0x40000000;
        private const int FILE_SHARE_READ = 0x00000001;
        private const int FILE_SHARE_WRITE = 0x00000002;
        private const int FILE_SHARE_DELETE = 0x00000004;
        private const int CREATE_ALWAYS = 2;
        private const int OPEN_EXISTING = 3;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;
        private const int FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const int INVALID_HANDLE_VALUE = -1;

        /// <summary>
        /// 备用数据流名不允许包含这些字符
        /// </summary>
        private static readonly char[] InvalidStreamChars = new[] { ':', '\\', '/', '*', '?', '"', '<', '>', '|' };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            int dwDesiredAccess,
            int dwShareMode,
            IntPtr lpSecurityAttributes,
            int dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(IntPtr hFile, long distance, out long newFilePointer, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceExW(string lpRootPathName, out long lpFreeBytesAvailableToCaller, out long lpTotalNumberOfBytes, out long lpTotalNumberOfFreeBytes);

        /// <summary>
        /// 判断流名是否合法
        /// </summary>
        public static bool IsValidStreamName(string streamName)
        {
            if (string.IsNullOrWhiteSpace(streamName))
            {
                return false;
            }

            if (streamName.IndexOfAny(InvalidStreamChars) >= 0)
            {
                return false;
            }

            return streamName.Length <= 255;
        }

        /// <summary>
        /// 拼接宿主文件路径与流名，形成 "path:stream" 形式的完整路径
        /// </summary>
        public static string BuildStreamPath(string hostFilePath, string streamName)
        {
            return $"{hostFilePath}:{streamName}";
        }

        /// <summary>
        /// 探测指定目录所在卷是否支持备用数据流
        /// </summary>
        public static bool IsSupportedInDirectory(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return false;
                }

                Directory.CreateDirectory(directory);

                string probeFile = Path.Combine(directory, ".ads_probe");
                byte[] payload = new byte[] { 0x4C, 0x41, 0x59 };

                bool written = TryWrite(probeFile, "probe", payload);
                bool verified = written && TryRead(probeFile, "probe") != null;

                TryDelete(probeFile, "probe");
                TryDeleteFile(probeFile);

                return verified;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 写入备用数据流（覆盖同名流）
        /// </summary>
        public static bool TryWrite(string hostFilePath, string streamName, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(hostFilePath) || !IsValidStreamName(streamName) || data == null)
            {
                return false;
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                // 确保宿主文件存在，否则 CreateFile 无法创建流
                string hostDirectory = Path.GetDirectoryName(hostFilePath);
                if (!string.IsNullOrEmpty(hostDirectory))
                {
                    Directory.CreateDirectory(hostDirectory);
                }

                if (!File.Exists(hostFilePath))
                {
                    using (File.Create(hostFilePath))
                    {
                    }
                }

                string streamPath = BuildStreamPath(hostFilePath, streamName);

                handle = CreateFileW(
                    streamPath,
                    GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    CREATE_ALWAYS,
                    FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
                    IntPtr.Zero);

                if (handle == IntPtr.Zero || handle.ToInt64() == INVALID_HANDLE_VALUE)
                {
                    handle = IntPtr.Zero;
                    return false;
                }

                int offset = 0;
                byte[] chunkBuffer = new byte[1024 * 1024];
                while (offset < data.Length)
                {
                    int chunk = Math.Min(chunkBuffer.Length, data.Length - offset);
                    Buffer.BlockCopy(data, offset, chunkBuffer, 0, chunk);

                    int written;
                    if (!WriteFile(handle, chunkBuffer, chunk, out written, IntPtr.Zero))
                    {
                        return false;
                    }

                    if (written <= 0)
                    {
                        return false;
                    }

                    offset += written;
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        /// <summary>
        /// 读取备用数据流，流不存在时返回 null
        /// </summary>
        public static byte[] TryRead(string hostFilePath, string streamName)
        {
            if (string.IsNullOrWhiteSpace(hostFilePath) || !IsValidStreamName(streamName))
            {
                return null;
            }

            if (!File.Exists(hostFilePath))
            {
                return null;
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                string streamPath = BuildStreamPath(hostFilePath, streamName);

                handle = CreateFileW(
                    streamPath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (handle == IntPtr.Zero || handle.ToInt64() == INVALID_HANDLE_VALUE)
                {
                    handle = IntPtr.Zero;
                    return null;
                }

                long size;
                if (!GetFileSizeEx(handle, out size))
                {
                    return null;
                }

                if (size <= 0)
                {
                    return new byte[0];
                }

                byte[] buffer = new byte[size];
                long position = 0;
                if (!SetFilePointerEx(handle, 0, out position, IntPtr.Zero))
                {
                    return null;
                }

                int totalRead = 0;
                byte[] chunkBuffer = new byte[1024 * 1024];
                while (totalRead < buffer.Length)
                {
                    int chunk = Math.Min(chunkBuffer.Length, buffer.Length - totalRead);
                    int read;
                    if (!ReadFile(handle, chunkBuffer, chunk, out read, IntPtr.Zero))
                    {
                        return null;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    Buffer.BlockCopy(chunkBuffer, 0, buffer, totalRead, read);
                    totalRead += read;
                }

                if (totalRead != buffer.Length)
                {
                    Array.Resize(ref buffer, totalRead);
                }

                return buffer;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
        }

        /// <summary>
        /// 删除备用数据流
        /// </summary>
        public static bool TryDelete(string hostFilePath, string streamName)
        {
            if (string.IsNullOrWhiteSpace(hostFilePath) || !IsValidStreamName(streamName))
            {
                return false;
            }

            if (!File.Exists(hostFilePath))
            {
                return false;
            }

            try
            {
                return DeleteFileW(BuildStreamPath(hostFilePath, streamName));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 删除宿主文件（同时会清除其上的所有数据流）
        /// </summary>
        public static bool TryDeleteFile(string hostFilePath)
        {
            try
            {
                if (File.Exists(hostFilePath))
                {
                    File.Delete(hostFilePath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
