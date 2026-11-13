using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Layouter.Services
{
    /// <summary>
    /// 桌面窗口扫描器。
    /// <para>
    /// 用于「双击桌面隐藏元素」功能：找出桌面上真实可见、且可以安全隐藏 / 还原的第三方窗口。
    /// </para>
    /// <para>
    /// 判定规则偏保守，宁可少隐藏也不误伤系统外壳：
    /// <list type="bullet">
    /// <item>排除本程序自己的窗口（分区格子由上层单独处理）；</item>
    /// <item>排除系统外壳窗口（桌面背景、任务栏、开始菜单、输入法、飞行窗等）；</item>
    /// <item>排除带所有者窗口的辅助窗口（工具提示、右键菜单等）；</item>
    /// <item>排除被 DWM 标记为「已隐藏」的窗口（其它虚拟桌面、UWP 挂起窗口）；</item>
    /// <item>排除不可见、已最小化的窗口。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class DesktopWindowScanner
    {
        /// <summary>DWM 窗口属性：窗口是否被桌面窗口管理器隐藏</summary>
        private const int DWMWA_CLOAKED = 14;

        /// <summary>获取窗口的根祖先（顶层窗口）</summary>
        private const uint GA_ROOT = 2;

        /// <summary>获取窗口的所有者</summary>
        private const uint GW_OWNER = 4;

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        /// <summary>
        /// 系统外壳窗口的类名。
        /// 这些窗口属于壁纸、任务栏、开始菜单等「不可交互元素」或系统外壳，隐藏它们没有意义且容易出问题。
        /// </summary>
        private static readonly HashSet<string> ShellWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",                              // 桌面背景窗口
            "WorkerW",                              // 桌面图层宿主
            "SHELLDLL_DefView",                     // 桌面图标视图
            "Shell_TrayWnd",                        // 主任务栏
            "Shell_SecondaryTrayWnd",               // 副屏任务栏
            "ShellChrome_Window",                   // 资源管理器外壳
            "Windows.UI.Core.CoreWindow",           // 开始菜单 / 搜索 / 商店应用宿主
            "Xaml_WindowedPopupClass",              // 通知中心、飞行窗
            "Windows.UI.Composition.DesktopWindowContentBridge",
            "ForegroundStaging",                    // 开始菜单过渡层
            "MultitaskingViewFrame",                // 多任务视图
            "TaskListThumbnailWnd",                 // 任务栏缩略图
            "IME",                                  // 输入法候选窗
            "MSCTFIME UI",
            "Default IME",
            "SysShadow",                            // 窗口阴影
            "tooltips_class32",                     // 工具提示
            "CicMarshalWnd",                        // 输入法辅助窗
            "DV2ControlHost"                        // 任务栏 DirectUI 宿主
        };

        /// <summary>
        /// 枚举桌面上「可见且可以安全隐藏」的第三方窗口
        /// </summary>
        /// <param name="desktopView">桌面图标视图句柄（需要排除）</param>
        /// <returns>可隐藏的窗口句柄列表</returns>
        public List<IntPtr> EnumerateHideableWindows(IntPtr desktopView)
        {
            var result = new List<IntPtr>();
            uint selfProcessId = (uint)Process.GetCurrentProcess().Id;

            Win32.EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    if (hwnd == IntPtr.Zero || hwnd == desktopView)
                    {
                        return true;
                    }

                    if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd))
                    {
                        return true;
                    }

                    // 只处理顶层窗口
                    if (Win32.GetAncestor(hwnd, GA_ROOT) != hwnd)
                    {
                        return true;
                    }

                    // 带所有者的窗口多为工具提示、右键菜单等辅助窗口，不处理
                    if (Win32.GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
                    {
                        return true;
                    }

                    // 本程序自己的窗口由上层单独处理
                    Win32.GetWindowThreadProcessId(hwnd, out uint processId);
                    if (processId == selfProcessId)
                    {
                        return true;
                    }

                    // 被 DWM 标记为隐藏的窗口本来就不在桌面上
                    if (IsCloaked(hwnd))
                    {
                        return true;
                    }

                    // 系统外壳窗口保持可见
                    if (IsShellWindow(hwnd))
                    {
                        return true;
                    }

                    result.Add(hwnd);
                }
                catch
                {
                    // 单个窗口判断失败不影响其它窗口
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        /// <summary>
        /// 判断窗口是否属于系统外壳
        /// </summary>
        public static bool IsShellWindow(IntPtr hwnd)
        {
            string className = GetClassName(hwnd);

            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            return ShellWindowClasses.Contains(className);
        }

        /// <summary>
        /// 获取窗口类名
        /// </summary>
        public static string GetClassName(IntPtr hwnd)
        {
            try
            {
                var builder = new StringBuilder(256);
                int length = Win32.GetClassName(hwnd, builder, builder.Capacity);

                return length > 0 ? builder.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 判断窗口是否被 DWM 隐藏（跨虚拟桌面、UWP 应用挂起等场景）
        /// </summary>
        private static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked;
                int result = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));

                return result == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断窗口句柄是否仍然有效
        /// </summary>
        public static bool IsValidWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && IsWindow(hwnd);
        }
    }
}
