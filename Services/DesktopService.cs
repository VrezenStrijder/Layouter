using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Layouter.Utility;

namespace Layouter.Services
{
    /// <summary>
    /// 桌面双击事件服务。
    /// <para>
    /// 采用「全局低级鼠标钩子（WH_MOUSE_LL）+ 隐藏桌面图标视图」的方案：
    /// 低级钩子的回调始终在当前进程内执行，不会去改写其它进程的窗口过程，
    /// 因此既不会让资源管理器失去响应，也不会在资源管理器重启后失效。
    /// </para>
    /// <para>
    /// 隐藏动作通过 <c>ShowWindow(SysListView32, SW_HIDE)</c> 完成：
    /// 只是把桌面图标视图临时隐藏，桌面上的文件一个都不会被移动，
    /// 因此可以瞬时切换、完整还原，也不存在任何文件丢失的风险。
    /// </para>
    /// <para>
    /// 启用后，用户在桌面空白处双击即可隐藏 / 还原全部桌面图标；
    /// 双击图标本身仍然用于打开文件，不受影响。
    /// </para>
    /// </summary>
    public class DesktopService
    {
        private static readonly Lazy<DesktopService> instance = new Lazy<DesktopService>(() => new DesktopService());
        public static DesktopService Instance => instance.Value;

        /// <summary>低级鼠标钩子委托</summary>
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            /// <summary>屏幕坐标。必须是两个 int，不能用 WPF 的 Point（那是两个 double）</summary>
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        /// <summary>双击判定矩形的宽度</summary>
        private const int SM_CXDOUBLECLK = 44;

        /// <summary>双击判定矩形的高度</summary>
        private const int SM_CYDOUBLECLK = 45;

        /// <summary>ListView 命中测试消息，用于判断双击是否落在图标上</summary>
        /// <remarks>LVM_HITTEST = LVM_FIRST(0x1000) + 12；不要误用 LVM_HITTESTLV(0x1012)，后者需要跨进程传指针。</remarks>
        private const int LVM_HITTEST = 0x100C;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        /// <summary>两次切换之间的最小间隔（毫秒），避免连点误触</summary>
        private const int TOGGLE_COOLDOWN_MS = 1200;

        /// <summary>看门狗间隔（毫秒），用于在资源管理器重启后自动恢复隐藏状态</summary>
        private const int WATCHDOG_INTERVAL_MS = 2000;

        private IntPtr hookHandle = IntPtr.Zero;
        private LowLevelMouseProc hookProc;
        private DateTime lastDownTime = DateTime.MinValue;
        private DateTime suppressUntil = DateTime.MinValue;
        private DateTime lastToggleTime = DateTime.MinValue;
        private Point lastDownPoint;
        private bool isHooked;

        private DispatcherTimer watchdogTimer;

        /// <summary>
        /// 是否已经挂钩
        /// </summary>
        public bool IsHooked => isHooked;

        /// <summary>
        /// 桌面图标当前是否处于隐藏状态
        /// </summary>
        public bool DesktopIconsHidden { get; private set; }

