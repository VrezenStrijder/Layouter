using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PluginEntry;
using Layouter.Plugins.SysCheckPlugin;

namespace Layouter.Plugins.SysCheckPlugin
{
    public class SysCheckPlugin : IPlugin
    {
        public Dictionary<string, Func<PluginParameter[], object>> FunctionDict { get; set; } = new Dictionary<string, Func<PluginParameter[], object>>();

        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;
        private PerformanceCounter swapCounter;
        private DispatcherTimer timer;
        private DetailedTemplateWindow detailedView;
        private PluginStyle pluginStyle;

        public string Name => "系统资源监控";


        public void Register()
        {
            FunctionDict.Add("显示系统资源", ShowSystemResources);

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                swapCounter = new PerformanceCounter("Paging File", "% Usage", "Total");
                
                // 加载样式文件
                string pluginPath = Path.GetDirectoryName(GetType().Assembly.Location);
                string stylePath = Path.Combine(pluginPath, "style.json");
                pluginStyle = PluginStyle.LoadFromFile(stylePath);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"初始化性能计数器或加载样式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Unregister()
        {
            StopMonitoring();

            FunctionDict.Clear();
            cpuCounter?.Dispose();
            ramCounter?.Dispose();
            swapCounter?.Dispose();
        }

        public Dictionary<string, List<PluginParameter>> GetParameterDescriptions()
        {
            return new Dictionary<string, List<PluginParameter>>
            {
                { "显示系统资源", new List<PluginParameter>() }
            };
        }

        public object Run(string functionKey, params PluginParameter[] args)
        {
            if (functionKey == "显示系统资源")
            {
                ShowSystemResources();
                return true;
            }
            return null;
        }

        private object ShowSystemResources(params PluginParameter[] parameters)
        {
            // 创建详细视图窗口模板
            detailedView = CreateDetailedViewTemplate();
            
            // 添加资源项目（初始值为0）
            detailedView.AddResourceItem("CPU Usage", 0, Brushes.Green);
            detailedView.AddResourceItem("RAM Usage", 0, Brushes.Green);
            detailedView.AddResourceItem("SWAP Usage", 0, Brushes.Green);
            
            // 应用样式
            if (pluginStyle != null)
            {
                detailedView.ApplyStyle(pluginStyle);
            }
            
            // 显示窗口
            detailedView.Show();
            
            // 启动定时器更新数据
            StartMonitoring();
            
            return null;
        }



        private void StartMonitoring()
        {
            if (timer == null)
            {
                timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += Timer_Tick;
                timer.Start();

                // 立即更新一次数据
                UpdateResourceUsage();
            }
        }

        private void StopMonitoring()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Tick -= Timer_Tick;
                timer = null;
            }

            // 关闭详细视图窗口
            if (detailedView != null)
            {
                detailedView.Close();
                detailedView = null;
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateResourceUsage();
        }

        private DetailedTemplateWindow CreateDetailedViewTemplate()
        {
            // 创建模板窗口
            return new DetailedTemplateWindow("系统资源监控");
        }

        private void UpdateResourceUsage()
        {
            try
            {
                // 获取CPU使用率
                float cpuUsage = cpuCounter.NextValue();
                // 第一次调用NextValue()通常返回0，需要等待一段时间再次调用
                if (cpuUsage < 0.1f)
                {
                    Thread.Sleep(500);
                    cpuUsage = cpuCounter.NextValue();
                }

                // 获取内存使用率
                float ramUsage = ramCounter.NextValue();

                // 获取交换空间使用率
                float swapUsage = swapCounter.NextValue();

                // 更新UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (detailedView != null)
                    {
                        // 根据使用率设置颜色
                        Brush cpuColor = GetColorByUsage(cpuUsage);
                        Brush ramColor = GetColorByUsage(ramUsage);
                        Brush swapColor = GetColorByUsage(swapUsage);
                        
                        // 更新详细视图中的资源项
                        detailedView.UpdateResourceItem("CPU Usage", cpuUsage, cpuColor);
                        detailedView.UpdateResourceItem("RAM Usage", ramUsage, ramColor);
                        detailedView.UpdateResourceItem("SWAP Usage", swapUsage, swapColor);
                    }
                });
            }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"更新资源使用率失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StopMonitoring();
            }
        }

        private Brush GetColorByUsage(float value)
        {
            int roundedValue = (int)Math.Round(value);
            
            // 根据使用率设置颜色
            if (roundedValue < 60)
            {
                return Brushes.Green;
            }
            else if (roundedValue < 80)
            {
                return Brushes.Orange;
            }
            else
            {
                return Brushes.Red;
            }
        }

    }
}