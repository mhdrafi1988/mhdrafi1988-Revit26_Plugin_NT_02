using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.Infrastructure
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _run;
        private readonly Func<bool> _can;

        public RelayCommand(Action run, Func<bool> can = null) { _run = run; _can = can; }

        public bool CanExecute(object parameter) => _can == null || _can();
        public void Execute(object parameter) => _run();

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Runs an async method on the UI thread. Continuations stay on Revit's main thread
    /// (inside the modal dialog), so Revit API calls after an await are still valid.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _run;
        private readonly Func<bool> _can;
        private bool _running;

        public AsyncRelayCommand(Func<Task> run, Func<bool> can = null) { _run = run; _can = can; }

        public bool CanExecute(object parameter) => !_running && (_can == null || _can());

        public async void Execute(object parameter)
        {
            if (_running) return;
            _running = true;
            CommandManager.InvalidateRequerySuggested();
            try { await _run(); }
            finally
            {
                _running = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
