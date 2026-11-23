using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Layouter.Utility
{
    /// <summary>
    /// 分区窗口可用的背景材质
    /// </summary>
    public enum PartitionBackdrop
    {
        /// <summary>不使用材质，纯色/半透明背景</summary>
        None = 0,

        /// <summary>亚克力毛玻璃</summary>
        Acrylic = 1,

        /// <summary>云母（仅 Windows 11 有效）</summary>
        Mica = 2
    }

    /// <summary>
    /// 窗口背景材质（亚克力 / 云母）
    /// </summary>
    /// <remarks>
    /// 亚克力用的是比较老的 <c>SetWindowCompositionAttribute</c> 方案：
    /// 它对 WPF 的 <c>AllowsTransparency=True</c> 窗口同样有效，
    /// 而 Win11 官方的 <c>DWMWA_SYSTEMBACKDROP_TYPE</c> 要求窗口不能用分层背景，
    /// 两者取舍后这里选了兼容性更好的那一个。
    /// <para>
    /// 材质只影响窗口「背后」那一层内容的观感：
    /// 分区窗口位于桌面层时，模糊对象是桌面壁纸；
    /// 若分区被其它窗口遮住，则模糊的是那个窗口。
    /// </para>
    /// </remarks>
    public static class WindowBackdrop
    {
        #region Win32

        /// <summary>ACCENT_POLICY</summary>
        private const int WCA_ACCENT_POLICY = 19;

        /// <summary>ACCENT_ENABLE_ACRYLICBLURBEHIND</summary>
        private const int AccentEnableAcrylicBlurBehind = 4;

        /// <summary>ACCENT_ENABLE_HOSTBACKDROP</summary>
        private const int AccentEnableHostBackdrop = 5;

        /// <summary>
        /// 不参与着色的 tint。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 实测结论（Windows 11 分层窗口）：
        /// <c>AccentFlags = 2</c> 时合成器会把 <c>GradientColor</c> 当成实心填充色，
        /// 背景会变成一块不透明纯色，毛玻璃完全消失；
        /// 置 0 后只保留模糊，<c>GradientColor</c> 也不再影响画面。
        /// </para>
        /// <para>
        /// 同时 <c>GradientColor</c> 的 alpha 通道在该状态下并不参与透明度混合，
        /// 因此色调与浓度统一交给窗口内的 WPF 叠加层表现。
        /// </para>
        /// </remarks>
        private const int NoTint = 0;

        /// <summary>DWMWA_SYSTEMBACKDROP_TYPE</summary>
        private const int DwmwaSystemBackdropType = 38;

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        #endregion

        /// <summary>当前系统是否支持亚克力材质</summary>
        public static bool IsAcrylicSupported =>
            Environment.OSVersion.Version.Major > 10 ||
            (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 17134);

        /// <summary>当前系统是否支持云母材质</summary>
        public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

        /// <summary>
        /// 给窗口套上背景材质
        /// </summary>
        /// <remarks>
        /// 合成器只负责毛玻璃模糊，色调由窗口的 WPF 叠加层表现。
        /// 注意 <c>AccentFlags</c> 必须为 0，否则合成器会按 <c>GradientColor</c>
        /// 填充整块窗口，模糊会彻底消失。
        /// </remarks>
        /// <param name="window">目标窗口</param>
        /// <param name="backdrop">材质类型</param>
        /// <returns>是否成功套用</returns>
        public static bool Apply(Window window, PartitionBackdrop backdrop)
        {
            if (window == null)
            {
                return false;
            }

            try
            {
                var helper = new WindowInteropHelper(window);
                helper.EnsureHandle();

                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                if (backdrop == PartitionBackdrop.None)
                {
                    Clear(hwnd);
                    return true;
                }

                if (backdrop == PartitionBackdrop.Mica)
                {
                    // 云母要求窗口不能用分层背景，而分区窗口开启了 AllowsTransparency，
                    // 直接套用会让窗口变成不透明的白块，因此这里统一按亚克力处理。
                    if (!IsAcrylicSupported)
                    {
                        Clear(hwnd);
                        return false;
                    }

                    SetAccent(hwnd, AccentEnableAcrylicBlurBehind, NoTint);
                }
                else
                {
                    if (!IsAcrylicSupported)
                    {
                        Clear(hwnd);
                        return false;
                    }

                    SetAccent(hwnd, AccentEnableAcrylicBlurBehind, NoTint);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Information($"应用窗口背景材质失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销背景材质
        /// </summary>
        public static void Clear(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                SetAccent(hwnd, 0, 0);

                // 云母是另一套开关，需要单独关掉
                int type = 1;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
            }
            catch (Exception ex)
            {
                Log.Information($"撤销窗口背景材质失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 下发合成策略
        /// </summary>
        private static void SetAccent(IntPtr hwnd, int state, int color)
        {
            var accent = new AccentPolicy
            {
                AccentState = state,
                // 必须为 0：非 0 时合成器会把 GradientColor 当作实心填充色
                AccentFlags = 0,
                GradientColor = color,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);
            IntPtr pointer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(accent, pointer, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = pointer,
                    SizeOfData = size
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

    }
}
