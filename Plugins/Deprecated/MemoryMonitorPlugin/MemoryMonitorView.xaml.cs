using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Layouter.Plugins.MemoryMonitorPlugin
{
    /// <summary>
    /// MemoryMonitorView.xaml 的交互逻辑
    /// </summary>
    public partial class MemoryMonitorView : UserControl
    {
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private class MemoryInfo
        {
            public long TotalPhysical { get; set; }
            public long AvailablePhysical { get; set; }
        }

        private Timer timer;
        private bool isMonitoring;

        public MemoryMonitorView()
        {
            InitializeComponent();
            isMonitoring = false;
        }

        public void StartMonitoring()
        {
            if (!isMonitoring)
            {
                isMonitoring = true;
                timer = new Timer(UpdateMemoryInfo, null, 0, 1000);
            }
        }

        public void StopMonitoring()
        {
            if (isMonitoring)
            {
                isMonitoring = false;
                timer?.Dispose();
                timer = null;
            }
        }

        private void UpdateMemoryInfo(object state)
        {
            try
            {
                var memoryInfo = GetMemoryInfo();

                Dispatcher.Invoke(() =>
                {
                    TotalMemoryTextBlock.Text = $"{memoryInfo.TotalPhysical / (1024 * 1024)} MB";
                    AvailableMemoryTextBlock.Text = $"{memoryInfo.AvailablePhysical / (1024 * 1024)} MB";

                    double usagePercentage = 100 - ((double)memoryInfo.AvailablePhysical / memoryInfo.TotalPhysical * 100);
                    MemoryUsageTextBlock.Text = $"{usagePercentage:F1}%";
                    MemoryUsageProgressBar.Value = usagePercentage;

                    // 根据内存使用率设置进度条颜色
                    if (usagePercentage < 60)
                    {
                        MemoryUsageProgressBar.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    else if (usagePercentage < 80)
                    {
                        MemoryUsageProgressBar.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                    else
                    {
                        MemoryUsageProgressBar.Foreground = new SolidColorBrush(Colors.Red);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Information($"更新内存信息时出错: {ex.Message}");
            }
        }

        private MemoryInfo GetMemoryInfo()
        {
            var memoryInfo = new MemoryInfo();

            var memoryStatus = new MEMORYSTATUSEX();
            memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

            if (GlobalMemoryStatusEx(ref memoryStatus))
            {
                memoryInfo.TotalPhysical = (long)memoryStatus.ullTotalPhys;
                memoryInfo.AvailablePhysical = (long)memoryStatus.ullAvailPhys;
            }

            return memoryInfo;
        }

        private void ReleaseMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 调用GC回收内存
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 调用系统API释放内存
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);

                // 更新内存信息
                UpdateMemoryInfo(null);

                AppMessageBox.Show("内存释放完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Information($"释放内存时出错: {ex.Message}");
                AppMessageBox.Show($"释放内存时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }
}
