using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Layouter.Services;
using Layouter.Views;

namespace Layouter.Utility
{
    /// <summary>
    /// 单例窗口管理
    /// </summary>
    public class SingletonWindowManager
    {
        private static readonly Lazy<SingletonWindowManager> instance = new Lazy<SingletonWindowManager>(() => new SingletonWindowManager());
        public static SingletonWindowManager Instance => instance.Value;

        // 存储当前打开的窗口引用
        private Dictionary<Type, Window> openWindows = new Dictionary<Type, Window>();

        private SingletonWindowManager() { }


        /// <summary>
        /// 显示单例窗口
        /// </summary>
        /// <typeparam name="T">窗口类型</typeparam>
        /// param name="isModal">是否为模态窗口</param>
        /// <param name="createWindow">创建窗口的委托</param>
        public T ShowWindow<T>(Func<T> createWindow,bool isModal=false) where T : Window
        {
            Type windowType = typeof(T);

            try
            {
                // 检查是否已经有窗口打开  
                if (openWindows.TryGetValue(windowType, out var existingWindow))
                {
                    // 如果窗口已经关闭但引用未清除  
                    if (!IsWindowOpen(existingWindow))
                    {
                        openWindows.Remove(windowType);
                    }
                    else
                    {
                        existingWindow.Activate();

                        // 如果窗口被最小化，则恢复窗口  
                        if (existingWindow.WindowState == WindowState.Minimized)
                        {
                            existingWindow.WindowState = WindowState.Normal;
                        }

                        return (existingWindow as T);
                    }
                }

                var window = createWindow();
                openWindows[windowType] = window;
                window.Closed += (s, e) => openWindows.Remove(windowType);

                if (isModal)
                {
                    bool? result = window.ShowDialog();
                    openWindows.Remove(windowType);
                }
                else
                {
                    window.Show();
                }

                return window;
            }
            catch (Exception ex)
            {
                Log.Information($"打开窗口失败: {ex.Message}");
                AppMessageBox.Show($"打开窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return null;
        }

        /// <summary>
        /// 检查窗口是否打开
        /// </summary>
        private bool IsWindowOpen(Window window)
        {
            try
            {
                return (window != null) && (PresentationSource.FromVisual(window) != null);
            }
            catch
            {
                return false;
            }
        }
    }
}
