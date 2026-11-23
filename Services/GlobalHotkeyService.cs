using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;
using Layouter.Models;
using Layouter.Utility;

namespace Layouter.Services
{
    /// <summary>
    /// 全局快捷键服务
    /// </summary>
    /// <remarks>
    /// 基于 Win32 <c>RegisterHotKey</c> 实现：在进程内创建一个「消息专用窗口」，
    /// 由它接收 <c>WM_HOTKEY</c> 消息。消息专用窗口不会出现在任务栏或 Alt+Tab 中。
    /// <para>
    /// 支持同时注册多个快捷键：每个功能使用独立的窗口消息编号，
    /// 收到消息后按编号还原出对应的功能再分发。
    /// </para>
    /// </remarks>
    public class GlobalHotkeyService
    {
        private static readonly Lazy<GlobalHotkeyService> instance = new Lazy<GlobalHotkeyService>(() => new GlobalHotkeyService());
        public static GlobalHotkeyService Instance => instance.Value;

        /// <summary>快捷键消息编号的起始值</summary>
        private const int HotkeyIdBase = 0xA17;

        private const int WM_HOTKEY = 0x0312;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? hwndSource;

        /// <summary>已注册的窗口消息编号 → 对应功能</summary>
        private readonly Dictionary<int, HotkeyAction> registeredIds = new Dictionary<int, HotkeyAction>();

        /// <summary>功能 → 当前快捷键描述</summary>
        private readonly Dictionary<HotkeyAction, string> currentGestures = new Dictionary<HotkeyAction, string>();

        private GlobalHotkeyService()
        {
        }

        /// <summary>快捷键被按下时触发（参数为触发的功能）</summary>
        public event EventHandler<HotkeyAction>? Triggered;

        /// <summary>
        /// 按当前配置重新注册全部全局快捷键
        /// </summary>
        /// <remarks>
        /// 设置变更后调用：先注销旧的，再按新配置注册。
        /// </remarks>
        public static void RegisterConfiguredHotkeys()
        {
            Instance.RegisterAll(GeneralSettingsService.Instance.GetAllHotkeys());
        }

        /// <summary>
        /// 获取某个功能当前已注册的快捷键描述
        /// </summary>
        public string? GetGesture(HotkeyAction action)
        {
            return currentGestures.TryGetValue(action, out string? gesture) ? gesture : null;
        }

        /// <summary>
        /// 注册一组全局快捷键（会先注销旧的）
        /// </summary>
        /// <param name="gestures">功能 → 快捷键描述</param>
        /// <returns>是否全部注册成功</returns>
        public bool RegisterAll(IReadOnlyDictionary<HotkeyAction, string> gestures)
        {
            Unregister();

            if (gestures == null || gestures.Count == 0)
            {
                return false;
            }

            try
            {
                EnsureMessageWindow();

                if (hwndSource == null)
                {
                    return false;
                }

                bool allSucceeded = true;

                foreach (KeyValuePair<HotkeyAction, string> entry in gestures)
                {
                    string gesture = entry.Value?.Trim() ?? string.Empty;

                    if (gesture.Length == 0)
                    {
                        // 空描述表示该功能不设置快捷键
                        continue;
                    }

                    if (!TryParse(gesture, out uint modifiers, out Key key))
                    {
                        Log.Information($"快捷键描述无法解析: {gesture}");
                        allSucceeded = false;
                        continue;
                    }

                    int id = HotkeyIdBase + (int)entry.Key;

                    uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

                    if (!RegisterHotKey(hwndSource.Handle, id, modifiers | MOD_NOREPEAT, virtualKey))
                    {
                        int error = Marshal.GetLastWin32Error();
                        Log.Information($"注册全局快捷键失败({HotkeyActionInfo.GetDisplayName(entry.Key)} {gesture})，错误码 {error}");
                        allSucceeded = false;
                        continue;
                    }

                    registeredIds[id] = entry.Key;
                    currentGestures[entry.Key] = gesture;

                    Log.Information($"已注册全局快捷键: {HotkeyActionInfo.GetDisplayName(entry.Key)} = {gesture}");
                }

                return allSucceeded;
            }
            catch (Exception ex)
            {
                Log.Information($"注册全局快捷键时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 注销全部已注册的快捷键
        /// </summary>
        public void Unregister()
        {
            try
            {
                if (hwndSource != null && registeredIds.Count > 0)
                {
                    foreach (int id in registeredIds.Keys)
                    {
                        UnregisterHotKey(hwndSource.Handle, id);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"注销全局快捷键时出错: {ex.Message}");
            }
            finally
            {
                registeredIds.Clear();
                currentGestures.Clear();
            }
        }

        /// <summary>
        /// 创建消息专用窗口，用于接收 WM_HOTKEY
        /// </summary>
        private void EnsureMessageWindow()
        {
            if (hwndSource != null)
            {
                return;
            }

            var parameters = new HwndSourceParameters("LayouterGlobalHotkey")
            {
                // HWND_MESSAGE：消息专用窗口，不显示、不参与任务栏
                ParentWindow = new IntPtr(-3),
                WindowStyle = 0
            };

            hwndSource = new HwndSource(parameters);
            hwndSource.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                if (registeredIds.TryGetValue(id, out HotkeyAction action))
                {
                    handled = true;
                    Triggered?.Invoke(this, action);
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 解析快捷键描述
        /// </summary>
        /// <param name="gesture">如 "Ctrl+Alt+S"</param>
        /// <param name="modifiers">修饰键标志</param>
        /// <param name="key">主键</param>
        public static bool TryParse(string gesture, out uint modifiers, out Key key)
        {
            modifiers = 0;
            key = Key.None;

            if (string.IsNullOrWhiteSpace(gesture))
            {
                return false;
            }

            string[] parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "alt":
                        modifiers |= MOD_ALT;
                        break;
                    case "shift":
                        modifiers |= MOD_SHIFT;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= MOD_WIN;
                        break;
                }
            }

            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, ignoreCase: true, out key))
            {
                // 兼容单字符写法
                if (keyText.Length == 1)
                {
                    key = (Key)Enum.Parse(typeof(Key), char.ToUpperInvariant(keyText[0]).ToString());
                }
                else
                {
                    key = Key.None;
                }
            }

            return key != Key.None;
        }

        /// <summary>
        /// 把快捷键描述格式化为易读的文本
        /// </summary>
        public static string FormatGesture(string gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture))
            {
                return "未设置";
            }

            string[] parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" + ");
                }

                string part = parts[i].Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        builder.Append("Ctrl");
                        break;
                    case "alt":
                        builder.Append("Alt");
                        break;
                    case "shift":
                        builder.Append("Shift");
                        break;
                    case "win":
                    case "windows":
                        builder.Append("Win");
                        break;
                    default:
                        builder.Append(part.ToUpperInvariant());
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 由按键信息生成快捷键描述
        /// </summary>
        public static string BuildGesture(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            if (key != Key.None)
            {
                parts.Add(key.ToString());
            }

            return string.Join("+", parts);
        }
    }
}
