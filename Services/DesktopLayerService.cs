using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static System.Windows.Win32;

namespace Layouter.Services
{
    /// <summary>
    /// 维持分区窗口的层级：分区以 SHELLDLL_DefView 为 owner（这样 Win+D / 显示桌面时
    /// 不会随普通窗口一起被最小化），但共享 owner 会让 Windows 在激活任意一个分区时
    /// 把整组分区一起提到普通窗口之上。这里在任何前台切换/窗口还原之后，把所有分区
    /// 重新压回桌面层级，从而保持"分区始终位于常规活动窗口之下"。
    /// </summary>
    internal static class DesktopLayerService
    {
        private static readonly List<Window> windows = new List<Window>();
        private static readonly HashSet<IntPtr> handles = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> guardedHandles = new HashSet<IntPtr>();

        private const int WM_WINDOWPOSCHANGING = 0x0046;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private static IntPtr hookHandle = IntPtr.Zero;
        private static WinEventProc hookProc;
        private static DispatcherTimer settleTimer;
        private static int settleTicks;
        private static bool isPushing;

        /// <summary>
        /// 登记一个分区窗口，之后所有层级纠正都会覆盖它。
        /// </summary>
        public static void Track(Window window)
        {
            if (window == null || windows.Contains(window))
            {
                return;
            }

            windows.Add(window);
            window.Closed += OnWindowClosed;
            window.IsVisibleChanged += OnWindowVisibleChanged;
            window.SourceInitialized += OnWindowSourceInitialized;

            AttachZOrderGuard(window);

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                handles.Add(handle);
            }

            EnsureHookInstalled();

            // 新建窗口的 Show() 会把它带到最前，稍后再压回去
            Push();
            ScheduleSettle(4);
        }

