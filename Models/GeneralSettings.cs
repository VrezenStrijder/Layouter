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
        /// 是否在全局搜索中启用「全文件系统搜索」（默认关闭）
        /// </summary>
        /// <remarks>
        /// 开启后，搜索结果会在分区图标之后追加一个「文件系统」区块，
        /// 检索程序自己建立的本地文件名索引所收录的全部文件。
        /// </remarks>
        public bool EnableFullFileSystemSearch { get; set; } = false;

        /// <summary>
        /// 触发全盘文件搜索所需的最少字符数（默认 2）
        /// </summary>
        /// <remarks>
        /// 只影响「全文件系统搜索」：单个字符在全盘索引里命中量极大，
        /// 既慢又几乎没有参考价值。分区内的图标搜索不受此限制，
        /// 因为它只是几十上百个字符串的比较，单字符也不会卡。
        /// </remarks>
        public int MinSearchLength { get; set; } = 2;

        /// <summary>
        /// 桌面图标当前是否处于隐藏状态（用于程序重启后对齐显示状态）
        /// </summary>
        public bool DesktopIconsHidden { get; set; } = false;

        /// <summary>
        /// 全局搜索的快捷键（如 Ctrl+Alt+S），为空表示不注册快捷键
        /// </summary>
        /// <remarks>
        /// 旧版本只保存了搜索这一个快捷键，2.9.0 起改用 <see cref="Hotkeys"/> 统一管理。
        /// 这个字段保留下来只为兼容旧配置，读取时会自动迁移。
        /// </remarks>
        public string SearchHotkey { get; set; } = "Ctrl+Alt+S";

        /// <summary>
        /// 各个功能的全局快捷键：功能名 → 快捷键描述
        /// </summary>
        /// <remarks>
        /// 键名取 <see cref="HotkeyAction"/> 的枚举名，值为 "Ctrl+Alt+S" 这类描述，
        /// 空字符串表示该功能不设置快捷键。
        /// </remarks>
        public Dictionary<string, string> Hotkeys { get; set; } = new Dictionary<string, string>();
    }
}
