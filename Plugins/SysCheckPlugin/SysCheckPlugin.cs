using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.Devices;
using PluginEntry;

namespace Layouter.Plugins.SysCheckPlugin
{
    public class SysCheckPlugin : IPlugin
    {
        public Dictionary<string, Func<PluginParameter[], object>> FunctionDict { get; set; } = new Dictionary<string, Func<PluginParameter[], object>>();

        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;
        private PerformanceCounter availableMemoryCounter;
        private PerformanceCounter swapCounter;

        private DispatcherTimer timer;
        private Grid mainGrid;
        private TextBlock cpuUsageText;
        private TextBlock ramUsageText;
        private TextBlock swapUsageText;
        private ProgressBar cpuProgressBar;
        private ProgressBar ramProgressBar;
        private ProgressBar swapProgressBar;
        private bool isRegister = false;

        public string Name => "系统资源监控";


        public void Register()
        {
            if (isRegister)
            {
                return;
            }

            FunctionDict.Add("CPU使用率", GetCPUUsage);
            FunctionDict.Add("内存使用率", GetRamUsage);
            FunctionDict.Add("缓存使用率", GetSwapUsage);

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                availableMemoryCounter = new PerformanceCounter("Memory", "Available Bytes");

                swapCounter = new PerformanceCounter("Paging File", "% Usage", "_Total");
                isRegister = true;
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"初始化性能计数器失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public object Run(string functionKey, params PluginParameter[] parameters)
        {
            if (FunctionDict.ContainsKey(functionKey))
            {
                try
                {
                    return FunctionDict[functionKey].Invoke(parameters);
                }
                catch (Exception ex)
                {
                    AppMessageBox.Show($"执行功能出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }
            return null;
        }

        private object GetCPUUsage(PluginParameter[] parameters)
        {
            return $"{Math.Round(cpuCounter.NextValue(), 2)}";
        }

        private object GetRamUsage(PluginParameter[] parameters)
        {
            ComputerInfo computerInfo = new ComputerInfo();
            ulong totalPhysicalMemory = computerInfo.TotalPhysicalMemory;
            float availableMemory = availableMemoryCounter.NextValue();
            float memoryUsagePercent = ((totalPhysicalMemory - availableMemory) / (float)totalPhysicalMemory) * 100;

            return $"{Math.Round(ramCounter.NextValue(), 2)}";
        }

        private object GetSwapUsage(PluginParameter[] parameters)
        {
            return $"{Math.Round(swapCounter.NextValue(), 2)}";
        }

        public void Unregister()
        {
            //StopMonitoring();

            FunctionDict.Clear();
            cpuCounter?.Dispose();
            ramCounter?.Dispose();
            swapCounter?.Dispose();
            isRegister = false;
        }

        public Dictionary<string, List<PluginParameter>> GetParameterDescriptions()
        {
            return null;
        }

        //public object Run(string functionKey, params PluginParameter[] args)
        //{
        //    if (functionKey == "显示系统资源")
        //    {
        //        ShowSystemResources();
        //        return true;
        //    }
        //    return null;
        //}

        //private object ShowSystemResources(params PluginParameter[] parameters)
        //{
        //    mainGrid = new Grid
        //    {
        //        Margin = new Thickness(15),
        //        HorizontalAlignment = HorizontalAlignment.Stretch
        //    };

        //    // 设置行定义
        //    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        //    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        //    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        //    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        //    // 添加标题
        //    var titleBlock = new TextBlock
        //    {
        //        Text = "系统资源监控",
        //        FontSize = 18,
        //        FontWeight = FontWeights.Bold,
        //        Margin = new Thickness(0, 0, 0, 15),
        //        HorizontalAlignment = HorizontalAlignment.Center
        //    };
        //    Grid.SetRow(titleBlock, 0);
        //    mainGrid.Children.Add(titleBlock);

        //    // CPU 使用率
        //    var cpuPanel = CreateResourcePanel("CPU 使用率", out cpuUsageText, out cpuProgressBar);
        //    Grid.SetRow(cpuPanel, 1);
        //    mainGrid.Children.Add(cpuPanel);

        //    // 内存使用率
        //    var ramPanel = CreateResourcePanel("内存使用率", out ramUsageText, out ramProgressBar);
        //    Grid.SetRow(ramPanel, 2);
        //    mainGrid.Children.Add(ramPanel);

        //    // 交换空间使用率
        //    var swapPanel = CreateResourcePanel("交换空间使用率", out swapUsageText, out swapProgressBar);
        //    Grid.SetRow(swapPanel, 3);
        //    mainGrid.Children.Add(swapPanel);

        //    // 启动定时器更新数据
        //    StartMonitoring();

        //    return null;
        //}

        //private Grid CreateResourcePanel(string title, out TextBlock usageText, out ProgressBar progressBar)
        //{
        //    var panel = new Grid
        //    {
        //        Margin = new Thickness(0, 5, 0, 10)
        //    };

        //    panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        //    panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        //    // 标题和使用率文本
        //    var headerPanel = new Grid();
        //    headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        //    headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        //    var titleBlock = new TextBlock
        //    {
        //        Text = title,
        //        FontWeight = FontWeights.Bold,
        //        VerticalAlignment = VerticalAlignment.Center
        //    };
        //    Grid.SetColumn(titleBlock, 0);
        //    headerPanel.Children.Add(titleBlock);

        //    usageText = new TextBlock
        //    {
        //        Text = "0%",
        //        HorizontalAlignment = HorizontalAlignment.Right,
        //        VerticalAlignment = VerticalAlignment.Center,
        //        Margin = new Thickness(10, 0, 0, 0)
        //    };
        //    Grid.SetColumn(usageText, 1);
        //    headerPanel.Children.Add(usageText);

        //    Grid.SetRow(headerPanel, 0);
        //    panel.Children.Add(headerPanel);

        //    // 进度条
        //    progressBar = new ProgressBar
        //    {
        //        Height = 20,
        //        Minimum = 0,
        //        Maximum = 100,
        //        Value = 0,
        //        Margin = new Thickness(0, 5, 0, 0)
        //    };
        //    Grid.SetRow(progressBar, 1);
        //    panel.Children.Add(progressBar);

        //    return panel;
        //}

        //private void StartMonitoring()
        //{
        //    if (timer == null)
        //    {
        //        timer = new DispatcherTimer
        //        {
        //            Interval = TimeSpan.FromSeconds(1)
        //        };
        //        timer.Tick += Timer_Tick;
        //        timer.Start();

        //        // 立即更新一次数据
        //        UpdateResourceUsage();
        //    }
        //}

        //private void StopMonitoring()
        //{
        //    if (timer != null)
        //    {
        //        timer.Stop();
        //        timer.Tick -= Timer_Tick;
        //        timer = null;
        //    }
        //}

        //private void Timer_Tick(object sender, EventArgs e)
        //{
        //    UpdateResourceUsage();
        //}

        //private void UpdateResourceUsage()
        //{
        //    try
        //    {
        //        // 获取CPU使用率
        //        float cpuUsage = cpuCounter.NextValue();
        //        // 第一次调用NextValue()通常返回0，需要等待一段时间再次调用
        //        if (cpuUsage < 0.1f)
        //        {
        //            System.Threading.Thread.Sleep(500);
        //            cpuUsage = cpuCounter.NextValue();
        //        }

        //        // 获取内存使用率
        //        float ramUsage = ramCounter.NextValue();

        //        // 获取交换空间使用率
        //        float swapUsage = swapCounter.NextValue();

        //        // 更新UI
        //        Application.Current.Dispatcher.Invoke(() =>
        //        {
        //            UpdateResourceUI(cpuUsageText, cpuProgressBar, cpuUsage);
        //            UpdateResourceUI(ramUsageText, ramProgressBar, ramUsage);
        //            UpdateResourceUI(swapUsageText, swapProgressBar, swapUsage);
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        AppMessageBox.Show($"更新资源使用率失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //        StopMonitoring();
        //    }
        //}

        //private void UpdateResourceUI(TextBlock textBlock, ProgressBar progressBar, float value)
        //{
        //    int roundedValue = (int)Math.Round(value);
        //    textBlock.Text = $"{roundedValue}%";
        //    progressBar.Value = roundedValue;

        //    // 根据使用率设置颜色
        //    if (roundedValue < 60)
        //    {
        //        progressBar.Foreground = new SolidColorBrush(Colors.Green);
        //    }
        //    else if (roundedValue < 80)
        //    {
        //        progressBar.Foreground = new SolidColorBrush(Colors.Orange);
        //    }
        //    else
        //    {
        //        progressBar.Foreground = new SolidColorBrush(Colors.Red);
        //    }
        //}

    }
}