using System.Windows;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.ViewModels;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Views
{
    /// <summary>Modeless V005 window: scan results plus Resolve Overlaps.</summary>
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