        /// <summary>
        /// 注册桌面双击事件
        /// </summary>
        public void RegisterDesktopDoubleClickEvent()
        {
            try
            {
                if (isHooked)
                {
                    StartWatchdog();
                    return;
                }

                if (!InstallHook())
                {
                    Log.Information("安装鼠标钩子失败，桌面双击功能不可用");
                    return;
                }

                // 启动时统一恢复为「显示」状态：
                // 无论是上次退出前隐藏的，还是异常退出残留的，都先恢复显示，
                // 避免用户面对一个不知原因的空桌面
                RestoreDesktopIcons();

                StartWatchdog();

                Log.Information("已注册桌面双击事件");
            }
            catch (Exception ex)
            {
                Log.Information($"注册桌面双击事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注销桌面双击事件
        /// </summary>
        public void UnregisterDesktopDoubleClickEvent()
        {
            try
            {
                StopWatchdog();
                RemoveHook();

                // 退出程序时把桌面图标恢复显示，避免用户面对一个「空桌面」而不知原因
                RestoreDesktopIcons();

                Log.Information("已注销桌面双击事件");
            }
            catch (Exception ex)
            {
                Log.Information($"注销桌面双击事件失败: {ex.Message}");
            }
        }

        private bool InstallHook()
        {
            RemoveHook();

            hookProc = LowLevelMouseProcCallback;

            // 低级钩子的回调位于当前进程内，模块句柄按文档必须传 NULL；
            // 某些环境下传当前模块句柄反而会安装失败，故两种方式依次尝试。
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, IntPtr.Zero, 0);
            if (hookHandle == IntPtr.Zero)
            {
                int firstError = Marshal.GetLastWin32Error();
                hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
                if (hookHandle == IntPtr.Zero)
                {
                    Log.Information($"安装鼠标钩子失败: {Marshal.GetLastWin32Error()}（首次尝试错误码 {firstError}）");
                }
            }

            isHooked = hookHandle != IntPtr.Zero;
            return isHooked;
        }

        private void RemoveHook()
        {
            try
            {
                if (hookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookHandle);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"移除鼠标钩子失败: {ex.Message}");
            }
            finally
            {
                hookHandle = IntPtr.Zero;
                hookProc = null;
                isHooked = false;
                lastDownTime = DateTime.MinValue;
                suppressUntil = DateTime.MinValue;
            }
        }

        #region 看门狗

        private void StartWatchdog()
        {
            if (watchdogTimer != null)
            {
                return;
            }

            watchdogTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(WATCHDOG_INTERVAL_MS)
            };
            watchdogTimer.Tick += (s, e) => ReconcileDesktopIconsState();
            watchdogTimer.Start();
        }

        private void StopWatchdog()
        {
            if (watchdogTimer == null)
            {
                return;
            }

            watchdogTimer.Stop();
            watchdogTimer = null;
        }

        /// <summary>
        /// 对齐隐藏状态。
        /// 资源管理器重启后桌面图标视图会被重建，原来的隐藏效果随之消失，
        /// 这里定时检查并把状态重新应用上去；同时也负责修复「程序崩溃后残留隐藏」的情况。
        /// </summary>
        private void ReconcileDesktopIconsState()
        {
            try
            {
                IntPtr desktopView = ResolveDesktopView();
                if (desktopView == IntPtr.Zero)
                {
                    return;
                }

                bool visible = IsWindowVisible(desktopView);

                if (DesktopIconsHidden && visible)
                {
                    // 状态是「隐藏」但视图可见（资源管理器重启过）→ 重新隐藏
                    ApplyDesktopIconsVisibility(desktopView, false);
                }
                else if (!DesktopIconsHidden && !visible && wasHiddenByUs)
                {
                    // 状态是「显示」但视图不可见（上次异常退出残留）→ 恢复显示
                    ApplyDesktopIconsVisibility(desktopView, true);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"同步桌面图标显示状态失败: {ex.Message}");
            }
        }

        /// <summary>是否曾经由本程序隐藏过桌面图标（用于区分「用户自己在资源管理器里隐藏」的情况）</summary>
        private bool wasHiddenByUs;

        #endregion

        #region 隐藏 / 还原桌面图标

        /// <summary>
        /// 切换桌面图标的显示状态
        /// </summary>
        /// <returns>true 表示当前处于「已隐藏」状态</returns>
        public bool ToggleDesktopIcons()
        {
            try
            {
                IntPtr desktopView = ResolveDesktopView();
                if (desktopView == IntPtr.Zero)
                {
                    Log.Information("未找到桌面图标视图，无法切换");
                    return DesktopIconsHidden;
                }

                bool visible = IsWindowVisible(desktopView);

                // 窗口的可见状态是异步反映的（由资源管理器线程处理），
                // 因此以应用自身记录的状态为准，避免快速连点时判断错位
                bool currentlyHidden = DesktopIconsHidden || !visible;
                ApplyDesktopIconsVisibility(desktopView, currentlyHidden);

                return DesktopIconsHidden;
            }
            catch (Exception ex)
            {
                Log.Information($"切换桌面图标显示状态失败: {ex.Message}");
                return DesktopIconsHidden;
            }
        }

