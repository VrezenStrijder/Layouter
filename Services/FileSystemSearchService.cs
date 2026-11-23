using Layouter.Models;

namespace Layouter.Services
{
    /// <summary>
    /// 文件系统搜索入口
    /// </summary>
    /// <remarks>
    /// <para>
    /// 早期版本直接查询 Windows 搜索索引（WDS）。实测该系统索引既覆盖不全
    /// （<c>D:\Program Files\Lunacy\Lunacy.exe</c> 不在索引里），稀有关键字还会
    /// 超时（<c>LIKE '%wb2api%'</c> 报 <c>0x80041607</c>，等 10~30 秒才失败），
    /// 因此改为查询程序自己建立的本地文件名索引，见 <see cref="LocalFileIndexService"/>。
    /// </para>
    /// <para>
    /// 保留本类是为了让调用方（ViewModel）不必关心索引实现；
    /// 它只做参数转发，真正的匹配与排序都在索引服务里。
    /// </para>
    /// </remarks>
    public class FileSystemSearchService
    {
        private static readonly Lazy<FileSystemSearchService> instance =
            new Lazy<FileSystemSearchService>(() => new FileSystemSearchService());

        public static FileSystemSearchService Instance => instance.Value;

        /// <summary>默认展示条数</summary>
        public const int DefaultMaxResults = 30;

        private FileSystemSearchService()
        {
        }

        /// <summary>
        /// 同步执行文件系统搜索（应在后台线程调用）
        /// </summary>
        /// <param name="query">关键字</param>
        /// <param name="maxResults">最多返回条数</param>
        public List<SearchResultItem> Search(string query, int maxResults = DefaultMaxResults)
        {
            return LocalFileIndexService.Instance.Search(query, maxResults);
        }
    }
}
