using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Layouter.Utility;
using Layouter.ViewModels;

namespace Layouter.Models
{

    public class PartitionSettings
    {
        public Color TitleForeground { get; set; }
        public Color TitleBackground { get; set; }
        public string TitleFont { get; set; }
        public double TitleFontSize { get; set; }
        public HorizontalAlignment TitleAlignment { get; set; }
        public double Opacity { get; set; }

        /// <summary>
        /// 未选中（窗口未激活、鼠标也不在分区上）时的分区透明度
        /// </summary>
        /// <remarks>
        /// 为 null 表示沿用旧配置的行为：取 <see cref="Opacity"/> 的 0.7 倍。
        /// 这样旧配置文件升级过来后外观保持不变。
        /// </remarks>
        public double? InactiveOpacity { get; set; }

        /// <summary>
        /// 分区窗口的背景材质
        /// </summary>
        public PartitionBackdrop Backdrop { get; set; } = PartitionBackdrop.None;

        /// <summary>
        /// 背景材质色调的浓度（0~1）
        /// </summary>
        /// <remarks>
        /// 这里只控制色调叠加层的不透明度，颜色见 <see cref="TintColorOverride"/>。
        /// </remarks>
        public double TintOpacity { get; set; } = 0.05;

        /// <summary>
        /// 自定义色调
        /// </summary>
        /// <remarks>
        /// 为 null 表示跟随标题栏背景色；只有用户主动选色后才会写入具体颜色。
        /// </remarks>
        public Color? TintColorOverride { get; set; }
        public IconSize IconSize { get; set; }
        public double IconTextSize { get; set; } = 12d;
        public bool IsLocked { get; set; } = false;
        public bool IsRolledUp { get; set; } = false;
    }

    public class GlobalPartitionSettings : PartitionSettings
    {

    }

    public class PartitionVisibilityChangedEventArgs : EventArgs
    {
        public string PartitionId { get; }
        public bool IsVisible { get; }

        public PartitionVisibilityChangedEventArgs(string partitionId, bool isVisible)
        {
            PartitionId = partitionId;
            IsVisible = isVisible;
        }
    }
}
