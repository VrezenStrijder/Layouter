using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Layouter.Models;
using Layouter.Services;
using Layouter.Utility;

namespace Layouter.ViewModels
{
    /// <summary>
    /// 全局搜索视图模型
    /// </summary>
    /// <remarks>
    /// 结果按分区分成若干区块（分区名作小标题）展示；在设置中开启后，
    /// 末尾会追加一个「文件系统」区块，检索结果来自 Windows 搜索索引。
    /// 分区图标索引在窗口打开时建立一次，之后输入只做字符串比较，实时反馈；
    /// 文件系统查询较慢（0.6~8 秒），走后台线程 + 防抖 + 过期结果丢弃，不阻塞界面。
    /// </remarks>
    public class GlobalSearchViewModel : ObservableObject
    {
        /// <summary>图标预览的防抖间隔（毫秒）</summary>
        private const int IconLoadDebounceMilliseconds = 120;

        /// <summary>文件系统查询的防抖间隔（毫秒）</summary>
        private const int FileSystemDebounceMilliseconds = 350;

        private string query = string.Empty;
        private SearchResultItem? selectedResult;
        private List<SearchableIcon>? index;
        private ObservableCollection<SearchResultItem> results = new ObservableCollection<SearchResultItem>();
        private CancellationTokenSource? iconLoadCancellation;
        private int imageLoadVersion;

        /// <summary>当前分区图标结果（扁平，按得分排序）</summary>
        private List<SearchResultItem> partitionResults = new List<SearchResultItem>();

        /// <summary>当前文件系统结果</summary>
        private List<SearchResultItem> fileSystemResults = new List<SearchResultItem>();

        /// <summary>文件系统查询版本号，用于丢弃过期结果</summary>
        private int fileSystemSearchVersion;

        private CancellationTokenSource? fileSystemSearchCancellation;

        public GlobalSearchViewModel()
        {
            LocateCommand = new RelayCommand<SearchResultItem>(item =>
            {
                if (item != null)
                {
                    LocateRequested?.Invoke(this, item);
                }
            });

            OpenCommand = new RelayCommand<SearchResultItem>(item =>
            {
                if (item != null)
                {
                    OpenRequested?.Invoke(this, item);
                }
            });
        }

        /// <summary>请求定位到某个图标</summary>
        public event EventHandler<SearchResultItem>? LocateRequested;

        /// <summary>请求打开（执行）某个结果</summary>
        public event EventHandler<SearchResultItem>? OpenRequested;

        /// <summary>当前查询内容</summary>
        public string Query
        {
            get => query;
            set
            {
                if (SetProperty(ref query, value))
                {
                    RefreshResults();
                }
            }
        }

        /// <summary>搜索结果（分区区块在前，文件系统区块在后）</summary>
        /// <remarks>
        /// 整体替换而不是逐条增删：一次替换只触发一次界面刷新。
        /// 区块标题不是独立条目，而是标记在每条结果上（<see cref="SearchResultItem.IsGroupStart"/>），
        /// 因此选中、上下键导航仍然是对结果条目本身操作，逻辑保持不变。
        /// </remarks>
        public ObservableCollection<SearchResultItem> Results
        {
            get => results;
            private set
            {
                results = value;
                OnPropertyChanged(nameof(Results));
            }
        }

