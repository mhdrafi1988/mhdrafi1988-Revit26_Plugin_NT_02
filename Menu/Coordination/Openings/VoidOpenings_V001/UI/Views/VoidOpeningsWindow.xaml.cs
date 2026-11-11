using System;
using System.Windows;
using System.Windows.Threading;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.Views
{
    /// <summary>Void Openings window: STRUCTURAL MODEL and MEP MODEL panels, notification strip and status bar.</summary>
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
    }
}
