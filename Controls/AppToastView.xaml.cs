using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Layouter.Controls
{
    /// <summary>
    /// 通知卡片的语义类型
    /// </summary>
    public enum ToastKind
    {
        /// <summary>信息</summary>
        Info,
        /// <summary>成功</summary>
        Success,
        /// <summary>警告</summary>
        Warning,
        /// <summary>错误</summary>
        Error
    }

    /// <summary>
    /// 轻量通知卡片：左侧语义色细条 + 圆形图标徽章 + 标题/正文/时间戳
    /// </summary>
    public partial class AppToastView : UserControl
    {
        /// <summary>
        /// 通知被点击或自动关闭时触发
        /// </summary>
        public event EventHandler? Dismissed;

        public AppToastView()
        {
            InitializeComponent();

            // 点击整张卡片即可关闭
            CardBorder.MouseLeftButtonUp += (s, e) => Dismissed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 应用通知内容与语义配色
        /// </summary>
        /// <param name="kind">语义类型</param>
        /// <param name="title">标题</param>
        /// <param name="message">正文（可为空）</param>
        public void Apply(ToastKind kind, string title, string? message)
        {
            Brush accent = ResolveBrush(kind);

            BadgeEllipse.Fill = accent;
            AccentStripe.Background = accent;
            BadgeIcon.Symbol = ResolveSymbol(kind);

            TitleText.Text = title ?? string.Empty;

            if (string.IsNullOrWhiteSpace(message))
            {
                MessageText.Visibility = Visibility.Collapsed;
                MessageText.Text = string.Empty;
            }
            else
            {
                MessageText.Visibility = Visibility.Visible;
                MessageText.Text = message;
            }

            TimestampText.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        }

        /// <summary>
        /// 触发关闭事件
        /// </summary>
        public void NotifyDismissed()
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private static Brush ResolveBrush(ToastKind kind)
        {
            var resources = Application.Current?.TryFindResource("AppSurfaceBrush") as ResourceDictionary;

            string key = kind switch
            {
                ToastKind.Success => "AppSuccessBrush",
                ToastKind.Warning => "AppWarningBrush",
                ToastKind.Error => "AppDangerBrush",
                _ => "AppAccentBrush"
            };

            if (Application.Current != null)
            {
                var brush = Application.Current.TryFindResource(key) as Brush;
                if (brush != null)
                {
                    return brush;
                }
            }

            // 资源缺失时的兜底配色
            return kind switch
            {
                ToastKind.Success => new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                ToastKind.Warning => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                ToastKind.Error => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                _ => new SolidColorBrush(Color.FromRgb(37, 99, 235))
            };
        }

        private static FluentIcons.Common.Symbol ResolveSymbol(ToastKind kind)
        {
            return kind switch
            {
                ToastKind.Success => FluentIcons.Common.Symbol.Checkmark,
                ToastKind.Warning => FluentIcons.Common.Symbol.Warning,
                ToastKind.Error => FluentIcons.Common.Symbol.ErrorCircle,
                _ => FluentIcons.Common.Symbol.Info
            };
        }
    }
}
