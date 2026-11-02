using System;
using System.Windows;
using Layouter.ViewModels;

namespace Layouter.Views
{
    /// <summary>
    /// 备份与还原窗口
    /// </summary>
    public partial class BackupManagerWindow : Window
    {
        private readonly BackupManagerViewModel vm;

        public BackupManagerWindow()
        {
            InitializeComponent();

            vm = new BackupManagerViewModel();
            DataContext = vm;
        }
    }
}