        private static void OnWindowSourceInitialized(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                AttachZOrderGuard(window);
            }
        }

        /// <summary>
        /// 拦截 z-order 变更：激活分区时 Windows 会把 owner 组一起上浮，
        /// 这里直接拒绝该次置顶，避免"先浮起再压回"造成的闪烁。
        /// 分区自身的层级只由 <see cref="Push"/> 决定。
        /// </summary>
        private static void AttachZOrderGuard(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null && !guardedHandles.Contains(handle))
            {
                guardedHandles.Add(handle);
                source.AddHook(OnWindowMessage);
            }
        }

        private static IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING && !isPushing)
            {
                WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.flags |= SWP_NOZORDER;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }

            return IntPtr.Zero;
        }

        private static void OnWindowClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnWindowClosed;
                window.IsVisibleChanged -= OnWindowVisibleChanged;
                window.SourceInitialized -= OnWindowSourceInitialized;
                windows.Remove(window);

                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    handles.Remove(handle);
                    guardedHandles.Remove(handle);
                }
            }
        }

        private static void OnWindowVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 从托盘/隐藏状态重新显示后需要再次沉降
            if (sender is Window window && window.IsVisible)
            {
                ScheduleSettle(3);
            }
        }

        /// <summary>
        /// 把已登记的分区压到最底层应用窗口之下（但仍在桌面之上），并保持分区之间的相对层次。
        /// </summary>
        private static void Push()
        {
            if (isPushing)
            {
                return;
            }

            RefreshHandles();
            if (handles.Count == 0)
            {
                return;
            }

            // 以"桌面之上、最靠下的应用窗口"为插入点：分区因此永远位于应用窗口之下、桌面之上。
            // 找不到这样的窗口时（例如只开着一个看不见的分层窗口），直接放到普通窗口最上层，
            // 否则分区会被那个看不见的窗口挡住，鼠标点击全部落在它身上。
            IntPtr anchor = FindInsertAnchor();
            bool placeOnTop = anchor == IntPtr.Zero;

            List<IntPtr> ordered = GetBottomToTopOrder();
            if (ordered.Count == 0)
            {
                return;
            }

            isPushing = true;
            try
            {
                // 每次插入都会落在 anchor 正下方，因此自下而上插入可保持原有相对层次
                foreach (IntPtr handle in ordered)
                {
                    SetWindowPos(handle, placeOnTop ? HWND_TOP : anchor, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
            }
            finally
            {
                isPushing = false;
            }
        }

        /// <summary>
        /// 同步一次句柄集合：窗口句柄可能在登记之后才创建。
        /// </summary>
        private static void RefreshHandles()
        {
            foreach (Window window in windows)
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    handles.Add(handle);
                }
            }
        }

        /// <summary>
        /// 找插入点：位于桌面窗口之上、z-order 最靠下的常规窗口。
        /// 桌面窗口（Progman/WorkerW）以下的部分不再作为候选，否则分区会被压到桌面图标下面。
        /// </summary>
        private static IntPtr FindInsertAnchor()
        {
            IntPtr anchor = IntPtr.Zero;
            bool desktopSeen = false;

            EnumWindows((handle, lParam) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                if (IsDesktopWindow(handle))
                {
                    desktopSeen = true;
                    return true;
                }

                if (desktopSeen || IsIconic(handle) || handles.Contains(handle) || IsShellWindow(handle))
                {
                    return true;
                }

                // 看不见、也点不到的窗口不参与让位：
                // 分区必须排在它们之上，否则会被它们静默吞掉鼠标点击。
                if (!IsRealWindow(handle))
                {
                    return true;
                }

                // EnumWindows 自上而下，这里留下的是桌面之上最靠下的那个
                anchor = handle;
                return true;
            }, IntPtr.Zero);

            return anchor;
        }

        /// <summary>
        /// 判断窗口是否真的会显示并接收点击
        /// </summary>
        /// <remarks>
        /// 浏览器、Electron 之类的程序常留下 alpha=0 的分层窗口，它们完全看不见，
        /// 却依然参与命中测试，会把下层的分区整块挡住。这类窗口不能当作"上方窗口"。
        /// </remarks>
        private static bool IsRealWindow(IntPtr handle)
        {
            long exStyle = GetWindowLong(handle, GWL_EXSTYLE);

            // 鼠标穿透的窗口本来就不接收点击
            if ((exStyle & WS_EX_TRANSPARENT) != 0)
            {
                return false;
            }

            // 整体透明的分层窗口：看不见，但会拦截点击
            if ((exStyle & WS_EX_LAYERED) != 0
                && GetLayeredWindowAttributes(handle, out _, out byte alpha, out uint flags)
                && (flags & LWA_ALPHA) != 0
                && alpha == 0)
            {
                return false;
            }

            if (!IsWindowEnabled(handle))
            {
                return false;
            }

            // 被 DWM 挂起的窗口（UWP 后台页、其他虚拟桌面上的窗口）不会显示
            if (DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 取已登记窗口在 z-order 中的自下而上顺序。
        /// </summary>
        private static List<IntPtr> GetBottomToTopOrder()
        {
            List<IntPtr> ordered = new List<IntPtr>();
            EnumWindows((handle, lParam) =>
            {
                if (handles.Contains(handle) && IsWindowVisible(handle))
                {
                    ordered.Add(handle);
                }
                return true;
            }, IntPtr.Zero);

            // EnumWindows 自上而下，反转即自下而上
            ordered.Reverse();

            // 补齐尚未进入 z-order 的句柄（例如刚创建、还没显示）
            foreach (IntPtr handle in handles)
            {
                if (!ordered.Contains(handle) && IsWindowVisible(handle))
                {
                    ordered.Add(handle);
                }
            }

            return ordered;
        }

        private static bool IsDesktopWindow(IntPtr handle)
        {
            string className = GetClassNameOf(handle);
            return className == "Progman" || className == "WorkerW";
        }

        private static bool IsShellWindow(IntPtr handle)
        {
            switch (GetClassNameOf(handle))
            {
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "ThumbnailDeviceHelperWnd":
                case "ForegroundStaging":
                    return true;
                default:
                    return false;
            }
        }

        private static string GetClassNameOf(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(128);
            GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        /// <summary>
        /// 在一小段时间内重复纠正层级，覆盖窗口还原动画等延迟变化。
        /// </summary>
        private static void ScheduleSettle(int ticks)
        {
            settleTicks = Math.Max(settleTicks, ticks);

            if (settleTimer == null)
            {
                settleTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(60)
                };
                settleTimer.Tick += OnSettleTick;
            }

            if (!settleTimer.IsEnabled)
            {
                settleTimer.Start();
            }
        }

        private static void OnSettleTick(object sender, EventArgs e)
        {
            Push();

            settleTicks--;
            if (settleTicks <= 0)
            {
                settleTimer.Stop();
            }
        }

        private static void EnsureHookInstalled()
        {
            if (hookHandle != IntPtr.Zero)
            {
                return;
            }

            hookProc = new WinEventProc(OnWinEvent);
            hookHandle = SetWinEventHook(
                WinEvent.EVENT_SYSTEM_FOREGROUND,
                WinEvent.EVENT_SYSTEM_DESKTOPSWITCH,
                IntPtr.Zero,
                hookProc,
                0,
                0,
                (uint)WinEvent.WINEVENT_OUTOFCONTEXT);

            GC.KeepAlive(hookProc);
        }

        private static void OnWinEvent(IntPtr hWinEventHook, WinEvent eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            switch (eventType)
            {
                case WinEvent.EVENT_SYSTEM_FOREGROUND:
                    // 点击分区会让整组 owner 一起浮到普通窗口之上，这里立刻压回去
                    Push();
                    ScheduleSettle(2);
                    break;

                case WinEvent.EVENT_SYSTEM_MINIMIZEEND:
                case WinEvent.EVENT_SYSTEM_DESKTOPSWITCH:
                    // 显示桌面还原 / 切换虚拟桌面后，层级会被重排
                    ScheduleSettle(12);
                    break;
            }
        }
    }
}
