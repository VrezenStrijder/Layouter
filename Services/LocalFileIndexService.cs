using System.IO;
using System.Text;
using Layouter.Models;

namespace Layouter.Services
{
    /// <summary>
    /// 本地全盘文件名索引
    /// </summary>
    /// <remarks>
    /// <para>
    /// 早期版本把「全文件系统搜索」直接交给 Windows 搜索索引（WDS），但实测在本机
    /// 有两个无法回避的问题：
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// 索引覆盖不全：<c>D:\Program Files\Lunacy\Lunacy.exe</c> 这类真实存在的文件
    /// 并不在索引里（资源管理器能搜到，是因为它对未索引位置会退化为全盘扫描）。
    /// </description></item>
    /// <item><description>
    /// 稀有关键字的 <c>LIKE</c> 查询会超时：<c>LIKE '%wb2api%'</c> 直接返回
    /// <c>0x80041607</c>，要等 10~30 秒才失败。
    /// </description></item>
    /// </list>
    /// <para>
    /// 因此改为自己枚举各固定盘的文件名，按盘落盘缓存
    /// （<c>%LOCALAPPDATA%\Layouter\fileindex\&lt;盘符&gt;.idx</c>），
    /// 查询时在内存里做字节级子串匹配：覆盖完整、响应在百毫秒级。
    /// 缓存超过 <see cref="MaxIndexAge"/> 或不存在时会在后台重建，
    /// 期间搜索照常可用（用已有索引）。
    /// </para>
    /// </remarks>
    public class LocalFileIndexService
    {
        public static LocalFileIndexService Instance { get; } = new LocalFileIndexService();

        /// <summary>单个盘最多收集的候选条数，避免海量命中撑爆内存</summary>
        private const int MaxMatchesPerDrive = 80;

        /// <summary>索引缓存的有效期，过期后在后台重建</summary>
        private static readonly TimeSpan MaxIndexAge = TimeSpan.FromHours(24);

        /// <summary>索引文件首行的标记，用于识别文件格式</summary>
        private const string HeaderPrefix = "BUILD|";

        private readonly object sync = new object();

        /// <summary>已加载到内存的索引（盘符 → UTF-8 文本行）</summary>
        private readonly Dictionary<char, byte[]> driveBuffers = new Dictionary<char, byte[]>();

        private Task? buildTask;
        private volatile bool isBuilding;

        private LocalFileIndexService()
        {
        }

        /// <summary>是否正在后台建立索引</summary>
        public bool IsBuilding => isBuilding;

        /// <summary>是否已有可用索引（内存已加载，或磁盘上有索引文件）</summary>
        public bool HasIndex
        {
            get
            {
                lock (sync)
                {
                    if (driveBuffers.Count > 0)
                    {
                        return true;
                    }
                }

                try
                {
                    return Directory.Exists(IndexDirectory) &&
                           Directory.EnumerateFiles(IndexDirectory, "*.idx").Any();
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>已加载的盘数量</summary>
        public int IndexedDriveCount
        {
            get
            {
                lock (sync)
                {
                    return driveBuffers.Count;
                }
            }
        }

        /// <summary>索引缓存目录</summary>
        public static string IndexDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Layouter",
            "fileindex");

        /// <summary>
        /// 取一份索引状态快照（供设置面板展示）
        /// </summary>
        public FileIndexSummary GetSummary()
        {
            var drives = new List<FileIndexStatus>();
            long total = 0;
            DateTime? latest = null;
            bool any = false;

            foreach (char letter in GetFixedDriveLetters())
            {
                string path = GetIndexPath(letter);
                long size = 0;
                DateTime? builtAt = null;

                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        size = info.Length;
                        builtAt = info.LastWriteTimeUtc;
                        any = true;
                        total += size;

                        if (latest == null || builtAt > latest)
                        {
                            latest = builtAt;
                        }
                    }
                }
                catch
                {
                    // 单个盘读不到（权限、被占用）就当没有，不影响其他盘
                }

                drives.Add(new FileIndexStatus(letter, size, builtAt));
            }

            return new FileIndexSummary(isBuilding, any, total, latest, drives);
        }

        /// <summary>
        /// 启动（或继续）后台建库；已有任务进行中时直接返回
        /// </summary>
        public void StartEnsure()
        {
            lock (sync)
            {
                if (buildTask != null && !buildTask.IsCompleted)
                {
                    return;
                }

                buildTask = Task.Run(EnsureAll);
            }
        }

