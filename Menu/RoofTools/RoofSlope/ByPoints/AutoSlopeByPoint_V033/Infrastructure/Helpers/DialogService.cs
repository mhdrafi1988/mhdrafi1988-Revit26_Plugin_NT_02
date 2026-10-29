// =======================================================
// File: DialogService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
// Folder / save dialogs, the "open it now?" prompt, and a guarded file
// open. V030 opened files in two places and only one was guarded (B9).
// =======================================================

using System;
using System.Diagnostics;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
{
    public static class DialogService
    {
        public static string SelectFolder(string initialPath)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the folder for AutoSlope exports",
                InitialDirectory = initialPath ?? string.Empty
            };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public static string SaveExcel(string initialDirectory, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                InitialDirectory = initialDirectory ?? string.Empty,
                FileName = defaultFileName
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static bool AskToOpen(string filePath)
        {
            var td = new TaskDialog("Export complete")
            {
                MainInstruction = "Excel file saved.",
                MainContent = filePath + "\n\nOpen it now?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes
            };
            return td.Show() == TaskDialogResult.Yes;
        }

        /// <summary>Opens a file with its default app. Returns an error message, or null on success.</summary>
        public static string TryOpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
