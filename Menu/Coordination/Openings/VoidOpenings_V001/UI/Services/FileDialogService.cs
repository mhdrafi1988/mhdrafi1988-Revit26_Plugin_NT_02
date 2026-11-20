using Microsoft.Win32;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.Services
{
    /// <summary>Asks the user for file names, so the view models do not open dialogs themselves.</summary>
    public sealed class FileDialogService
    {
        /// <summary>Asks where to save; null when the user cancels.</summary>
        public string AskSave(string title, string filter, string defaultName)
        {
            var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>Asks which file to open; null when the user cancels.</summary>
        public string AskOpen(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
