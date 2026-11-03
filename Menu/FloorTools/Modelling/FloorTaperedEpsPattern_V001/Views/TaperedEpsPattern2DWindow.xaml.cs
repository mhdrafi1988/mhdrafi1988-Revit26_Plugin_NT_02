using System;
using System.Windows;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Views
{
    /// <summary>
    /// Modeless tool window. All logic lives in <see cref="TaperedEpsPattern2DViewModel"/>.
    /// Plain WPF Window for now; to use the repo's ToolWindowShell, change the base class here
    /// and the root element in the XAML.
    /// </summary>
    public partial class TaperedEpsPattern2DWindow : Window
    {
        public TaperedEpsPattern2DWindow(TaperedEpsPattern2DViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += OnRequestClose;
            Closed += (_, _) => viewModel.RequestClose -= OnRequestClose;
        }

        private void OnRequestClose(object? sender, EventArgs e) => Close();
    }
}