        /// <summary>当前选中的结果</summary>
        public SearchResultItem? SelectedResult
        {
            get => selectedResult;
            set
            {
                if (SetProperty(ref selectedResult, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        /// <summary>定位命令</summary>
        public RelayCommand<SearchResultItem> LocateCommand { get; }

        /// <summary>打开命令</summary>
        public RelayCommand<SearchResultItem> OpenCommand { get; }

        /// <summary>状态栏文字</summary>
        public string StatusText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return $"共 {index?.Count ?? 0} 个图标可检索";
                }

                if (Results.Count == 0)
                {
                    // 索引首次建立需要枚举全盘，期间搜索是空的，给个明确提示
                    if (LocalFileIndexService.Instance.IsBuilding)
                    {
                        return "正在建立全盘文件索引，稍候即可搜索文件系统";
                    }

                    bool fileSystemEnabled =
                        GeneralSettingsService.Instance.GetEnableFullFileSystemSearch();

                    if (!LocalFileIndexService.Instance.HasIndex && fileSystemEnabled)
                    {
                        return "文件索引尚未建立，请在搜索设置中重建索引";
                    }

                    // 达到阈值才会真正去查全盘，这里说明为什么没结果
                    int minLength = GeneralSettingsService.Instance.GetMinSearchLength();
                    if (fileSystemEnabled && query.Trim().Length < minLength)
                    {
                        return $"至少输入 {minLength} 个字符才能搜索文件系统";
                    }

                    return "没有匹配的结果";
                }

                return $"找到 {Results.Count} 个匹配结果";
            }
        }

        /// <summary>
        /// 重建索引（图标发生变化后调用）
        /// </summary>
        public void RebuildIndex()
        {
            index = IconSearchService.Instance.BuildIndex();
            RefreshResults();
        }

        /// <summary>
        /// 设置项（全文件系统搜索开关）变化后由外部调用，立即按新设置刷新
        /// </summary>
        public void RefreshForSettingsChange()
        {
            RefreshResults();
        }

        /// <summary>
        /// 按当前查询内容刷新结果
        /// </summary>
        private void RefreshResults()
        {
            try
            {
                if (index == null)
                {
                    index = IconSearchService.Instance.BuildIndex();
                }

                partitionResults = IconSearchService.Instance.Search(index, query);

                // 文件系统结果异步拉取；分区结果先立即显示，界面不会因 WDS 变慢
                ScheduleFileSystemSearch();

                RebuildResults();
            }
            catch (Exception ex)
            {
                Log.Information($"刷新搜索结果失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 把分区结果与文件系统结果拼成有序列表，标记区块起始条
        /// </summary>
        private List<SearchResultItem> BuildGroupedResults()
        {
            var list = new List<SearchResultItem>();

            // 分区结果按分区分组：组间按该组最高得分降序，组内保持得分降序
            var groups = partitionResults
                .GroupBy(r => r.GroupKey)
                .OrderByDescending(g => g.Max(r => r.Score))
                .ToList();

            int order = 0;
            foreach (var group in groups)
            {
                var items = group
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                for (int i = 0; i < items.Count; i++)
                {
                    items[i].IsGroupStart = i == 0;
                    items[i].GroupOrder = order;
                    list.Add(items[i]);
                }

                order++;
            }

            // 文件系统区块恒在最后，并用独立的区块标题与上方分隔
            if (fileSystemResults.Count > 0)
            {
                for (int i = 0; i < fileSystemResults.Count; i++)
                {
                    fileSystemResults[i].IsGroupStart = i == 0;
                    fileSystemResults[i].GroupOrder = int.MaxValue;
                    list.Add(fileSystemResults[i]);
                }
            }

            return list;
        }

        /// <summary>
        /// 重新生成 <see cref="Results"/>，并尽量保留当前选中项
        /// </summary>
        private void RebuildResults()
        {
            List<SearchResultItem> list = BuildGroupedResults();
            SearchResultItem? previous = selectedResult;

            Results = new ObservableCollection<SearchResultItem>(list);

            if (previous != null && list.Contains(previous))
            {
                SelectedResult = previous;
            }
            else
            {
                SelectedResult = Results.FirstOrDefault();
            }

            OnPropertyChanged(nameof(StatusText));
            ScheduleIconImages(list);
        }

        /// <summary>
        /// 安排一次文件系统查询：防抖 + 后台执行 + 过期结果丢弃
        /// </summary>
        private void ScheduleFileSystemSearch()
        {
            CancelFileSystemSearch();

            int version = ++fileSystemSearchVersion;

            // 关键字变化后立即清掉上一轮的残留，避免新查询期间显示旧结果
            fileSystemResults = new List<SearchResultItem>();

            bool enabled;
            int minLength;

            try
            {
                enabled = GeneralSettingsService.Instance.GetEnableFullFileSystemSearch();
                minLength = GeneralSettingsService.Instance.GetMinSearchLength();
            }
            catch
            {
                enabled = false;
                minLength = 2;
            }

            string trimmed = (query ?? string.Empty).Trim();

            // 未开启，或字符数不足阈值（默认 2）时直接跳过：
            // 单个字符在整盘索引里命中量极大，既慢又几乎没有参考价值
            if (!enabled || trimmed.Length < minLength)
            {
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            fileSystemSearchCancellation = cancellation;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FileSystemDebounceMilliseconds, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    List<SearchResultItem> hits = FileSystemSearchService.Instance.Search(trimmed);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    int capturedVersion = version;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 期间查询条件又变了，丢弃过期结果
                        if (capturedVersion != fileSystemSearchVersion)
                        {
                            return;
                        }

                        fileSystemResults = hits;
                        RebuildResults();
                    }));
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                }
                catch (Exception ex)
                {
                    Log.Information($"全文件系统搜索任务出错: {ex.Message}");
                }
                finally
                {
                    cancellation.Dispose();
                }
            });
        }

