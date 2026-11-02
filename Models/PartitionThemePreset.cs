using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Layouter.Models
{
    /// <summary>
    /// 分区外观预设主题。
    /// <para>
    /// 用于在「分区设置 / 全局分区设置」中一键套用成套的外观参数，
    /// 免去逐项手动调整标题栏配色、透明度与图标尺寸。
    /// </para>
    /// </summary>
    public class PartitionThemePreset
    {
        /// <summary>主题名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>主题说明</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>标题栏前景色</summary>
        public Color TitleForeground { get; set; } = Colors.White;

        /// <summary>标题栏背景色</summary>
        public Color TitleBackground { get; set; } = Colors.DodgerBlue;

        /// <summary>分区整体透明度（0.3 ~ 1.0）</summary>
        public double Opacity { get; set; } = 0.9;

        /// <summary>标题字号</summary>
        public double TitleFontSize { get; set; } = 14;

        /// <summary>图标文字字号</summary>
        public double IconTextSize { get; set; } = 12;

        /// <summary>图标尺寸：Small / Medium / Large</summary>
        public string IconSize { get; set; } = "Medium";

        /// <summary>标题对齐：Left / Center / Right</summary>
        public string TitleAlignment { get; set; } = "Left";

        /// <summary>标题字体名称</summary>
        public string TitleFont { get; set; } = "Microsoft YaHei UI";

        /// <summary>全部预设主题</summary>
        public static IReadOnlyList<PartitionThemePreset> All { get; } = new List<PartitionThemePreset>
        {
            new PartitionThemePreset
            {
                Name = "经典深蓝",
                Description = "稳重的深蓝标题栏，文字清晰醒目",
                TitleForeground = Colors.White,
                TitleBackground = Color.FromRgb(0x1F, 0x3A, 0x93),
                Opacity = 0.90,
                TitleFontSize = 14,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "毛玻璃蓝",
                Description = "半透明蓝色，配合桌面壁纸更有层次",
                TitleForeground = Colors.White,
                TitleBackground = Color.FromRgb(0x25, 0x63, 0xEB),
                Opacity = 0.72,
                TitleFontSize = 14,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "极简浅色",
                Description = "浅色标题栏，适合明亮桌面与文档整理",
                TitleForeground = Color.FromRgb(0x1F, 0x29, 0x37),
                TitleBackground = Colors.White,
                Opacity = 0.95,
                TitleFontSize = 14,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "石墨灰",
                Description = "低调的深灰调，适合深色壁纸",
                TitleForeground = Colors.White,
                TitleBackground = Color.FromRgb(0x2F, 0x35, 0x42),
                Opacity = 0.88,
                TitleFontSize = 14,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "暖阳橙",
                Description = "温暖的橙色标题栏，工作生活两相宜",
                TitleForeground = Color.FromRgb(0x3B, 0x1F, 0x00),
                TitleBackground = Color.FromRgb(0xF5, 0x9E, 0x0B),
                Opacity = 0.88,
                TitleFontSize = 15,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "森野绿",
                Description = "沉静的绿色，长时间使用也不刺眼",
                TitleForeground = Colors.White,
                TitleBackground = Color.FromRgb(0x15, 0x80, 0x3D),
                Opacity = 0.88,
                TitleFontSize = 14,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Left"
            },
            new PartitionThemePreset
            {
                Name = "夜幕紫",
                Description = "通透的紫色，中心对齐更有仪表感",
                TitleForeground = Colors.White,
                TitleBackground = Color.FromRgb(0x6D, 0x28, 0xD9),
                Opacity = 0.82,
                TitleFontSize = 15,
                IconTextSize = 12,
                IconSize = "Medium",
                TitleAlignment = "Center"
            },
            new PartitionThemePreset
            {
                Name = "透明无框",
                Description = "隐藏标题栏底色，只保留文字与图标",
                TitleForeground = Colors.White,
                TitleBackground = Colors.Transparent,
                Opacity = 0.55,
                TitleFontSize = 14,
                IconTextSize = 13,
                IconSize = "Large",
                TitleAlignment = "Left"
            },
        };
    }
}
