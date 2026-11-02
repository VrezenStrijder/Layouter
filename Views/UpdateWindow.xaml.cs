using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Layouter.ViewModels;

namespace Layouter.Views
{
    /// <summary>
    /// UpdateWindow.xaml 的交互逻辑
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private UpdateWindowViewModel vm;

        public UpdateWindow()
        {
            InitializeComponent();

            CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            string updateUrl = Env.UpdateUrl;
            string releasePageUrl = Env.ReleasePageUrl;
            string releaseApiUrl = Env.ReleaseApiUrl;
            vm = new UpdateWindowViewModel(CurrentVersion, updateUrl, releasePageUrl, releaseApiUrl);

            DataContext = vm;
        }

        public string CurrentVersion { get; set; }

        private async void CheckNewVersion_Click(object sender, RoutedEventArgs e)
        {
            
            await vm.CheckForUpdatesAsync();
        }

        private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
        {
            vm.OpenReleasePage();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            // 获取点击事件的来源按钮
            Button downloadButton = sender as Button;
            if (downloadButton == null) return;

            // 获取按钮所在的Grid容器
            Grid parentGrid = downloadButton.Parent as Grid;
            if (parentGrid == null) return;

            // 查找RadioButton组
            var radioButtonA = FindVisualChild<RadioButton>(parentGrid, "rbVersionA");
            var radioButtonB = FindVisualChild<RadioButton>(parentGrid, "rbVersionB");

            // 根据选择的RadioButton决定使用哪个下载链接
            bool useFirstLink = radioButtonA != null && radioButtonA.IsChecked == true;
            
            // 调用ViewModel的下载方法，传入选择的链接类型
            vm.DownloadAndInstall(useFirstLink);
        }

        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T tChild && tChild.Name == name)
                    return tChild;

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

    }
}
