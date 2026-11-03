using System;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers
{
    public static class DialogService
    {
        public static string SelectFolder(string initialPath = "")
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Folder for AutoSlope Exports",
                    InitialDirectory = initialPath
                };
                return dialog.ShowDialog() == true ? dialog.FolderName : null;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Folder Selection Error", $"Error selecting folder: {ex.Message}\n\nUsing default folder.");
                return initialPath;
            }
        }

        public static string ShowSaveFileDialog(string filter, string initialDirectory, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter,
                InitialDirectory = initialDirectory,
                FileName = defaultFileName
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}