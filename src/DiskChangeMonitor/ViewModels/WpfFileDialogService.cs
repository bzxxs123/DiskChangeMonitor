using Microsoft.Win32;

namespace DiskChangeMonitor.ViewModels
{
    public sealed class WpfFileDialogService : IFileDialogService
    {
        public string? PickCsvFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 WizTree CSV 导出文件",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? PickSaveFile(string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = "导出对比结果",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = defaultFileName,
                AddExtension = true,
                DefaultExt = ".csv"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
