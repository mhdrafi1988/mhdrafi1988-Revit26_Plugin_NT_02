using System.Windows;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.Views
{
    /// <summary>
    /// Asks before the re-host changes the model, and carries the shape-edited override back to the window.
    /// </summary>
    public partial class RehostConfirmDialog : Window
    {
        /// <summary>Creates the dialog for <paramref name="confirmation"/>.</summary>
        public RehostConfirmDialog(RehostConfirmation confirmation)
        {
            InitializeComponent();
            Confirmation = confirmation;
            DataContext = confirmation;
        }

        /// <summary>The confirmation, with <see cref="RehostConfirmation.IncludeShapeEdited"/> as the user left it.</summary>
        public RehostConfirmation Confirmation { get; }

        private void Run_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
