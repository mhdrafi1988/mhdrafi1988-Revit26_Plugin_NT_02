using System;
using System.Windows;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.Views
{
    /// <summary>
    /// Code-behind for the post-export review. NEW in V004.
    /// <para>
    /// View-only: it owns nothing but the dialog result. The window is shown with
    /// <see cref="Window.ShowDialog"/> — modal on purpose, since the export run is already finished
    /// and the main window has nothing to do until the review is answered. CLAUDE.md's
    /// modeless-<c>Show()</c> convention is about tool windows, not a confirmation dialog hanging
    /// off one.
    /// </para>
    /// </summary>
    public partial class PostExportReviewWindow : Window
    {
        private readonly PostExportReviewViewModel _viewModel;

        /// <summary>Creates the review window over <paramref name="viewModel"/>.</summary>
        public PostExportReviewWindow(PostExportReviewViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            _viewModel.CloseRequested += OnCloseRequested;
            Closed += (_, _) => _viewModel.CloseRequested -= OnCloseRequested;
        }

        /// <summary>
        /// True when the user pressed Apply. Read by the ViewModel after
        /// <see cref="Window.ShowDialog"/> returns — "Skip all", Close and the title-bar X all
        /// leave the model untouched.
        /// </summary>
        public bool Confirmed { get; private set; }

        private void OnCloseRequested(object sender, bool apply)
        {
            Confirmed = apply;
            // DialogResult can only be set on a window shown with ShowDialog; guard so a stray
            // non-modal show can't throw here.
            try { DialogResult = apply; }
            catch (InvalidOperationException) { }
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            try { DialogResult = false; }
            catch (InvalidOperationException) { }
            Close();
        }
    }
}
