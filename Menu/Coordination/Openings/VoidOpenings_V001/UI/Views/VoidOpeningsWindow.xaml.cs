using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.Views
{
    /// <summary>
    /// Void Openings window: the Setup tab (STRUCTURAL MODEL and MEP MODEL panels, OPENING CLEARANCE
    /// card), the Results tab, the Analyze bar, the notification strip and the status bar.
    /// </summary>
    public partial class VoidOpeningsWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public VoidOpeningsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = viewModel.Title;

            // Started through the dispatcher so the first read continues on the window's thread.
            Loaded += (sender, args) => Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(async () => await viewModel.InitializeAsync()));
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // DataGrid.SelectedItems cannot be bound, so the selection is handed to the view model here.
        private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && DataContext is MainViewModel viewModel)
                viewModel.Results.SetSelection(grid.SelectedItems.OfType<ClashRowViewModel>().ToList());
        }

        private void OnCopyNotAnalysedClick(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is MainViewModel viewModel) || string.IsNullOrEmpty(viewModel.Results.NotAnalysedText))
                return;
            try
            {
                Clipboard.SetText(viewModel.Results.NotAnalysedText);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another program holds the clipboard for a moment; pressing the button again works.
                MessageBox.Show(this, "The clipboard is in use by another program. Press Copy list again.", viewModel.Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>Stops a running Analyze when the window has closed.</summary>
        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainViewModel)?.CancelAnalysis();
            base.OnClosed(e);
        }

        /// <summary>Asks before unsaved clearance rules are lost: Yes saves, No drops them, Cancel keeps the window open.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || !(DataContext is MainViewModel viewModel) || !viewModel.Clearance.IsDirty)
                return;

            MessageBoxResult answer = MessageBox.Show(this,
                "The clearance rules have unsaved changes.\n\nSave them before closing?",
                viewModel.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);

            if (answer == MessageBoxResult.Cancel)
                e.Cancel = true;
            else if (answer == MessageBoxResult.Yes && !viewModel.Clearance.TrySave())
                e.Cancel = true; // not saved (an error on the card, or the file could not be written): the strip says why
        }
    }
}
