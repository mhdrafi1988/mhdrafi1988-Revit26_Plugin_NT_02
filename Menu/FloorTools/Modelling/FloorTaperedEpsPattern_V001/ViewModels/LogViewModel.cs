using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>Log panel (collapsed by default) with Copy All / Copy Selected.</summary>
    public sealed partial class LogViewModel : ObservableObject
    {
        public ObservableCollection<LogEntryViewModel> Entries { get; } = new ObservableCollection<LogEntryViewModel>();

        [ObservableProperty]
        private bool _isExpanded;

        public void Info(string message) => Entries.Add(new LogEntryViewModel("INFO", message));

        public void Warn(string message) => Entries.Add(new LogEntryViewModel("WARN", message));

        public void Error(string message)
        {
            Entries.Add(new LogEntryViewModel("ERROR", message));
            IsExpanded = true;
        }

        [RelayCommand]
        private void CopyAll() => SetClipboard(Entries.Select(e => e.ToString()));

        /// <summary>CommandParameter = the ListBox's SelectedItems.</summary>
        [RelayCommand]
        private void CopySelected(IList? selected)
        {
            if (selected == null || selected.Count == 0)
            {
                return;
            }

            SetClipboard(Entries.Where(selected.Contains).Select(e => e.ToString()));
        }

        private static void SetClipboard(System.Collections.Generic.IEnumerable<string> lines)
        {
            string text = string.Join(System.Environment.NewLine, lines);
            if (text.Length > 0)
            {
                Clipboard.SetText(text);
            }
        }
    }
}
