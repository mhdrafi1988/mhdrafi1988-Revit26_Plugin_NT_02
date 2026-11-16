using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.Views
{
    /// <summary>
    /// Void Openings window: STRUCTURAL MODEL and MEP MODEL panels, the OPENING CLEARANCE card, the
    /// notification strip and the status bar.
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
