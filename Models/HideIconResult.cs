namespace Layouter.Models
{
    /// <summary>
    /// 隐藏（搬运）单个桌面图标的结果
    /// </summary>
    public class HideIconResult
    {
        /// <summary>桌面上的原始路径</summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>隐藏后在安全存储中的存放路径（失败时为原路径）</summary>
        public string StoredPath { get; set; } = string.Empty;

        /// <summary>是否处理成功</summary>
        public bool Success { get; set; }

        /// <summary>失败原因</summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
