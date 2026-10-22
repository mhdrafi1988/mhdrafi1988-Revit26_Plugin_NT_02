using System.Windows;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.UI.ViewModels;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.UI.Views
{
    /// <summary>Link Version Checker V006 window: status of Desktop Connector RVT links.</summary>
    public partial class LinkStatusWindow : Window
    {
        /// <summary>Creates the window over <paramref name="vm"/>.</summary>
        public LinkStatusWindow(LinkStatusViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
