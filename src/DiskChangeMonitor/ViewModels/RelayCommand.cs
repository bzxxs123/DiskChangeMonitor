using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DiskChangeMonitor.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public Task ExecuteAsync(object? parameter = null)
        {
            return _execute();
        }

        public async void Execute(object? parameter)
        {
            await ExecuteAsync(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
