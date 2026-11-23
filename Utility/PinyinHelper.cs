using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Layouter.Utility
{
    /// <summary>
    /// 汉字转拼音辅助。
    /// <para>
    /// 拼音对照表随程序一起分发（<c>Resources/pinyin_table.bin</c>，
    /// 原始 deflate 压缩），首次使用时才解压载入，运行期不依赖任何外部组件。
    /// 表中每个汉字对应一到多个读音，多音字保留全部读音以便匹配。
    /// </para>
    /// </summary>
    public static class PinyinHelper
    {
        private static readonly object syncRoot = new object();

        /// <summary>
        /// 汉字 -> 读音列表（无声调，如「行」对应 xing / hang）
        /// </summary>
        private static Dictionary<char, string[]>? readings;

        /// <summary>
        /// 拼音表是否已载入
        /// </summary>
        public static bool IsLoaded => readings != null;

        /// <summary>
        /// 确保拼音表已载入（线程安全，只载入一次）
        /// </summary>
        private static bool EnsureLoaded()
        {
            if (readings != null)
            {
                return readings.Count > 0;
            }

            lock (syncRoot)
            {
                if (readings != null)
                {
                    return readings.Count > 0;
                }

                try
                {
                    readings = LoadTable();
                }
                catch (Exception ex)
                {
                    Log.Information($"加载拼音表失败: {ex.Message}");
                    readings = new Dictionary<char, string[]>();
                }

                return readings.Count > 0;
            }
        }

        /// <summary>
        /// 从嵌入资源中解压并解析拼音表
        /// </summary>
        private static Dictionary<char, string[]> LoadTable()
        {
            var map = new Dictionary<char, string[]>();

            Assembly assembly = typeof(PinyinHelper).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("pinyin_table.bin", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Log.Information("未找到拼音表资源，拼音搜索不可用");
                return map;
            }

            Stream? resource = assembly.GetManifestResourceStream(resourceName);
            if (resource == null)
            {
                return map;
            }

            using (resource)
            using (var deflate = new DeflateStream(resource, CompressionMode.Decompress))
            using (var reader = new StreamReader(deflate, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    // 格式：汉字:读音1,读音2
                    if (line.Length < 3 || line[1] != ':')
                    {
                        continue;
                    }

                    char ch = line[0];
                    string[] parts = line.Substring(2).Split(',');
                    var valid = parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
                    if (valid.Length > 0)
                    {
                        map[ch] = valid;
                    }
                }
            }

            Log.Information($"已载入拼音表，共 {map.Count} 个汉字");
            return map;
        }

        /// <summary>
        /// 取单个汉字的读音
        /// </summary>
        /// <param name="ch">汉字</param>
        /// <returns>读音列表；非汉字或无读音时返回空数组</returns>
        public static string[] GetReadings(char ch)
        {
            if (!EnsureLoaded())
            {
                return Array.Empty<string>();
            }

            Dictionary<char, string[]>? table = readings;
            if (table == null)
            {
                return Array.Empty<string>();
            }

            return table.TryGetValue(ch, out string[]? values) ? values : Array.Empty<string>();
        }

        /// <summary>
        /// 判断字符是否为汉字
        /// </summary>
        public static bool IsChinese(char ch)
        {
            return ch >= '\u4E00' && ch <= '\u9FFF';
        }

        /// <summary>
        /// 文本中是否含有汉字
        /// </summary>
        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.Any(IsChinese);
        }

        /// <summary>
        /// 取整段文本的拼音（多音字取第一个读音，即最常用的读法）
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>拼音串；非汉字原样保留</returns>
        public static string GetPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length * 4);

            foreach (char ch in text)
            {
                if (!IsChinese(ch))
                {
                    builder.Append(ch);
                    continue;
                }

                string[] values = GetReadings(ch);
                builder.Append(values.Length > 0 ? values[0] : string.Empty);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 取整段文本的拼音首字母（微信 -> wx）
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>首字母串；非汉字原样保留</returns>
        public static string GetInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);

            foreach (char ch in text)
            {
                if (!IsChinese(ch))
                {
                    builder.Append(ch);
                    continue;
                }

                string[] values = GetReadings(ch);
                if (values.Length > 0 && values[0].Length > 0)
                {
                    builder.Append(values[0][0]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 文本中所有汉字读音的全集（用于「以拼音开头」这类模糊匹配）
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>读音集合</returns>
        public static HashSet<string> GetAllReadings(string text)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            foreach (char ch in text)
            {
                if (!IsChinese(ch))
                {
                    continue;
                }

                foreach (string value in GetReadings(ch))
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }
    }
}
