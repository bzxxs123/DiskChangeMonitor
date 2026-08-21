namespace DiskChangeMonitor.ViewModels
{
    public interface IFileDialogService
    {
        string? PickCsvFile();

        string? PickSaveFile(string defaultFileName);
    }
}
