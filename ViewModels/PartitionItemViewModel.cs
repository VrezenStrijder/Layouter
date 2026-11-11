using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Layouter.ViewModels
{
    public class PartitionItemViewModel : ObservableObject
    {
        private string partitionId;
        private string title;
        private bool isVisible;
        private bool isLocked;
        private bool isSelected;
        private int iconCount;
        private int shortcutCount;
        private int folderCount;
        private int fileCount;
        private int shellCount;

        /// <summary>
        /// 分区ID
        /// </summary>
        public string PartitionId
        {
            get => partitionId;
            set => SetProperty(ref partitionId, value);
        }

        /// <summary>
        /// 分区标题
        /// </summary>
        public string Title
        {
            get => title;
            set => SetProperty(ref title, value);
        }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible
        {
            get => isVisible;
            set => SetProperty(ref isVisible, value);
        }

        /// <summary>
        /// 是否锁定
        /// </summary>
        public bool IsLocked
        {
            get => isLocked;
            set => SetProperty(ref isLocked, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        /// <summary>
        /// 分区内图标总数
        /// </summary>
        public int IconCount
        {
            get => iconCount;
            set
            {
                if (SetProperty(ref iconCount, value))
                {
                    OnPropertyChanged(nameof(IconCountText));
                    OnPropertyChanged(nameof(StatisticsText));
                }
            }
        }

        /// <summary>
        /// 快捷方式数量
        /// </summary>
        public int ShortcutCount
        {
            get => shortcutCount;
            set
            {
                if (SetProperty(ref shortcutCount, value))
                {
                    OnPropertyChanged(nameof(StatisticsText));
                }
            }
        }

        /// <summary>
        /// 文件夹数量
        /// </summary>
        public int FolderCount
        {
            get => folderCount;
            set
            {
                if (SetProperty(ref folderCount, value))
                {
                    OnPropertyChanged(nameof(StatisticsText));
                }
            }
        }

        /// <summary>
        /// 普通文件数量
        /// </summary>
        public int FileCount
        {
            get => fileCount;
            set
            {
                if (SetProperty(ref fileCount, value))
                {
                    OnPropertyChanged(nameof(StatisticsText));
                }
            }
        }

        /// <summary>
        /// 系统图标数量
        /// </summary>
        public int ShellCount
        {
            get => shellCount;
            set
            {
                if (SetProperty(ref shellCount, value))
                {
                    OnPropertyChanged(nameof(StatisticsText));
                }
            }
        }

        /// <summary>
        /// 图标总数（显示用）
        /// </summary>
        public string IconCountText => IconCount > 0 ? IconCount.ToString() : "-";

        /// <summary>
        /// 图标类别构成（显示用，只显示数量不为 0 的类别）
        /// </summary>
        public string StatisticsText
        {
            get
            {
                if (IconCount <= 0)
                {
                    return "空分区";
                }

                var parts = new List<string>();

                if (ShortcutCount > 0) parts.Add($"快捷方式 {ShortcutCount}");
                if (FolderCount > 0) parts.Add($"文件夹 {FolderCount}");
                if (FileCount > 0) parts.Add($"文件 {FileCount}");
                if (ShellCount > 0) parts.Add($"系统图标 {ShellCount}");

                return parts.Count > 0 ? string.Join(" · ", parts) : "空分区";
            }
        }
    }
}
