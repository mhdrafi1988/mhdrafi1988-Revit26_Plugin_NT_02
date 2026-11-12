using System.Windows;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.ViewModels;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Views
{
    /// <summary>Modeless V006 window: scan results, Resolve Overlaps and the priority order.</summary>
    public partial class AnnotationOverlapPanel : Window
    {
        /// <summary>Creates the window bound to <paramref name="viewModel"/>; saves its options on close.</summary>
        public AnnotationOverlapPanel(AnnotationOverlapViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Closed += (_, _) => viewModel.SaveSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
