using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Layouter.Models
{
    /// <summary>
    /// 版本信息。字段与远端 update.json 保持一致，
    /// 额外提供 <see cref="IsUpdateAvailable"/> 用于区分「当前版本」与「可更新的最新版本」。
    /// </summary>
    public class ProductVersion
    {
        public string LatestVersion { get; set; }

        public string DownloadLink1 { get; set; }

        public string DownloadLink2 { get; set; }

        public string ReleaseDate { get; set; }

        /// <summary>
        /// 版本更新说明（为空时会自动从发布页拉取）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否为可下载的新版本
        /// </summary>
        public bool IsUpdateAvailable { get; set; }
    }
}
