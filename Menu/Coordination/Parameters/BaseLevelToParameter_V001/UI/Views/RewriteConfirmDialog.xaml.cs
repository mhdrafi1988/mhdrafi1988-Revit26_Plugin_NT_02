using System.Windows;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.Views
{
    /// <summary>Modal "Rewrite existing values?" dialog shown by Run (1.2.0).</summary>
    public partial class RewriteConfirmDialog : Window
    {
        /// <summary>Creates the dialog for <paramref name="confirmation"/>.</summary>
        public RewriteConfirmDialog(RewriteConfirmation confirmation)
        {
            InitializeComponent();
            DataContext = confirmation;
        }

        /// <summary>The user's answer; Cancel when the dialog was closed another way.</summary>
        public RewriteAnswer Answer { get; private set; } = RewriteAnswer.Cancel;

        private void Rewrite_Click(object sender, RoutedEventArgs e)
        {
            Answer = RewriteAnswer.Rewrite;
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Answer = RewriteAnswer.Skip;
            DialogResult = true;
        }
    }
}
