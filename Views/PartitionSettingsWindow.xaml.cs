using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Layouter.Models;
using Layouter.Services;
using Layouter.ViewModels;
using Microsoft.Win32;
using ColorDialog = System.Windows.Forms.ColorDialog;

namespace Layouter.Views
{
    public partial class PartitionSettingsWindow : Window
    {
        private DesktopManagerViewModel vm;
        private bool isGlobalSettings;

        // 保存原始设置，用于取消或重置
        private SolidColorBrush originalTitleForeground;
        private SolidColorBrush originalTitleBackground;
        private FontFamily originalTitleFont;
        private HorizontalAlignment originalTitleAlignment;
        private SolidColorBrush originalContentBackground;
        private double originalTitleFontSize;
        private double originalOpacity;
        private double originalInactiveOpacity;
        private PartitionBackdrop originalBackdrop;
        private double originalTintOpacity;
        private Color? originalTintColorOverride;
        private IconSize originalIconSize;
        private double originalIconTextSize;

        /// <summary>编辑中的自定义色调，null 表示跟随标题栏背景色</summary>
        private Color? tintColorOverride;

        public PartitionSettingsWindow(DesktopManagerViewModel viewModel, bool isGlobalSettings = false)
        {
            InitializeComponent();

            vm = viewModel;
            this.isGlobalSettings = isGlobalSettings;

            // 保存原始设置
            originalTitleForeground = vm.TitleForeground.Clone();
            originalTitleBackground = vm.TitleBackground.Clone();
            originalTitleFont = vm.TitleFont;
            originalTitleAlignment = vm.TitleAlignment;
            originalTitleFontSize = vm.TitleFontSize;
            originalOpacity = vm.Opacity;
            originalInactiveOpacity = vm.InactiveOpacity;
            originalBackdrop = vm.Backdrop;
            originalTintOpacity = vm.TintOpacity;
            originalTintColorOverride = vm.TintColorOverride;
            originalIconSize = vm.IconSize;
            originalIconTextSize = vm.IconTextSize;

            // 初始化UI
            InitializeUI();

            // 生成预设主题按钮
            BuildPresetThemes();

            // 设置窗口标题
            this.Title = isGlobalSettings ? "全局分区设置" : "分区设置";
            HeaderTitleText.Text = this.Title;
        }

        #region 预设主题

        private readonly List<Button> presetThemeButtons = new List<Button>();

        /// <summary>
        /// 动态生成预设主题按钮
        /// </summary>
        private void BuildPresetThemes()
        {
            if (PresetThemePanel == null)
            {
                return;
            }

            foreach (var preset in PartitionThemePreset.All)
            {
                var button = new Button
                {
                    Tag = preset,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(8, 6, 8, 6),
                    Style = TryFindResource("AppGhostButton") as Style,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = preset.Description
                };

                button.Content = BuildPresetContent(preset);
                button.Click += PresetTheme_Click;

                presetThemeButtons.Add(button);
                PresetThemePanel.Children.Add(button);
            }
        }

