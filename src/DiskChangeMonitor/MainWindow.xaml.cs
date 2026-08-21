using System.Windows;
using System.Windows.Threading;
using DiskChangeMonitor.Storage;
using DiskChangeMonitor.ViewModels;

namespace DiskChangeMonitor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var store = new SqliteSnapshotStore();
            var viewModel = new MainViewModel(store, new WpfFileDialogService());
            DataContext = viewModel;

            Loaded += async (_, _) =>
            {
                try
                {
                    await store.InitializeAsync();
                    await viewModel.InitializeAsync();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(this, "初始化失败: " + ex.Message, "磁盘变化监控", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }
    }
}
