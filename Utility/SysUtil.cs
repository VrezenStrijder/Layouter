using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Layouter.Services;
using Microsoft.VisualBasic;

namespace Layouter.Utility
{
    public class SysUtil
    {
        public static IntPtr GetWindowHandle(Window win)
        {
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            return hwnd;
        }

        public static void SetDesktopLevelWindow(Window win)
        {
            IntPtr hwnd = GetWindowHandle(win);

            // 以桌面视图为 owner：Win+D / 显示桌面时不会随普通窗口一起最小化
            DesktopUtil.SetShellViewAsOwnerWindow(hwnd);
            DesktopUtil.SetAsToolWindow(hwnd);

            // 共享 owner 会让 Windows 在激活任一窗口时整组上浮，由该服务负责压回桌面层级
            DesktopLayerService.Track(win);
        }

        public static bool IsWindows11()
        {
            bool? flag = null;

            if (flag == null)
            {
                try
                {
                    var osVersion = Environment.OSVersion.Version;
                    if (osVersion.Major >= 10 && osVersion.Build >= 22000)
                    {
                        flag = true;
                    }
                    else
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                        {
                            foreach (var os in searcher.Get())
                            {
                                string caption = os["Caption"].ToString();
                                flag = caption.Contains("Windows 11");
                                break;
                            }
                        }

                        if (flag == null)
                        {
                            flag = (osVersion.Major == 10 && osVersion.Build >= 22000);
                        }
                    }
                }
                catch
                {
                    flag = false;
                }
            }

            return flag.Value;
        }

        public static Size MeasureText(string text, FontFamily fontFamily, double fontSize, FontStyle fontStyle, FontWeight fontWeight)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontStyle = fontStyle,
                FontWeight = fontWeight
            };

            // 先设置约束
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize;
        }
    }
}
