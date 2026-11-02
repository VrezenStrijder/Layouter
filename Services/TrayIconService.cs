using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Layouter.Views;
using Hardcodet.Wpf.TaskbarNotification;
using Layouter.ViewModels;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using FluentIcons.Wpf;
using Layouter.Utility;
using System.Collections;

namespace Layouter.Services
{
    public class TrayIconService : IDisposable
    {
        private TaskbarIcon notifyIcon;
        private readonly TrayIconViewModel viewModel;
        private bool isInitialized = false;

        private static readonly Lazy<TrayIconService> instance = new Lazy<TrayIconService>(() => new TrayIconService());
        public static TrayIconService Instance => instance.Value;


        public TrayIconService()
        {
            this.viewModel = new TrayIconViewModel();
        }

        public void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            // 从资源中获取托盘图标
            notifyIcon = Application.Current.FindResource("TrayIcon") as TaskbarIcon;

            if (notifyIcon != null)
            {
                // 设置数据上下文
                notifyIcon.DataContext = viewModel;
                notifyIcon.Icon = IconUtil.CreateIconFromFluentIcon(FluentIcons.Common.Icon.LayoutCellFour, Brushes.LightBlue);

                viewModel.SetTrayIcon(notifyIcon);
                isInitialized = true;
            }
            else
            {
                AppMessageBox.Show("无法初始化托盘图标", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 显示气泡提示
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息内容</param>
        /// <param name="icon">图标类型</param>
        public void ShowBalloonTip(string title, string message, BalloonIcon icon)
        {
            if (notifyIcon == null || !isInitialized)
            {
                return;
            }

            try
            {
                notifyIcon.ShowBalloonTip(title, message, icon);
            }
            catch (Exception ex)
            {
                Log.Information($"显示气泡提示失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            notifyIcon?.Dispose();
        }
    }
}
