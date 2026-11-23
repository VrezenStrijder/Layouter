using CommunityToolkit.Mvvm.ComponentModel;
using Layouter.Services;

namespace Layouter.Models
{
    /// <summary>
    /// 快捷键设置窗口中的一行
    /// </summary>
    /// <remarks>
    /// 每一行对应一个功能：左边是功能名称，右边是快捷键捕获区。
    /// 用户按下组合键后即时更新 <see cref="GestureText"/>，
    /// 点「确定」时统一写入配置。
    /// </remarks>
    public class HotkeySettingItem : ObservableObject
    {
        private string gestureText;

        public HotkeySettingItem(HotkeyAction action, string gesture)
        {
            Action = action;
            DisplayName = HotkeyActionInfo.GetDisplayName(action);
            Description = HotkeyActionInfo.GetDescription(action);

            SetGesture(gesture);
        }

        /// <summary>对应的功能</summary>
        public HotkeyAction Action { get; }

        /// <summary>功能名称</summary>
        public string DisplayName { get; }

        /// <summary>功能说明</summary>
        public string Description { get; }

        /// <summary>当前快捷键描述（为空表示未设置）</summary>
        public string Gesture { get; private set; }

        /// <summary>捕获区显示的文本</summary>
        public string GestureText
        {
            get => gestureText;
            private set => SetProperty(ref gestureText, value);
        }

        /// <summary>
        /// 更新快捷键
        /// </summary>
        /// <param name="gesture">快捷键描述，如 "Ctrl+Alt+S"</param>
        public void SetGesture(string gesture)
        {
            Gesture = gesture?.Trim() ?? string.Empty;
            RefreshDisplay();
        }

        /// <summary>
        /// 按当前快捷键刷新捕获区的显示
        /// </summary>
        public void RefreshDisplay()
        {
            GestureText = string.IsNullOrEmpty(Gesture)
                ? "未设置"
                : GlobalHotkeyService.FormatGesture(Gesture);
        }

        /// <summary>
        /// 临时显示一段提示文字（例如只按下修饰键时的状态）
        /// </summary>
        /// <remarks>
        /// 这里只改显示不改 <see cref="Gesture"/>，
        /// 录制完成或捕获区失去焦点后会恢复成已保存的快捷键。
        /// </remarks>
        /// <param name="text">提示文字</param>
        public void ShowPendingModifiers(string text)
        {
            GestureText = text;
        }
    }
}
