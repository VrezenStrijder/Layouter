using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FluentIcons.Common;

namespace Layouter.Views
{
    /// <summary>
    /// 主题化消息对话框。
    /// <para>
    /// 与 <see cref="Utility.AppMessageBox"/> 配合使用，对外表现与
    /// <see cref="System.Windows.MessageBox"/> 保持一致（返回 <see cref="MessageBoxResult"/>），
    /// 但视觉上使用应用自身的设计令牌，避免系统默认对话框与整体风格割裂。
    /// </para>
    /// </summary>
    public partial class AppMessageBoxWindow : Window
    {
        private MessageBoxResult dialogResult = MessageBoxResult.None;

        public AppMessageBoxWindow()
        {
            InitializeComponent();

            // 允许点击遮罩区域之外关闭（与系统对话框行为一致）
            RootBorder.MouseLeftButtonDown += RootBorder_MouseLeftButtonDown;
        }

        /// <summary>
        /// 设置对话框内容
        /// </summary>
        /// <param name="message">正文</param>
        /// <param name="caption">标题</param>
        /// <param name="button">按钮组合</param>
        /// <param name="image">语义图标</param>
        public void Configure(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            MessageText.Text = message ?? string.Empty;
            TitleText.Text = string.IsNullOrWhiteSpace(caption) ? "提示" : caption;
            Title = TitleText.Text;

            ApplyImage(image);
            ApplyButtons(button);
        }

        /// <summary>
        /// 对话框关闭后返回的结果
        /// </summary>
        public MessageBoxResult DialogResult { get => dialogResult; set => dialogResult = value; }

        /// <summary>
        /// 应用语义图标与配色
        /// </summary>
        private void ApplyImage(MessageBoxImage image)
        {
            Brush accent;
            Symbol symbol;

            switch (image)
            {
                case MessageBoxImage.Error:
                    accent = ResolveBrush("AppDangerBrush", Color.FromRgb(220, 38, 38));
                    symbol = Symbol.ErrorCircle;
                    break;
                case MessageBoxImage.Warning:
                    accent = ResolveBrush("AppWarningBrush", Color.FromRgb(217, 119, 6));
                    symbol = Symbol.Warning;
                    break;
                case MessageBoxImage.Question:
                    accent = ResolveBrush("AppAccentBrush", Color.FromRgb(37, 99, 235));
                    symbol = Symbol.Question;
                    break;
                case MessageBoxImage.None:
                    accent = ResolveBrush("AppMutedTextBrush", Color.FromRgb(107, 114, 128));
                    symbol = Symbol.Info;
                    break;
                case MessageBoxImage.Information:
                default:
                    accent = ResolveBrush("AppAccentBrush", Color.FromRgb(37, 99, 235));
                    symbol = Symbol.Info;
                    break;
            }

            BadgeEllipse.Fill = accent;
            BadgeIcon.Symbol = symbol;
        }

        /// <summary>
        /// 按按钮组合配置按钮行
        /// </summary>
        private void ApplyButtons(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    ShowCancel(false);
                    SetOk("确定", MessageBoxResult.OK, isDestructive: false);
                    break;

                case MessageBoxButton.OKCancel:
                    ShowCancel(true);
                    SetCancel("取消", MessageBoxResult.Cancel);
                    SetOk("确定", MessageBoxResult.OK, isDestructive: false);
                    break;

                case MessageBoxButton.YesNo:
                    ShowCancel(true);
                    SetCancel("否", MessageBoxResult.No);
                    SetOk("是", MessageBoxResult.Yes, isDestructive: false);
                    break;

                case MessageBoxButton.YesNoCancel:
                    ShowCancel(true);
                    SetCancel("取消", MessageBoxResult.Cancel);
                    SetOk("是", MessageBoxResult.Yes, isDestructive: false);
                    break;

                default:
                    ShowCancel(false);
                    SetOk("确定", MessageBoxResult.OK, isDestructive: false);
                    break;
            }
        }

        /// <summary>
        /// 标记「确定」按钮为破坏性操作（红色）
        /// </summary>
        public void MarkOkAsDestructive()
        {
            OkButton.Style = null;
            OkButton.Background = ResolveBrush("AppDangerBrush", Color.FromRgb(220, 38, 38));
            OkButton.Foreground = Brushes.White;
            OkButton.BorderBrush = ResolveBrush("AppDangerBrush", Color.FromRgb(220, 38, 38));
            OkButton.BorderThickness = new Thickness(1);
            OkButton.Margin = new Thickness(0);
            OkButton.MinWidth = 88;
            OkButton.Padding = new Thickness(14, 6, 14, 6);
            OkButton.Cursor = System.Windows.Input.Cursors.Hand;
        }

        private void SetOk(string text, MessageBoxResult result, bool isDestructive)
        {
            OkButton.Content = text;
            OkButton.Tag = result;
            OkButton.Visibility = Visibility.Visible;
            OkButton.IsDefault = true;

            if (isDestructive)
            {
                MarkOkAsDestructive();
            }
        }

        private void SetCancel(string text, MessageBoxResult result)
        {
            CancelButton.Content = text;
            CancelButton.Tag = result;
            CancelButton.Visibility = Visibility.Visible;
        }

        private void ShowCancel(bool visible)
        {
            CancelButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static Brush ResolveBrush(string resourceKey, Color fallback)
        {
            var brush = Application.Current?.TryFindResource(resourceKey) as Brush;
            return brush ?? new SolidColorBrush(fallback);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 双击标题区域无特殊行为，保持简单
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = OkButton.Tag is MessageBoxResult okResult ? okResult : MessageBoxResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = CancelButton.Tag is MessageBoxResult cancelResult ? cancelResult : MessageBoxResult.Cancel;
            Close();
        }
    }
}
