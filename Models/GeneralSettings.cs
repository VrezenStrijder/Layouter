using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Layouter.Models
{
    /// <summary>
    /// 通用设置类
    /// </summary>
    public class GeneralSettings
    {
        /// <summary>
        /// 是否开机自启动
        /// </summary>
        public bool AutoStartEnabled { get; set; } = false;

        /// <summary>
        /// 是否启用全局样式
        /// </summary>
        public bool EnableGlobalStyle { get; set; } = false;

        /// <summary>
        /// 分区窗口显示状态字典
        /// </summary>
        public Dictionary<string, bool> PartitionVisibility { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// 配置备份的默认根目录（存放所有备份文件）
        /// </summary>
        public string BackupRootDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 隐藏存储目录（存放从桌面隐藏起来的图标实体文件）
        /// </summary>
        public string HiddenStoreDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用「双击桌面隐藏桌面图标」
        /// </summary>
        public bool EnableDesktopDoubleClickToggle { get; set; } = false;

        /// <summary>
        /// 桌面图标当前是否处于隐藏状态（用于程序重启后对齐显示状态）
        /// </summary>
        public bool DesktopIconsHidden { get; set; } = false;    }
}
