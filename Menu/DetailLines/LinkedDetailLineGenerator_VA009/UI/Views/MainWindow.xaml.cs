using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.UI.Views
{
    /// <summary>
    /// Code-behind is intentionally thin â€” no Revit API or business logic here.
    /// Modeless window (Show(), not ShowDialog()) per suite convention; Esc closes it.
    /// Window never auto-closes on its own â€” only via explicit Close button or Esc.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>selectedBoundaryElement is the Floor/Roof the command required
        /// the user to pre-select â€” it's the source of the "Restrict to boundary"
        /// clip shape and never changes for the lifetime of this window.</summary>
        public MainWindow(UIApplication uiApp, Element selectedBoundaryElement)
        {
            InitializeComponent();

            var viewModel = new MainViewModel(uiApp, selectedBoundaryElement);
            viewModel.SetOwnerWindow(this);
            DataContext = viewModel;
            Closed += (_, _) => viewModel.ReleaseRevitQueue();

            // Never use System.Windows.Application.Current.MainWindow for Revit add-ins â€” use the
            // Revit main window handle via WindowInteropHelper (suite convention).
            new System.Windows.Interop.WindowInteropHelper(this).Owner = uiApp.MainWindowHandle;

            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyAllLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}

