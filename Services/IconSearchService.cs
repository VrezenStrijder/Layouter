using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Layouter.Models;
using Layouter.Utility;
using Layouter.ViewModels;

namespace Layouter.Services
{
    /// <summary>
    /// 可搜索的图标条目（索引用的中间结构）
    /// </summary>
    public class SearchableIcon
    {
        public SearchableIcon(DesktopIcon icon, string windowId, string partitionName, string name, string path)
        {
            Icon = icon;
            WindowId = windowId;
            PartitionName = partitionName ?? string.Empty;

            NameLower = (name ?? string.Empty).ToLowerInvariant();
            PathLower = (path ?? string.Empty).ToLowerInvariant();
            PartitionLower = PartitionName.ToLowerInvariant();

            // 拼音与首字母在索引阶段一次算好，
            // 之后每次输入都只是字符串比较，实时反馈不会有延迟
            NamePinyin = PinyinHelper.GetPinyin(name ?? string.Empty).ToLowerInvariant();
            NameInitials = PinyinHelper.GetInitials(name ?? string.Empty).ToLowerInvariant();
        }

        public DesktopIcon Icon { get; }
        public string WindowId { get; }
        public string PartitionName { get; }
        public string NameLower { get; }
        public string PathLower { get; }
        public string PartitionLower { get; }
        public string NamePinyin { get; }
        public string NameInitials { get; }
    }

    /// <summary>
    /// 图标搜索服务
    /// </summary>
    /// <remarks>
    /// 搜索范围是本程序中所有分区里的全部图标，
    /// 匹配目标包括图标名称、原始路径、分区名称，
    /// 并且同时支持中文、英文与汉语拼音（整拼与首字母）。
    /// </remarks>
    public class IconSearchService
    {
        private static readonly Lazy<IconSearchService> instance = new Lazy<IconSearchService>(() => new IconSearchService());
        public static IconSearchService Instance => instance.Value;

        private IconSearchService()
        {
        }

        /// <summary>
        /// 建立搜索索引：遍历所有分区窗口里的全部图标
        /// </summary>
        public List<SearchableIcon> BuildIndex()
        {
            var list = new List<SearchableIcon>();

            try
            {
                foreach (var window in WindowManagerService.Instance.GetAllWindows())
                {
                    if (window?.DataContext is not DesktopManagerViewModel viewModel)
                    {
                        continue;
                    }

                    string partitionName = viewModel.Name ?? string.Empty;

                    foreach (var icon in viewModel.Icons)
                    {
                        if (icon == null)
                        {
                            continue;
                        }

                        list.Add(new SearchableIcon(
                            icon,
                            window.WindowId,
                            partitionName,
                            icon.Name,
                            icon.DisplayPath));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"建立搜索索引时出错: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="index">索引</param>
        /// <param name="query">查询内容</param>
        /// <param name="maxResults">最多返回条数</param>
        public List<SearchResultItem> Search(List<SearchableIcon> index, string query, int maxResults = 60)
        {
            var results = new List<SearchResultItem>();

            if (index == null || index.Count == 0)
            {
                return results;
            }

            string trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return results;
            }

            string lower = trimmed.ToLowerInvariant();
            string latin = ExtractLatin(trimmed);
            string han = ExtractHan(trimmed);
            string hanPinyin = han.Length > 0 ? PinyinHelper.GetPinyin(han).ToLowerInvariant() : string.Empty;

            foreach (var item in index)
            {
                int score = Score(item, lower, latin, han, hanPinyin);
                if (score > 0)
                {
                    results.Add(new SearchResultItem(item.Icon, item.WindowId, item.PartitionName, score));
                }
            }

            // 得分高的在前；同分时名称短的更像用户要找的那个
            results = results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.DisplayName.Length)
                .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (results.Count > maxResults)
            {
                results = results.Take(maxResults).ToList();
            }

            return results;
        }

        /// <summary>
        /// 计算匹配得分，0 表示不匹配
        /// </summary>
        private static int Score(SearchableIcon item, string lower, string latin, string han, string hanPinyin)
        {
            int score = 0;

            // 1) 名称整体匹配（中文或英文都能命中）
            if (lower.Length > 0 && item.NameLower.Contains(lower))
            {
                score = Math.Max(score, PositionScore(item.NameLower, lower, 1000, 520));
            }

            // 2) 名称中的汉字片段
            if (han.Length > 0 && item.NameLower.Contains(han))
            {
                score = Math.Max(score, PositionScore(item.NameLower, han, 960, 500));
            }

            // 3) 名称中的字母片段：英文直接命中，拼音与首字母在这里生效
            if (latin.Length > 0)
            {
                if (item.NameLower.Contains(latin))
                {
                    score = Math.Max(score, PositionScore(item.NameLower, latin, 900, 480));
                }
                else if (item.NamePinyin.Contains(latin))
                {
                    score = Math.Max(score, PositionScore(item.NamePinyin, latin, 760, 400));
                }
                else if (item.NameInitials.Contains(latin))
                {
                    score = Math.Max(score, PositionScore(item.NameInitials, latin, 700, 360));
                }
            }

            // 4) 查询里带汉字时，用汉字的拼音再比对一次
            //    （输入「微信」也能命中名为「WeiXin」的图标）
            if (hanPinyin.Length > 0 && item.NamePinyin.Contains(hanPinyin))
            {
                score = Math.Max(score, 620);
            }

            // 5) 路径匹配（英文查询可以命中路径里的文件名）
            if (latin.Length > 0 && item.PathLower.Contains(latin))
            {
                score = Math.Max(score, 240);
            }

            // 6) 分区名匹配
            if (latin.Length > 0 && item.PartitionLower.Contains(latin))
            {
                score = Math.Max(score, 200);
            }
            else if (han.Length > 0 && item.PartitionLower.Contains(han))
            {
                score = Math.Max(score, 220);
            }

            return score;
        }

        /// <summary>
        /// 越靠前的匹配得分越高
        /// </summary>
        private static int PositionScore(string text, string query, int bestScore, int baseScore)
        {
            int index = text.IndexOf(query, StringComparison.Ordinal);
            if (index <= 0)
            {
                return bestScore;
            }

            return Math.Max(baseScore, bestScore - index * 8);
        }

        /// <summary>
        /// 提取查询中的字母与数字片段
        /// </summary>
        private static string ExtractLatin(string text)
        {
            var builder = new StringBuilder();
            foreach (char ch in text)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                {
                    builder.Append(ch);
                }
            }
            return builder.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// 提取查询中的汉字片段
        /// </summary>
        private static string ExtractHan(string text)
        {
            var builder = new StringBuilder();
            foreach (char ch in text)
            {
                if (PinyinHelper.IsChinese(ch))
                {
                    builder.Append(ch);
                }
            }
            return builder.ToString();
        }
    }
}
