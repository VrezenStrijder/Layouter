using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Layouter.Models;
using Layouter.Services;
using Layouter.Utility;

namespace Layouter.Views
{
    /// <summary>
    /// 快捷键设置窗口
    /// </summary>
    /// <remarks>
    /// 每行对应一个功能（全局搜索、新建分区、自动排列图标、打开分区管理）。
    /// 用户在捕获区按下组合键后即时更新，确定时统一写入配置并立即重新注册。
    /// </remarks>
    public partial class HotkeySettingsWindow : Window
    {
        private readonly ObservableCollection<HotkeySettingItem> items = new ObservableCollection<HotkeySettingItem>();

        public HotkeySettingsWindow()
        {
            InitializeComponent();

            HotkeyList.ItemsSource = items;

            LoadItems();
        }

        /// <summary>
        /// 读取当前配置并填充列表
        /// </summary>
        private void LoadItems()
        {
            items.Clear();

            foreach (HotkeyAction action in HotkeyActionInfo.All)
            {
                items.Add(new HotkeySettingItem(action, GeneralSettingsService.Instance.GetHotkey(action)));
            }
        }

        /// <summary>
        /// 点击捕获区：让它获得焦点，之后的按键才会被记录
        /// </summary>
        private void CaptureBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindCaptureBox(e.OriginalSource) is FrameworkElement box)
            {
                box.Focus();
            }
        }

        /// <summary>
        /// 捕获区失去焦点：把临时显示的修饰键还原成已保存的快捷键
        /// </summary>
        private void CaptureBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (FindCaptureBox(e.OriginalSource) is FrameworkElement box
                && box.DataContext is HotkeySettingItem item)
            {
                item.RefreshDisplay();
            }
        }

        /// <summary>
        /// 录制快捷键
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is not FrameworkElement focused)
            {
                return;
            }

            if (focused.DataContext is not HotkeySettingItem item)
            {
                return;
            }

            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // 只按住修饰键时先显示出来，方便用户看清当前按住了什么
            if (IsModifierKey(key))
            {
                item.ShowPendingModifiers(GlobalHotkeyService.BuildGesture(Keyboard.Modifiers, Key.None));
                HintText.Text = "请继续按下主键（字母、数字或功能键）";
                return;
            }

            // Esc 放弃本次修改
            if (key == Key.Escape)
            {
                item.RefreshDisplay();
                HintText.Text = "快捷键至少需要包含一个修饰键（Ctrl / Alt / Shift / Win），否则会与日常输入冲突。";
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                item.ShowPendingModifiers("需要至少一个修饰键");
                HintText.Text = "直接按字母或数字会与日常输入冲突，请配合 Ctrl / Alt / Shift / Win 使用。";
                return;
            }

            item.SetGesture(GlobalHotkeyService.BuildGesture(Keyboard.Modifiers, key));
            HintText.Text = "点击「确定」保存这个快捷键。";
        }

        /// <summary>
        /// 找到捕获区控件
        /// </summary>
        private static FrameworkElement? FindCaptureBox(object? source)
        {
            DependencyObject? current = source as DependencyObject;

            while (current != null)
            {
                if (current is Border border && Equals(border.Tag, "HotkeyCapture"))
                {
                    return border;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static bool IsModifierKey(Key key)
        {
            switch (key)
            {
                case Key.LeftCtrl:
                case Key.RightCtrl:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LWin:
                case Key.RWin:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 恢复默认快捷键
        /// </summary>
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (HotkeySettingItem item in items)
            {
                item.SetGesture(HotkeyActionInfo.GetDefaultGesture(item.Action));
            }

            HintText.Text = "已恢复默认快捷键，点击「确定」保存。";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            // 同一组键不能绑定到两个功能，否则后注册的会注册失败
            var duplicates = items
                .Where(i => !string.IsNullOrEmpty(i.Gesture))
                .GroupBy(i => i.Gesture, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count > 0)
            {
                string names = string.Join("、", duplicates.SelectMany(g => g).Select(i => i.DisplayName));
                AppMessageBox.ShowWarning($"以下功能使用了相同的快捷键：{names}\n请修改后再保存。", "快捷键冲突");
                return;
            }

            foreach (HotkeySettingItem item in items)
            {
                GeneralSettingsService.Instance.SetHotkey(item.Action, item.Gesture);
            }

            DialogResult = true;
        }
    }
}