        /// <summary>
        /// 丢弃现有索引并重新建库
        /// </summary>
        /// <returns>
        /// 已开始重建返回 <c>true</c>；正在建库中、无法打断时返回 <c>false</c>
        /// </returns>
        public bool Rebuild()
        {
            // 正在建库时不打断：删目录会让进行中的写入失败，等这轮结束再重建即可
            if (isBuilding)
            {
                return false;
            }

            lock (sync)
            {
                driveBuffers.Clear();
            }

            try
            {
                if (Directory.Exists(IndexDirectory))
                {
                    Directory.Delete(IndexDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"清理文件索引缓存失败: {ex.Message}");
            }

            StartEnsure();
            return true;
        }

        /// <summary>
        /// 执行搜索（应在后台线程调用）
        /// </summary>
        /// <param name="query">关键字</param>
        /// <param name="maxResults">最多返回条数</param>
        public List<SearchResultItem> Search(string query, int maxResults = 30)
        {
            var results = new List<SearchResultItem>();

            string term = (query ?? string.Empty).Trim();
            if (term.Length < GeneralSettingsService.Instance.GetMinSearchLength())
            {
                return results;
            }

            string lowerTerm = term.ToLowerInvariant();
            byte[] needle = Encoding.UTF8.GetBytes(lowerTerm);
            int[] skip = BuildSkipTable(needle);
            var candidates = new List<SearchResultItem>();

            foreach (char letter in GetFixedDriveLetters())
            {
                byte[]? buffer = GetBuffer(letter);
                if (buffer == null)
                {
                    continue;
                }

                ScanBuffer(buffer, needle, skip, lowerTerm, candidates);
            }

            if (candidates.Count == 0)
            {
                return results;
            }

            return SelectBalanced(candidates, maxResults);
        }

        /// <summary>
        /// 逐盘检查缓存，缺失或过期就重建
        /// </summary>
        private void EnsureAll()
        {
            isBuilding = true;
            try
            {
                foreach (char letter in GetFixedDriveLetters())
                {
                    try
                    {
                        string path = GetIndexPath(letter);
                        if (IsFresh(path))
                        {
                            continue;
                        }

                        BuildDrive(letter, path);
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"建立文件索引失败({letter}): {ex.Message}");
                    }
                }
            }
            finally
            {
                isBuilding = false;
            }
        }

        /// <summary>
        /// 是否属于不值得收录的目录（系统硬链接堆、依赖缓存、回收站等）
        /// </summary>
        private static bool IsExcludedPath(string path)
        {
            return path.Contains(@"\WinSxS\", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(@"\$RECYCLE.BIN\", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(@"\System Volume Information\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFresh(string path)        {
            try
            {
                return File.Exists(path) &&
                       DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MaxIndexAge;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 枚举整盘文件并写入索引文件
        /// </summary>
        /// <remarks>
        /// 先写临时文件再替换，避免建到一半的索引被搜索读到。
        /// 跳过重解析点（junction / 符号链接），否则容易绕圈。
        /// </remarks>
        private static void BuildDrive(char letter, string targetPath)
        {
            Directory.CreateDirectory(IndexDirectory);

            string tempPath = targetPath + ".tmp";
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false
            };

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 20))
                {
                    writer.WriteLine(HeaderPrefix + DateTime.UtcNow.ToString("O"));

                    foreach (string file in Directory.EnumerateFiles(letter + ":\\", "*", options))
                    {
                        // WinSxS 之类是硬链接堆，node_modules / .git 是依赖缓存，
                        // 回收站与系统卷信息是垃圾；收录它们只会把索引撑大几十 MB，
                        // 搜出来也没有意义。
                        if (IsExcludedPath(file))
                        {
                            continue;
                        }

                        writer.WriteLine(file);
                    }
                }

                File.Move(tempPath, targetPath, true);
                Log.Information($"文件索引已建立: {letter}:");
            }
            catch (Exception ex)
            {
                Log.Information($"构建文件索引失败({letter}): {ex.Message}");

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // 忽略清理失败
                }
            }
        }

        /// <summary>
        /// 取某盘的索引内容，首次访问时从磁盘加载
        /// </summary>
        private byte[]? GetBuffer(char letter)
        {
            lock (sync)
            {
                if (driveBuffers.TryGetValue(letter, out byte[]? cached))
                {
                    return cached;
                }
            }

            byte[]? buffer = LoadBuffer(letter);
            if (buffer == null)
            {
                return null;
            }

            lock (sync)
            {
                driveBuffers[letter] = buffer;
            }

            return buffer;
        }

