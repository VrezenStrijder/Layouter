using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Layouter.Utility
{
    /// <summary>
    /// 把文件路径转换为图标。
    /// <para>
    /// 同时实现 <see cref="IValueConverter"/> 与 <see cref="IMultiValueConverter"/>：
    /// 单值用法只传路径（按默认尺寸取图标），
    /// 多值用法可额外传入图标显示尺寸，从而按需请求原生分辨率的图标，避免放大后模糊。
    /// </para>
    /// </summary>
    public class FilePathToIconConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || !(value is string filePath))
            {
                return null;
            }

            return CreateIcon(filePath, ShortCutUtil.DefaultIconSize);
        }

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length == 0)
            {
                return null;
            }

            if (!(values[0] is string filePath) || string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            int size = ShortCutUtil.DefaultIconSize;

            if (values.Length > 1 && values[1] is System.Windows.Size iconSize && iconSize.Width > 0)
            {
                size = (int)Math.Round(iconSize.Width);
            }
            else if (values.Length > 1 && values[1] is double width && width > 0)
            {
                size = (int)Math.Round(width);
            }

            return CreateIcon(filePath, size);
        }

        /// <summary>
        /// 生成图标位图源
        /// </summary>
        private static object CreateIcon(string filePath, int size)
        {
            try
            {
                // 如果是图片文件，直接返回
                string extension = Path.GetExtension(filePath).ToLower();
                if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif")
                {
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(filePath);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            return bmp;
                        }
                        catch
                        {

                        }
                    }
                }

                var source = ShortCutUtil.GetSuitableIcon(filePath, size);

                return source;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
