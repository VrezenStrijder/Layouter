using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Layouter.Controls;

namespace Layouter.Utility
{
    /// <summary>
    /// 轻量通知（Toast）服务。
    /// <para>
    /// 使用一个无边框、置顶、不抢焦点的宿主窗口承载全部通知卡片，
    /// 卡片自右下角向上堆叠，超时后自动淡出。
    /// </para>
    /// </summary>
    public static class AppToastService
    {
        private const double ToastWidth = 340;
        private const double Margin = 16;
        private const double StackGap = 10;

        private static readonly object syncRoot = new object();
        private static Window? hostWindow;
        private static StackPanel? stack;
        private static readonly List<AppToastView> activeToasts = new List<AppToastView>();

        /// <summary>
        /// 成功提示
        /// </summary>
        public static void Success(string title, string? message = null, int durationMs = 4000)
        {
            Show(ToastKind.Success, title, message, durationMs);
        }

        /// <summary>
        /// 错误提示
        /// </summary>
        public static void Error(string title, string? message = null, int durationMs = 5000)
        {
            Show(ToastKind.Error, title, message, durationMs);
        }

        /// <summary>
        /// 警告提示
        /// </summary>
        public static void Warning(string title, string? message = null, int durationMs = 4500)
        {
            Show(ToastKind.Warning, title, message, durationMs);
        }

        /// <summary>
        /// 信息提示
        /// </summary>
        public static void Info(string title, string? message = null, int durationMs = 4000)
        {
            Show(ToastKind.Info, title, message, durationMs);
        }

        /// <summary>
        /// 关闭全部通知
        /// </summary>
        public static void Clear()
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (syncRoot)
                {
                    foreach (var toast in new List<AppToastView>(activeToasts))
                    {
                        RemoveToast(toast, animate: false);
                    }
                }
            }));
        }

        /// <summary>
        /// 显示一条通知
        /// </summary>
        public static void Show(ToastKind kind, string title, string? message = null, int durationMs = 4000)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            var dispatcher = app.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Show(kind, title, message, durationMs)));
                return;
            }

            try
            {
                EnsureHostWindow();

                var toast = new AppToastView
                {
                    Width = ToastWidth,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Opacity = 0
                };
                toast.Apply(kind, title, message);
                toast.Dismissed += (s, e) => RemoveToast(toast, animate: true);

                lock (syncRoot)
                {
                    activeToasts.Add(toast);
                }

                if (stack != null)
                {
                    stack.Children.Add(toast);
                }

                AnimateIn(toast);
                UpdateHostPosition();

                // 超时自动关闭
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(800, durationMs)) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    RemoveToast(toast, animate: true);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Log.Information($"显示通知失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建并显示宿主窗口
        /// </summary>
        private static void EnsureHostWindow()
        {
            if (hostWindow != null && hostWindow.IsLoaded)
            {
                if (!hostWindow.IsVisible)
                {
                    hostWindow.Show();
                }

                return;
            }

            stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            hostWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Focusable = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                SizeToContent = SizeToContent.Height,
                Width = ToastWidth,
                Title = "Layouter 通知"
            };

            hostWindow.Content = stack;
            hostWindow.SizeChanged += (s, e) => UpdateHostPosition();

            hostWindow.Show();
            UpdateHostPosition();
        }

        /// <summary>
        /// 重新计算宿主窗口在屏幕右下角的位置
        /// </summary>
        private static void UpdateHostPosition()
        {
            if (hostWindow == null || stack == null)
            {
                return;
            }

            try
            {
                var workArea = SystemParameters.WorkArea;
                double height = stack.ActualHeight > 0 ? stack.ActualHeight : hostWindow.ActualHeight;

                hostWindow.Left = workArea.Right - ToastWidth - Margin;
                hostWindow.Top = workArea.Bottom - height - Margin;
            }
            catch (Exception ex)
            {
                Log.Information($"计算通知位置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除一条通知
        /// </summary>
        private static void RemoveToast(AppToastView toast, bool animate)
        {
            lock (syncRoot)
            {
                if (!activeToasts.Remove(toast))
                {
                    return;
                }
            }

            if (!animate)
            {
                if (stack != null)
                {
                    stack.Children.Remove(toast);
                }

                UpdateHostPosition();
                CloseHostIfEmpty();
                return;
            }

            var opacity = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            var transform = new TranslateTransform(24, 0);
            toast.RenderTransform = transform;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            opacity.EasingFunction = ease;

            var move = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            move.EasingFunction = ease;
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, move);

            toast.BeginAnimation(UIElement.OpacityProperty, opacity);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (stack != null)
                {
                    stack.Children.Remove(toast);
                }

                UpdateHostPosition();
                CloseHostIfEmpty();
            };
            timer.Start();
        }

        /// <summary>
        /// 没有通知时关闭宿主窗口
        /// </summary>
        private static void CloseHostIfEmpty()
        {
            lock (syncRoot)
            {
                if (activeToasts.Count > 0)
                {
                    return;
                }
            }

            if (hostWindow != null)
            {
                hostWindow.Hide();
            }
        }

        /// <summary>
        /// 淡入动画
        /// </summary>
        private static void AnimateIn(AppToastView toast)
        {
            var transform = new TranslateTransform(-24, 0);
            toast.RenderTransform = transform;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            fade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            toast.BeginAnimation(UIElement.OpacityProperty, fade);

            var move = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            move.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, move);
        }
    }
}