        private static byte[]? LoadBuffer(char letter)
        {
            string path = GetIndexPath(letter);

            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                byte[] data = File.ReadAllBytes(path);
                if (data.Length < HeaderPrefix.Length)
                {
                    return null;
                }

                for (int i = 0; i < HeaderPrefix.Length; i++)
                {
                    if (data[i] != (byte)HeaderPrefix[i])
                    {
                        return null;
                    }
                }

                return data;
            }
            catch (Exception ex)
            {
                Log.Information($"读取文件索引失败({letter}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 在索引内容里做子串匹配
        /// </summary>
        /// <remarks>
        /// <para>
        /// 不在 C# 里逐字节扫描（那样 300MB 索引要两三秒），而是先用
        /// <c>Span.IndexOf</c> 的向量化实现找到候选位置，再校验大小写，
        /// 命中后向前后扩展到整行取出路径。
        /// </para>
        /// <para>
        /// 索引是 UTF-8 文本：ASCII 部分做大小写折叠，
        /// 中文等非 ASCII 部分按字节精确比较（大小写折叠只对 ASCII 有意义）。
        /// </para>
        /// </remarks>
        private static void ScanBuffer(byte[] data, byte[] needle, int[] skip, string lowerTerm,
            List<SearchResultItem> output)
        {
            int matches = 0;
            int lastLineEnd = -1;

            // 第一行是建库时间戳，跳过
            var span = new ReadOnlySpan<byte>(data);
            int headerEnd = span.IndexOf((byte)'\n');
            if (headerEnd < 0)
            {
                return;
            }

            int searchFrom = headerEnd + 1;

            while (searchFrom < span.Length)
            {
                int hit = IndexOfIgnoreCase(span.Slice(searchFrom), needle, skip);
                if (hit < 0)
                {
                    return;
                }

                int position = searchFrom + hit;

                int lineStart = position == 0
                    ? 0
                    : span.Slice(0, position).LastIndexOf((byte)'\n') + 1;

                int nextBreak = span.Slice(position).IndexOf((byte)'\n');
                int lineEnd = nextBreak < 0 ? span.Length : position + nextBreak;

                if (lineEnd != lastLineEnd)
                {
                    int contentEnd = lineEnd;
                    if (contentEnd > lineStart && data[contentEnd - 1] == (byte)'\r')
                    {
                        contentEnd--;
                    }

                    if (contentEnd > lineStart)
                    {
                        string path = Encoding.UTF8.GetString(data, lineStart, contentEnd - lineStart);
                        string name = Path.GetFileName(path);

                        output.Add(new SearchResultItem(
                            path,
                            name,
                            Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
                            false,
                            ComputeScore(name, path, lowerTerm)));
                    }

                    lastLineEnd = lineEnd;

                    if (++matches >= MaxMatchesPerDrive)
                    {
                        return;
                    }
                }

                searchFrom = position + 1;
            }
        }

        /// <summary>
        /// 大小写不敏感的子串查找（ASCII 折叠），返回首个匹配的起始下标
        /// </summary>
        /// <remarks>
        /// 用 Boyer-Moore-Horspool：按最后一个字符查跳表，一次跳一段。
        /// 不能用 <c>Span.IndexOf</c> 逐字符试——关键字首字母很常见时
        /// （例如 <c>cmd.exe</c> 的 <c>c</c>）会退化成数千万次重试，
        /// 实测要 30 秒；跳表实现把 300MB 全量扫描压到亚秒级。
        /// </remarks>
        private static int IndexOfIgnoreCase(ReadOnlySpan<byte> haystack, byte[] needle, int[] skip)
        {
            int length = needle.Length;
            if (length == 0 || haystack.Length < length)
            {
                return -1;
            }

            int last = length - 1;
            int index = last;

            while (index < haystack.Length)
            {
                byte current = FoldAscii(haystack[index]);

                if (current == needle[last])
                {
                    int j = last - 1;
                    int k = index - 1;

                    while (j >= 0 && FoldAscii(haystack[k]) == needle[j])
                    {
                        j--;
                        k--;
                    }

                    if (j < 0)
                    {
                        return k + 1;
                    }
                }

                index += skip[current];
            }

            return -1;
        }

        /// <summary>
        /// 构建 Boyer-Moore-Horspool 跳表（按小写字节索引）
        /// </summary>
        private static int[] BuildSkipTable(byte[] needle)
        {
            int length = needle.Length;
            int[] skip = new int[256];

            for (int i = 0; i < skip.Length; i++)
            {
                skip[i] = length;
            }

            for (int i = 0; i < length - 1; i++)
            {
                skip[needle[i]] = length - 1 - i;
            }

            return skip;
        }

        private static byte FoldAscii(byte value)
        {
            return value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;
        }

        /// <summary>
        /// 按盘符均衡挑选结果
        /// </summary>
        /// <remarks>
        /// 单纯按得分取前 N 条时，命中数量多的盘会占满名额（C 盘尤甚）。
        /// 这里按「盘符分组 → 组内按得分排序 → 轮转取一条」挑选，
        /// 保证每个盘的最优结果都能进入展示列表，整体仍按相关度排序。
        /// </remarks>
        private static List<SearchResultItem> SelectBalanced(List<SearchResultItem> candidates, int maxResults)
        {
            var sorted = candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (sorted.Count <= maxResults)
            {
                return sorted;
            }

            var groups = sorted
                .GroupBy(c => GetGroupKey(c.FilePath))
                .Select(g => g.ToList())
                .OrderByDescending(g => g[0].Score)
                .ToList();

            var selected = new List<SearchResultItem>(maxResults);
            int round = 0;

            while (selected.Count < maxResults)
            {
                bool added = false;

                foreach (var group in groups)
                {
                    if (round >= group.Count)
                    {
                        continue;
                    }

                    selected.Add(group[round]);
                    added = true;

                    if (selected.Count >= maxResults)
                    {
                        break;
                    }
                }

                if (!added)
                {
                    break;
                }

                round++;
            }

            return selected;
        }

        private static string GetGroupKey(string path)
        {
            string letter = GetDriveLetter(path);
            return letter.Length > 0 ? letter : "?";
        }

        private static string GetDriveLetter(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':')
            {
                return string.Empty;
            }

            char letter = char.ToUpperInvariant(path[0]);
            if (letter < 'A' || letter > 'Z')
            {
                return string.Empty;
            }

            return letter.ToString();
        }

        /// <summary>
        /// 粗略相关度：文件名以关键字开头 &gt; 名称包含 &gt; 路径包含；名称短的更靠前
        /// </summary>
        private static int ComputeScore(string name, string path, string lowerTerm)
        {
            string lowerName = name.ToLowerInvariant();
            int score = 100;

            if (lowerName.StartsWith(lowerTerm, StringComparison.Ordinal))
            {
                score += 600;
            }
            else if (lowerName.Contains(lowerTerm))
            {
                score += 300;
            }

            score -= Math.Min(name.Length, 120);
            return score;
        }

        private static List<char> GetFixedDriveLetters()
        {
            var letters = new List<char>();

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    string name = drive.Name;
                    if (name.Length < 2 || name[1] != ':')
                    {
                        continue;
                    }

                    letters.Add(char.ToUpperInvariant(name[0]));
                }
            }
            catch (Exception ex)
            {
                Log.Information($"枚举磁盘失败: {ex.Message}");
            }

