using System;
using System.Windows;
using System.Windows.Threading;
using Layouter.Views;

namespace Layouter.Utility
{
    /// <summary>
    /// 主题化消息对话框入口。
    /// <para>
    /// API 与 <see cref="System.Windows.MessageBox"/> 完全对齐，
    /// 因此原有的 <c>MessageBox.Show(...)</c> 调用只需替换为 <c>AppMessageBox.Show(...)</c> 即可切换到新样式。
    /// </para>
    /// </summary>
    public static class AppMessageBox
    {
        /// <summary>
        /// 显示消息对话框
        /// </summary>
        public static MessageBoxResult Show(string message)
        {
            return Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// 显示消息对话框
        /// </summary>
        public static MessageBoxResult Show(string message, string caption)
        {
            return Show(message, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// 显示消息对话框
        /// </summary>
        public static MessageBoxResult Show(string message, string caption, MessageBoxButton button)
        {
            return Show(message, caption, button, MessageBoxImage.None);
        }

        /// <summary>
        /// 显示消息对话框
        /// </summary>
        public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            var app = Application.Current;
            if (app == null)
            {
                // 没有 Application（例如单元测试环境）时退回系统对话框
                return System.Windows.MessageBox.Show(message, caption, button, image);
            }

            if (!app.Dispatcher.CheckAccess())
            {
                // 非 UI 线程调用时切回 UI 线程执行
                MessageBoxResult result = MessageBoxResult.None;
                app.Dispatcher.Invoke(new Action(() =>
                {
                    result = ShowCore(message, caption, button, image);
                }));

                return result;
            }

            return ShowCore(message, caption, button, image);
        }

        /// <summary>
        /// 信息提示
        /// </summary>
        public static void ShowInfo(string message, string caption = "提示")
        {
            Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 成功提示
        /// </summary>
        public static void ShowSuccess(string message, string caption = "成功")
        {
            Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 警告提示
        /// </summary>
        public static void ShowWarning(string message, string caption = "警告")
        {
            Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 错误提示
        /// </summary>
        public static void ShowError(string message, string caption = "错误")
        {
            Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// 确认对话框
        /// </summary>
        /// <param name="message">正文</param>
        /// <param name="caption">标题</param>
        /// <param name="isDestructive">确认按钮是否使用危险色（删除类操作）</param>
        public static bool ShowConfirm(string message, string caption = "确认", bool isDestructive = false)
        {
            var app = Application.Current;
            if (app == null)
            {
                return System.Windows.MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }

            if (!app.Dispatcher.CheckAccess())
            {
                bool result = false;
                app.Dispatcher.Invoke(new Action(() =>
                {
                    result = ShowConfirmCore(message, caption, isDestructive);
                }));

                return result;
            }

            return ShowConfirmCore(message, caption, isDestructive);
        }

        private static MessageBoxResult ShowCore(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            try
            {
                var window = new AppMessageBoxWindow();
                window.Configure(message, caption, button, image);
                SetOwner(window);

                window.ShowDialog();

                return window.DialogResult == MessageBoxResult.None ? MessageBoxResult.OK : window.DialogResult;
            }
            catch (Exception ex)
            {
                Log.Information($"显示消息对话框失败: {ex.Message}");
                return System.Windows.MessageBox.Show(message, caption, button, image);
            }
        }

        private static bool ShowConfirmCore(string message, string caption, bool isDestructive)
        {
            try
            {
                var window = new AppMessageBoxWindow();
                window.Configure(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (isDestructive)
                {
                    window.MarkOkAsDestructive();
                }

                SetOwner(window);

                window.ShowDialog();

                return window.DialogResult == MessageBoxResult.Yes;
            }
            catch (Exception ex)
            {
                Log.Information($"显示确认对话框失败: {ex.Message}");
                return System.Windows.MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }
        }

        /// <summary>
        /// 尝试把当前活动窗口设为对话框的父窗口，使其居中于应用之上
        /// </summary>
        private static void SetOwner(Window window)
        {
            try
            {
                var owner = Application.Current?.Windows
                    .Cast<Window>()
                    .FirstOrDefault(w => w.IsActive && w.IsVisible);

                if (owner != null && !ReferenceEquals(owner, window))
                {
                    window.Owner = owner;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"设置对话框父窗口失败: {ex.Message}");
            }
        }
    }
}
