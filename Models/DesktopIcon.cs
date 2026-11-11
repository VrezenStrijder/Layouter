using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Layouter.Services;

namespace Layouter.Models
{
    public class DesktopIcon : ObservableObject
    {
        private string id;
        private string name;
        private string iconPath;
        private Point position;
        private Size size = new Size(48, 48);
        private bool isDragging;
        private IconType iconType = IconType.Shortcut;
        private double textSize = 12d;
        private bool isSelected;
        private bool isRenaming;

        public string Id 
        { 
            get => id; 
            set => SetProperty(ref id, value); 
        }

        public string Name
        {
            get => name;
            set
            {
                if (SetProperty(ref name, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName
        {
            get
            {
                string value = name ?? string.Empty;

                if (value.Length <= MaxDisplayNameLength)
                {
                    return value;
                }

                return value.Substring(0, MaxDisplayNameLength - 1) + "…";
            }
        }

        /// <summary>
        /// 名称在图标下方显示时的最大字符数，超出部分以省略号收尾
        /// </summary>
        public const int MaxDisplayNameLength = 14;

        public string IconPath 
        { 
            get => iconPath; 
            set
            {
                if (SetProperty(ref iconPath, value))
                {
                    OnPropertyChanged(nameof(DisplayPath));
                }
            }
        }

        /// <summary>
        /// 展示用的文件路径
        /// </summary>
        /// <remarks>
        /// 图标被收进「安全隐藏存储」后，<see cref="IconPath"/> 指向的是存放路径
        /// （形如 HiddenStore\xxxxxxxx.lnk），对用户没有意义，
        /// 这里换算回它原本所在的路径。
        /// </remarks>
        public string DisplayPath
        {
            get
            {
                if (string.IsNullOrEmpty(iconPath))
                {
                    return string.Empty;
                }

                try
                {
                    return DesktopIconService.Instance.RemoveHiddenPathInIconPath(iconPath);
                }
                catch
                {
                    return iconPath;
                }
            }
        }

        public Point Position 
        { 
            get => position; 
            set => SetProperty(ref position, value); 
        }

        public Size Size 
        { 
            get => size; 
            set => SetProperty(ref size, value); 
        }
        
        public bool IsDragging
        {
            get => isDragging;
            set => SetProperty(ref isDragging, value);
        }

        public IconType IconType
        {
            get => iconType;
            set => SetProperty(ref iconType, value);
        }

        public double TextSize
        {
            get => textSize;
            set => SetProperty(ref textSize, value);
        }

        /// <summary>
        /// 是否处于选中状态（用于高亮显示）
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        /// <summary>
        /// 是否处于重命名编辑状态
        /// </summary>
        public bool IsRenaming
        {
            get => isRenaming;
            set => SetProperty(ref isRenaming, value);
        }

        public DesktopIcon()
        {
            // 生成唯一ID
            Id = Guid.NewGuid().ToString();
            Size = new Size(48, 48); // 默认大小
        }

        public DesktopIcon Clone()
        {
            return new DesktopIcon
            {
                Name = this.Name,
                IconPath = this.IconPath,
                Position = this.Position,
                Size = this.Size,
                IconType = this.IconType,
                TextSize = this.TextSize
            };
        }
    }

    public enum IconType
    {
        Shortcut = 0,
        Shell,
        File,
        Folder
    }
}
