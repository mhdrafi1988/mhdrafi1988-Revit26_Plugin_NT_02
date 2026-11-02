// File: DialogService.cs
// Location: Infrastructure/Helpers/
// Ported from AutoSlopeByDrain V004 (roof) (its version was slightly more polished
// than ByPoint's — right-aligned buttons, existence checks on the initial
// directory). Label text updated from "CSV exports" to "Excel export"
// since export format changed to Excel-only per Rafi's confirmed decision.

using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
{
    public static class DialogService
    {
        public static string SelectFolder(string initialPath = "")
        {
            try
            {
                var dialog = new System.Windows.Window
                {
                    Title = "Select Export Folder",
                    Width = 450,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = initialPath,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var buttonPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };

                var browseButton = new System.Windows.Controls.Button
                {
                    Content = "Browse...",
                    Width = 80,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var okButton = new System.Windows.Controls.Button
                {
                    Content = "OK",
                    Width = 60,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var cancelButton = new System.Windows.Controls.Button
                {
                    Content = "Cancel",
                    Width = 60
                };

                string result = null;
                bool dialogResult = false;

                browseButton.Click += (s, e) =>
                {
                    var openDialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "All files (*.*)|*.*",
                        InitialDirectory = Directory.Exists(textBox.Text) ? textBox.Text :
                                          Directory.Exists(initialPath) ? initialPath :
                                          Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Title = "Select a folder (choose any file in the folder)",
                        CheckFileExists = true,
                        ValidateNames = false
                    };
                    if (openDialog.ShowDialog() == true)
                    {
                        textBox.Text = Path.GetDirectoryName(openDialog.FileName);
                    }
                };

                okButton.Click += (s, e) =>
                {
                    result = textBox.Text;
                    dialogResult = true;
                    dialog.Close();
                };

                cancelButton.Click += (s, e) =>
                {
                    dialogResult = false;
                    dialog.Close();
                };

                buttonPanel.Children.Add(browseButton);
                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);

                stackPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Select folder for Excel export:",
                    Margin = new Thickness(0, 0, 0, 5),
                    FontWeight = System.Windows.FontWeights.Bold
                });
                stackPanel.Children.Add(textBox);
                stackPanel.Children.Add(buttonPanel);

                dialog.Content = stackPanel;
                dialog.ShowDialog();

                return dialogResult ? result : null;
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
