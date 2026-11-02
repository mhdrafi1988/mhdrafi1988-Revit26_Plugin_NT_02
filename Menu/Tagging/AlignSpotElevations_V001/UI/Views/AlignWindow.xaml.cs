using Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Windows;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.Views
{
    /// <summary>Options and preview for Align Spot Elevations. Shown modally; DialogResult is true on Apply.</summary>
    public partial class AlignWindow : Window
    {
        private readonly AlignViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public AlignWindow(AlignViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.AlignSpotElevations.Title;
            _viewModel.PropertyChanged += (_, __) => UpdateColumns();
            UpdateColumns();
            SkippedText.Text = _viewModel.HasSkipped ? $"{_viewModel.Skipped.Count} skipped — click for details" : string.Empty;
        }

        /// <summary>Shows the leader end and bend columns only while those parts are ticked.</summary>
        private void UpdateColumns()
        {
            EndColumn.Visibility = _viewModel.MoveLeaderEnd || _viewModel.MoveLeaderShoulder ? Visibility.Visible : Visibility.Collapsed;
            BendColumn.Visibility = _viewModel.MoveLeaderShoulder ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Skipped_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show(this, string.Join(Environment.NewLine, _viewModel.Skipped),
                "Skipped spot elevations", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
