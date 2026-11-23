using System.Collections.Generic;

namespace Layouter.Models
{
    /// <summary>
    /// 可以配置全局快捷键的功能
    /// </summary>
    /// <remarks>
    /// 枚举值同时用作全局快捷键注册时的消息编号偏移，
    /// 因此只允许追加，不允许调整已有值的顺序。
    /// </remarks>
    public enum HotkeyAction
    {
        /// <summary>全局搜索</summary>
        Search = 0,

        /// <summary>新建分区</summary>
        AddPartition = 1,

        /// <summary>自动排列图标</summary>
        ArrangeIcons = 2,

        /// <summary>打开分区管理</summary>
        PartitionManager = 3
    }

    /// <summary>
    /// 快捷键功能的展示信息与默认值
    /// </summary>
    public static class HotkeyActionInfo
    {
        /// <summary>
        /// 全部可配置的功能（按设置窗口中的展示顺序）
        /// </summary>
        public static readonly IReadOnlyList<HotkeyAction> All = new List<HotkeyAction>
        {
            HotkeyAction.Search,
            HotkeyAction.AddPartition,
            HotkeyAction.ArrangeIcons,
            HotkeyAction.PartitionManager
        };

        /// <summary>
        /// 功能名称
        /// </summary>
        public static string GetDisplayName(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Search: return "全局搜索";
                case HotkeyAction.AddPartition: return "新建分区";
                case HotkeyAction.ArrangeIcons: return "自动排列图标";
                case HotkeyAction.PartitionManager: return "打开分区管理";
                default: return action.ToString();
            }
        }

        /// <summary>
        /// 功能说明
        /// </summary>
        public static string GetDescription(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Search: return "唤起全局搜索窗口，检索全部分区中的图标";
                case HotkeyAction.AddPartition: return "新建一个分区窗口";
                case HotkeyAction.ArrangeIcons: return "重新排列所有分区中的图标";
                case HotkeyAction.PartitionManager: return "打开分区管理窗口";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 默认快捷键
        /// </summary>
        public static string GetDefaultGesture(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Search: return "Ctrl+Alt+S";
                case HotkeyAction.AddPartition: return "Ctrl+Alt+N";
                case HotkeyAction.ArrangeIcons: return "Ctrl+Alt+A";
                case HotkeyAction.PartitionManager: return "Ctrl+Alt+M";
                default: return string.Empty;
            }
        }
    }
}
