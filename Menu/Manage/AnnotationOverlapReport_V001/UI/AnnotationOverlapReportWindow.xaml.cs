using System.Windows;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.UI
{
    /// <summary>Modeless Annotation Overlap Report window: scope, annotation types, results, Excel export.</summary>
    public partial class AnnotationOverlapReportWindow : Window
    {
        private readonly AnnotationOverlapReportViewModel _viewModel;

        /// <summary>Creates the window bound to <paramref name="viewModel"/>; saves its options on close.</summary>
        public AnnotationOverlapReportWindow(AnnotationOverlapReportViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Closed += (_, _) => _viewModel.SaveSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.ZoomToPairCommand.CanExecute(null))
                _viewModel.ZoomToPairCommand.Execute(null);
        }
    }
}