        /// <summary>
        /// 取消挂起的文件系统查询
        /// </summary>
        private void CancelFileSystemSearch()
        {
            CancellationTokenSource? cancellation = fileSystemSearchCancellation;
            fileSystemSearchCancellation = null;

            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 任务已自行释放
            }

            // 不 Dispose：交给后台任务在 finally 中完成
        }

        /// <summary>
        /// 安排图标预览的提取
        /// </summary>
        /// <remarks>
        /// 同一时刻只保留一批提取任务；新的输入到来时，
        /// 上一批尚未开始的任务会被取消，已经在跑的则通过版本号丢弃结果。
        /// 分区图标用图标路径，文件系统结果用文件真实路径，二者都交给
        /// <see cref="ShortCutUtil.GetSuitableIcon(string, int)"/> 提取系统图标。
        /// </remarks>
        private void ScheduleIconImages(List<SearchResultItem> items)
        {
            CancelIconLoad();

            if (items == null || items.Count == 0)
            {
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();

            // 提前取出令牌：取消时会释放 CTS，到那时再取会抛 ObjectDisposedException
            CancellationToken token = cancellation.Token;

            iconLoadCancellation = cancellation;
            int version = ++imageLoadVersion;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(IconLoadDebounceMilliseconds, token).ConfigureAwait(false);

                    foreach (SearchResultItem item in items)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        string iconPath = item?.OpenPath ?? string.Empty;
                        if (string.IsNullOrEmpty(iconPath))
                        {
                            continue;
                        }

                        BitmapSource? source = null;

                        try
                        {
                            int size = item!.Icon != null ? (int)Math.Round(item.Icon.Size.Width) : 32;
                            if (size <= 0)
                            {
                                size = 32;
                            }

                            source = ShortCutUtil.GetSuitableIcon(iconPath, size);
                        }
                        catch (Exception ex)
                        {
                            Log.Information($"加载搜索结果图标失败: {ex.Message}");
                        }

                        if (source == null)
                        {
                            continue;
                        }

                        int capturedVersion = version;
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (capturedVersion == imageLoadVersion)
                            {
                                item!.IconImage = source;
                            }
                        }));
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常的取消流程
                }
                catch (Exception ex)
                {
                    Log.Information($"加载搜索结果图标出错: {ex.Message}");
                }
                finally
                {
                    cancellation.Dispose();
                }
            });
        }

        /// <summary>
        /// 取消正在进行的图标提取
        /// </summary>
        private void CancelIconLoad()
        {
            CancellationTokenSource? cancellation = iconLoadCancellation;
            iconLoadCancellation = null;

            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 忽略
            }
        }
    }
}
