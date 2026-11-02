using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Layouter.Utility
{
    public static class Env
    {
        public static string AppName { get; set; } = "Layouter";

        public static string UpdateUrl { get; set; } = "https://raw.githubusercontent.com/VrezenStrijder/Layouter/refs/heads/main/update.json";

        public static string ReleasePageUrl { get; set; } = "https://github.com/VrezenStrijder/Layouter/releases";

        /// <summary>
        /// GitHub Releases API 前缀，用于按标签拉取对应版本的更新说明
        /// </summary>
        public static string ReleaseApiUrl { get; set; } = "https://api.github.com/repos/VrezenStrijder/Layouter/releases/tags/";

        public static string FrameworkDependencyVersionFolderName { get; set; } = "Layouter_frameworkdependency";
        public static string IndependentVersionFolderName { get; set; } = "Layouter_independent";

        public static string HiddenFolderName { get; set; } = ".layouterhidden";

        /// <summary>
        /// 安全隐藏存储的目录名（位于应用数据目录下，桌面不再残留隐藏目录）
        /// </summary>
        public static string HiddenStoreFolderName { get; set; } = "HiddenStore";

        /// <summary>
        /// 安全隐藏存储的宿主文件名，其上挂载 ADS 索引数据流
        /// </summary>
        public static string HiddenStoreHostFileName { get; set; } = ".lstore";

        /// <summary>
        /// ADS 索引数据流名称
        /// </summary>
        public static string HiddenStoreIndexStreamName { get; set; } = "layouter.index";

        /// <summary>
        /// 卷不支持 ADS 时使用的降级索引文件名
        /// </summary>
        public static string HiddenStoreIndexFileName { get; set; } = "index.json";
    }
}