        /// <summary>
        /// 构建预设主题按钮的内容（色块预览 + 名称）
        /// </summary>
        private static UIElement BuildPresetContent(PartitionThemePreset preset)
        {
            var panel = new StackPanel();

            var swatch = new Border
            {
                Width = 54,
                Height = 26,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE9)),
                BorderThickness = new Thickness(1),
                Child = new Border
                {
                    Background = new SolidColorBrush(preset.TitleBackground),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "T",
                        Foreground = new SolidColorBrush(preset.TitleForeground),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            panel.Children.Add(swatch);

            panel.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63))
            });

            return panel;
        }

        private void PresetTheme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PartitionThemePreset preset)
            {
                ApplyPreset(preset);
                HighlightSelectedPreset(button);
            }
        }

        /// <summary>
        /// 套用预设主题
        /// </summary>
        private void ApplyPreset(PartitionThemePreset preset)
        {
            if (preset == null)
            {
                return;
            }

            TitleForegroundPreview.Background = new SolidColorBrush(preset.TitleForeground);

            // 背景色不透明时按标题栏基础透明度处理，透明色则保持原样
            Color background = preset.TitleBackground;
            if (background.A == 255)
            {
                background.A = vm.TitleBaseAlpha;
            }
            TitleBackgroundPreview.Background = new SolidColorBrush(background);

            if (!string.IsNullOrEmpty(preset.TitleFont))
            {
                SelectFontByName(preset.TitleFont);
            }

            TitleFontSizeSlider.Value = Math.Min(Math.Max(preset.TitleFontSize, TitleFontSizeSlider.Minimum), TitleFontSizeSlider.Maximum);
            OpacitySlider.Value = Math.Min(Math.Max(preset.Opacity, OpacitySlider.Minimum), OpacitySlider.Maximum);
            // 未选中状态沿用同一套比例，保持预设主题的整体观感一致
            InactiveOpacitySlider.Value = Math.Min(Math.Max(OpacitySlider.Value * 0.7, InactiveOpacitySlider.Minimum), InactiveOpacitySlider.Maximum);
            TextSizeSlider.Value = Math.Min(Math.Max(preset.IconTextSize, TextSizeSlider.Minimum), TextSizeSlider.Maximum);

            switch (preset.IconSize)
            {
                case "Small":
                    IconSizeComboBox.SelectedIndex = 0;
                    break;
                case "Large":
                    IconSizeComboBox.SelectedIndex = 2;
                    break;
                default:
                    IconSizeComboBox.SelectedIndex = 1;
                    break;
            }

            switch (preset.TitleAlignment)
            {
                case "Center":
                    TitleAlignmentComboBox.SelectedIndex = 1;
                    break;
                case "Right":
                    TitleAlignmentComboBox.SelectedIndex = 2;
                    break;
                default:
                    TitleAlignmentComboBox.SelectedIndex = 0;
                    break;
            }

            RefreshTintPreview();
        }

        /// <summary>
        /// 高亮当前选中的预设主题
        /// </summary>
        private void HighlightSelectedPreset(Button selected)
        {
            foreach (var button in presetThemeButtons)
            {
                bool isSelected = ReferenceEquals(button, selected);
                button.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
                    : Brushes.Transparent;
            }
        }

        #endregion

        private void InitializeUI()
        {
            // 设置颜色预览
            TitleForegroundPreview.Background = vm.TitleForeground.Clone();
            TitleBackgroundPreview.Background = vm.TitleBackground.Clone();
            TitleFontSizeSlider.Value = vm.TitleFontSize;
            TitleFontSizeText.Text = vm.TitleFontSize.ToString("F1");
            TextSizeSlider.Value = vm.IconTextSize;

            // 设置字体下拉框
            foreach (var font in Fonts.SystemFontFamilies)
            {
                if (font.Source == vm.TitleFont.Source)
                {
                    TitleFontComboBox.SelectedItem = font;
                    break;
                }
            }

            // 设置对齐方式
            switch (vm.TitleAlignment)
            {
                case HorizontalAlignment.Left:
                    TitleAlignmentComboBox.SelectedIndex = 0;
                    break;
                case HorizontalAlignment.Center:
                    TitleAlignmentComboBox.SelectedIndex = 1;
                    break;
                case HorizontalAlignment.Right:
                    TitleAlignmentComboBox.SelectedIndex = 2;
                    break;
            }

            // 设置透明度
            OpacitySlider.Value = vm.Opacity;
            InactiveOpacitySlider.Value = vm.InactiveOpacity;

            // 设置背景材质
            switch (vm.Backdrop)
            {
                case PartitionBackdrop.Acrylic:
                    BackdropComboBox.SelectedIndex = 1;
                    break;
                default:
                    BackdropComboBox.SelectedIndex = 0;
                    break;
            }

            TintOpacitySlider.Value = vm.TintOpacity;

            // 自定义色调：null 时默认跟随标题栏
            tintColorOverride = vm.TintColorOverride;
            TintFollowTitleCheckBox.IsChecked = !tintColorOverride.HasValue;

            UpdateTintColorControls();
            BackdropComboBox_SelectionChanged(null, null);
            RefreshTintPreview();

            // 设置图标大小
            switch (vm.IconSize)
            {
                case IconSize.Small:
                    IconSizeComboBox.SelectedIndex = 0;
                    break;
                case IconSize.Medium:
                    IconSizeComboBox.SelectedIndex = 1;
                    break;
                case IconSize.Large:
                    IconSizeComboBox.SelectedIndex = 2;
                    break;
            }

        }

        private void TitleForegroundButton_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new ColorDialog();
            var currentColor = ((SolidColorBrush)TitleForegroundPreview.Background).Color;
            colorDialog.Color = System.Drawing.Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var newColor = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                TitleForegroundPreview.Background = new SolidColorBrush(newColor);
            }
        }

        private void TitleBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new ColorDialog();
            colorDialog.Color = System.Drawing.Color.FromArgb(
                ((SolidColorBrush)TitleBackgroundPreview.Background).Color.R,
                ((SolidColorBrush)TitleBackgroundPreview.Background).Color.G,
                ((SolidColorBrush)TitleBackgroundPreview.Background).Color.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // 创建带透明度的颜色
                Color c = Color.FromRgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);

                // 应用基础透明度
                c.A = vm.TitleBaseAlpha;

                TitleBackgroundPreview.Background = new SolidColorBrush(c);
                RefreshTintPreview();
            }

        }


        private void TitleFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void TitleAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PreviewBorder == null)
            {
                return;
            }
            PreviewBorder.Opacity = OpacitySlider.Value;
        }

        /// <summary>
        /// 未选中状态下的透明度变化：同步右侧预览
        /// </summary>
        private void InactiveOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (InactivePreviewBorder == null)
            {
                return;
            }
            InactivePreviewBorder.Opacity = InactiveOpacitySlider.Value;
        }

        /// <summary>
        /// 背景材质变化：没有材质时把色调相关控件置灰
        /// </summary>
        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TintPreview == null)
            {
                return;
            }

            bool enabled = GetSelectedBackdrop() != PartitionBackdrop.None;

            TintOpacitySlider.IsEnabled = enabled;
            TintFollowTitleCheckBox.IsEnabled = enabled;
            TintColorButton.IsEnabled = enabled && TintFollowTitleCheckBox.IsChecked == false;
            TintPreview.Opacity = enabled ? 1.0 : 0.4;
        }

        /// <summary>
        /// 色调浓度变化：同步色块预览
        /// </summary>
        private void TintOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshTintPreview();
        }

        /// <summary>
        /// 读取当前选中的背景材质
        /// </summary>
        private PartitionBackdrop GetSelectedBackdrop()
        {
            if (BackdropComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse(tag, out PartitionBackdrop parsed))
                {
                    return parsed;
                }
            }

            return PartitionBackdrop.None;
        }

        /// <summary>
        /// 刷新色调预览：自定义色优先，否则跟随标题栏背景色
        /// </summary>
        /// <remarks>
        /// 窗口还在 XAML 解析阶段时，下方的浓度滑块可能尚未创建，
        /// 此时直接返回，等 InitializeUI 统一刷新。
        /// </remarks>
        private void RefreshTintPreview()
        {
            if (TintPreview == null || TitleBackgroundPreview == null || TintOpacitySlider == null)
            {
                return;
            }

            Color color = tintColorOverride ?? GetTitleBarColor();
            TintPreview.Background = BuildTintBrush(color, TintOpacitySlider.Value);
        }

        /// <summary>
        /// 读取标题栏预览颜色
        /// </summary>
        private Color GetTitleBarColor()
        {
            return TitleBackgroundPreview?.Background is SolidColorBrush brush
                ? brush.Color
                : Colors.DodgerBlue;
        }

        /// <summary>
        /// 跟随标题栏开关变化
        /// </summary>
        private void TintFollowTitle_Changed(object sender, RoutedEventArgs e)
        {
            if (TintFollowTitleCheckBox == null)
            {
                return;
            }

            if (TintFollowTitleCheckBox.IsChecked == true)
            {
                // 恢复跟随：清除自定义色
                tintColorOverride = null;
            }
            else if (!tintColorOverride.HasValue)
            {
                // 从跟随切换到自定义时，以当前标题栏颜色作为起点
                tintColorOverride = GetTitleBarColor();
            }

            UpdateTintColorControls();
            RefreshTintPreview();
        }

        /// <summary>
        /// 选择自定义色调
        /// </summary>
        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (TintFollowTitleCheckBox.IsChecked != false)
            {
                TintFollowTitleCheckBox.IsChecked = false;
            }

            Color current = tintColorOverride ?? GetTitleBarColor();

            using (var colorDialog = new ColorDialog())
            {
                colorDialog.FullOpen = true;
                colorDialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                tintColorOverride = Color.FromRgb(
                    colorDialog.Color.R,
                    colorDialog.Color.G,
                    colorDialog.Color.B);

                UpdateTintColorControls();
                RefreshTintPreview();
            }
        }

        /// <summary>
        /// 按「是否跟随标题栏」更新选色按钮的可用状态
        /// </summary>
        private void UpdateTintColorControls()
        {
            if (TintColorButton == null || TintFollowTitleCheckBox == null)
            {
                return;
            }

            TintColorButton.IsEnabled = TintFollowTitleCheckBox.IsChecked != true;
        }

        /// <summary>
        /// 生成色调预览用的画刷
        /// </summary>
        private static SolidColorBrush BuildTintBrush(Color color, double opacity)
        {
            double clamped = Math.Clamp(opacity, 0, 1);
            byte alpha = (byte)Math.Round(clamped * 255);
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private void TitleFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)        {
            if (TitleFontSizeText != null)
            {
                TitleFontSizeText.Text = e.NewValue.ToString("F1");
            }
        }

        private void IconSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 预览更新
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 重置为默认值
            TitleForegroundPreview.Background = new SolidColorBrush(Colors.White);
            TitleBackgroundPreview.Background = new SolidColorBrush(Colors.DodgerBlue);
            SelectFontByName("Microsoft YaHei");
            TitleFontSizeSlider.Value = 14d;
            TitleAlignmentComboBox.SelectedIndex = 0; // 左对齐
            OpacitySlider.Value = 0.90;
            InactiveOpacitySlider.Value = 0.63;
            BackdropComboBox.SelectedIndex = 0; // 无背景材质
            TintOpacitySlider.Value = 0.05;
            tintColorOverride = null;
            TintFollowTitleCheckBox.IsChecked = true;
            UpdateTintColorControls();
            RefreshTintPreview();
            IconSizeComboBox.SelectedIndex = 1; // 中等大小
            TextSizeSlider.Value = 12;
        }

        /// <summary>
        /// 在字体下拉框中选中指定字体
        /// </summary>
        private void SelectFontByName(string fontName)
        {
            if (TitleFontComboBox == null || string.IsNullOrEmpty(fontName))
            {
                return;
            }

            foreach (var font in Fonts.SystemFontFamilies)
            {
                if (string.Equals(font.Source, fontName, StringComparison.OrdinalIgnoreCase))
                {
                    TitleFontComboBox.SelectedItem = font;
                    return;
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 应用设置到ViewModel
            vm.TitleForeground = ((SolidColorBrush)TitleForegroundPreview.Background).Clone();
            vm.TitleBackground = ((SolidColorBrush)TitleBackgroundPreview.Background).Clone();
            vm.TitleFont = (FontFamily)TitleFontComboBox.SelectedItem;

            // 设置对齐方式
            switch (TitleAlignmentComboBox.SelectedIndex)
            {
                case 0:
                    vm.TitleAlignment = HorizontalAlignment.Left;
                    break;
                case 1:
                    vm.TitleAlignment = HorizontalAlignment.Center;
                    break;
                case 2:
                    vm.TitleAlignment = HorizontalAlignment.Right;
                    break;
            }

            vm.TitleFontSize = TitleFontSizeSlider.Value;
            vm.Opacity = OpacitySlider.Value;
            vm.InactiveOpacity = InactiveOpacitySlider.Value;

            // 背景材质
            vm.Backdrop = GetSelectedBackdrop();
            vm.TintOpacity = TintOpacitySlider.Value;
            vm.TintColorOverride = tintColorOverride;

            // 设置图标大小
            switch (IconSizeComboBox.SelectedIndex)
            {
                case 0:
                    vm.IconSize = IconSize.Small;
                    break;
                case 1:
                    vm.IconSize = IconSize.Medium;
                    break;
                case 2:
                    vm.IconSize = IconSize.Large;
                    break;
            }

            //设置图标文字大小
            vm.IconTextSize = TextSizeSlider.Value;
            foreach (var icon in vm.Icons)
            {
                icon.TextSize = vm.IconTextSize;
            }
            
            if (!string.IsNullOrEmpty(vm.windowId))
            {
                // 保存设置到配置文件
                PartitionSettingsService.Instance.SaveSettings(vm, isGlobalSettings);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 恢复原始设置
            vm.TitleForeground = originalTitleForeground;
            vm.TitleBackground = originalTitleBackground;
            vm.TitleFont = originalTitleFont;
            vm.TitleAlignment = originalTitleAlignment;
            vm.TitleFontSize = originalTitleFontSize;
            vm.Opacity = originalOpacity;
            vm.InactiveOpacity = originalInactiveOpacity;
            vm.Backdrop = originalBackdrop;
            vm.TintOpacity = originalTintOpacity;
            vm.TintColorOverride = originalTintColorOverride;
            vm.IconSize = originalIconSize;
            vm.IconTextSize = originalIconTextSize;

            DialogResult = false;
            Close();
        }

    }
}