        /// <summary>
        /// 恢复显示桌面图标（无论之前是否由本程序隐藏，都确保图标可见）
        /// </summary>
        public void RestoreDesktopIcons()
        {
            try
            {
                IntPtr desktopView = ResolveDesktopView();
                if (desktopView == IntPtr.Zero)
                {
                    return;
                }

                if (IsWindowVisible(desktopView))
                {
                    DesktopIconsHidden = false;
                    wasHiddenByUs = false;
                    GeneralSettingsService.Instance.SetDesktopIconsHidden(false);
                    return;
                }

                ApplyDesktopIconsVisibility(desktopView, true);
            }
            catch (Exception ex)
            {
                Log.Information($"恢复桌面图标失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用桌面图标的显示状态
        /// </summary>
        /// <param name="desktopView">桌面图标视图句柄</param>
        /// <param name="show">true 显示，false 隐藏</param>
        private void ApplyDesktopIconsVisibility(IntPtr desktopView, bool show)
        {
            ShowWindow(desktopView, show ? SW_SHOW : SW_HIDE);

            DesktopIconsHidden = !show;
            wasHiddenByUs = !show;

            try
            {
                GeneralSettingsService.Instance.SetDesktopIconsHidden(DesktopIconsHidden);
            }
            catch (Exception ex)
            {
                Log.Information($"保存桌面图标显示状态失败: {ex.Message}");
            }

            Log.Information(show ? "已恢复桌面图标" : "已隐藏桌面图标");
        }

        /// <summary>
        /// 查找桌面图标视图（SysListView32 / FolderView）
        /// </summary>
        private static IntPtr ResolveDesktopView()
        {
            IntPtr desktopView = DesktopUtil.FindDesktopWindows();
            if (desktopView == IntPtr.Zero)
            {
                desktopView = DesktopUtil.GetShellViewWindow();
            }

            return desktopView;
        }

        #endregion

        /// <summary>
        /// 低级鼠标钩子回调：自行识别双击，不依赖资源管理器额外发送消息
        /// </summary>
        private IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var point = new Point(data.pt.X, data.pt.Y);
                    DateTime now = DateTime.Now;

                    uint doubleClickTime = GetDoubleClickTime();
                    int doubleClickSizeX = GetSystemMetrics(SM_CXDOUBLECLK);
                    int doubleClickSizeY = GetSystemMetrics(SM_CYDOUBLECLK);

                    // 刚处理过一次双击：忽略这段时间内的后续按下，避免三连击被算成多次双击
                    if (now < suppressUntil)
                    {
                        lastDownTime = DateTime.MinValue;
                        lastDownPoint = point;
                        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
                    }

                    bool isDoubleClick =
                        (now - lastDownTime).TotalMilliseconds <= doubleClickTime &&
                        Math.Abs(point.X - lastDownPoint.X) <= doubleClickSizeX &&
                        Math.Abs(point.Y - lastDownPoint.Y) <= doubleClickSizeY;

                    lastDownTime = now;
                    lastDownPoint = point;

                    if (isDoubleClick)
                    {
                        suppressUntil = now.AddMilliseconds(doubleClickTime);
                        TryHandleDesktopDoubleClick(point, now);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"处理鼠标钩子消息失败: {ex.Message}");
            }

            // 必须继续传递给下一个钩子，否则系统级鼠标行为会异常
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// 处理桌面双击：切换桌面图标的隐藏 / 显示
        /// </summary>
        private void TryHandleDesktopDoubleClick(Point screenPoint, DateTime now)
        {
            try
            {
                if (!GeneralSettingsService.Instance.GetEnableDesktopDoubleClickToggle())
                {
                    return;
                }

                // 防误触：两次切换之间必须间隔足够长，避免连点造成来回闪烁
                if ((now - lastToggleTime).TotalMilliseconds < TOGGLE_COOLDOWN_MS)
                {
                    return;
                }

                IntPtr desktopView = ResolveDesktopView();
                if (desktopView == IntPtr.Zero)
                {
                    return;
                }

                // 双击位置必须落在桌面图标视图范围内，否则不予处理
                if (!IsPointInWindow(desktopView, screenPoint))
                {
                    return;
                }

                // 必须确实点在桌面上（任务栏、其它程序的窗口都不算），
                // 否则双击任务栏之类的操作会误触发图标隐藏
                if (!IsPointOnDesktop(desktopView, screenPoint))
                {
                    return;
                }

                // 双击落在图标上时不处理，保留「双击打开文件」的默认行为
                if (IsPointOnDesktopIcon(desktopView, screenPoint))
                {
                    return;
                }

                lastToggleTime = now;

                bool hidden = ToggleDesktopIcons();

                string message = hidden
                    ? "已隐藏桌面图标，再次双击桌面即可恢复"
                    : "已恢复桌面图标";

                TrayIconService.Instance.ShowBalloonTip("桌面图标", message, BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Information($"处理桌面双击失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断屏幕坐标是否位于窗口范围内
        /// </summary>
        private static bool IsPointInWindow(IntPtr hwnd, Point screenPoint)
        {
            try
            {
                if (GetWindowRect(hwnd, out RECT rect))
                {
                    return screenPoint.X >= rect.Left && screenPoint.X < rect.Right &&
                           screenPoint.Y >= rect.Top && screenPoint.Y < rect.Bottom;
                }
            }
            catch (Exception ex)
            {
                Log.Information($"桌面窗口范围判断失败: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 判断点击位置是否确实落在桌面（而不是任务栏或其它程序的窗口）
        /// </summary>
        private static bool IsPointOnDesktop(IntPtr desktopView, Point screenPoint)
        {
            try
            {
                var nativePoint = new Win32.POINT { X = (int)screenPoint.X, Y = (int)screenPoint.Y };
                IntPtr windowAtPoint = Win32.WindowFromPoint(nativePoint);

                // 桌面上没有任何窗口覆盖该位置时，WindowFromPoint 会返回 NULL，
                // 这正是「点在裸露的桌面上」的特征
                if (windowAtPoint == IntPtr.Zero)
                {
                    return true;
                }

                IntPtr rootAtPoint = Win32.GetAncestor(windowAtPoint, Win32.GA_ROOT);
                IntPtr rootOfDesktop = Win32.GetAncestor(desktopView, Win32.GA_ROOT);

                return rootAtPoint != IntPtr.Zero && rootAtPoint == rootOfDesktop;
            }
            catch (Exception ex)
            {
                Log.Information($"桌面命中判断失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断双击位置是否命中了桌面图标
        /// </summary>
        private static bool IsPointOnDesktopIcon(IntPtr hwnd, Point screenPoint)
        {
            try
            {
                var clientPoint = new POINT { X = (int)screenPoint.X, Y = (int)screenPoint.Y };
                if (!ScreenToClient(hwnd, ref clientPoint))
                {
                    return false;
                }

                IntPtr hit = SendMessage(hwnd, LVM_HITTEST, IntPtr.Zero, (IntPtr)((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF)));

                // LVM_HITTEST 命中图标时返回图标序号，未命中返回 -1
                return (int)hit >= 0;
            }
            catch (Exception ex)
            {
                Log.Information($"桌面图标命中测试失败: {ex.Message}");
                return false;
            }
        }
    }
}
