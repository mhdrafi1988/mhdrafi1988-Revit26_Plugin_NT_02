using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.Views
{
    /// <summary>
    /// Section Roof Detail Lines window. Modeless: everything it shows is bound, and the only
    /// code here keeps a request that is still running in Revit from calling back into a closed
    /// window, and saves the window's choices on the way out.
    /// </summary>
    public partial class SectionRoofDetailLinesWindow : Window
    {
        private readonly ViewModel_SectionRoofDetailLines _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SectionRoofDetailLinesWindow(ViewModel_SectionRoofDetailLines viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.SectionRoofDetailLines.Title;
            _viewModel.OwnerWindow = this;

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;
        }

        /// <summary>
        /// A request still running in Revit would call back into a closed window, so the window
        /// waits for it. Nothing is unsubscribed here: no Revit event is subscribed from the WPF
        /// thread in this tool, which is what would need an API context to undo.
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel.IsBusy)
            {
                e.Cancel = true;
                MessageBox.Show(this,
                    "Revit is still working on the last request. Close the window when it has finished.",
                    ToolCatalog.SectionRoofDetailLines.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _viewModel.SaveSettings();
        }

        /// <summary>Esc closes the window; Enter in a number box commits what was typed.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox box)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogListBox.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