            return letters;
        }

        private static string GetIndexPath(char letter)
        {
            return Path.Combine(IndexDirectory, char.ToUpperInvariant(letter) + ".idx");
        }
    }

    /// <summary>
    /// 单个盘的索引状态
    /// </summary>
    /// <param name="Drive">盘符</param>
    /// <param name="SizeBytes">索引文件大小，0 表示尚未建立</param>
    /// <param name="BuiltAtUtc">索引建立时间（UTC）</param>
    public sealed record FileIndexStatus(char Drive, long SizeBytes, DateTime? BuiltAtUtc)
    {
        /// <summary>该盘是否已有索引</summary>
        public bool Exists => SizeBytes > 0;
    }

    /// <summary>
    /// 文件索引的整体状态快照
    /// </summary>
    /// <param name="IsBuilding">是否正在后台建立</param>
    /// <param name="HasIndex">是否已有任意一个盘的索引</param>
    /// <param name="TotalBytes">所有索引文件的总大小</param>
    /// <param name="LatestBuiltAtUtc">最近一次建立时间（UTC）</param>
    /// <param name="Drives">各盘状态</param>
    public sealed record FileIndexSummary(
        bool IsBuilding,
        bool HasIndex,
        long TotalBytes,
        DateTime? LatestBuiltAtUtc,
        IReadOnlyList<FileIndexStatus> Drives);
}
