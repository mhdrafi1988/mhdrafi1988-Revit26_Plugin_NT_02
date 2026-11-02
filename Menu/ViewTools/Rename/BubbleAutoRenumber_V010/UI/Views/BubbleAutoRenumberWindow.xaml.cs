using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.UI.Views
{
    /// <summary>
    /// CHANGED in V009: the two log Copy handlers are gone — Copy All, Copy Selected
    /// and Clear are ToolViewModelBase commands bound straight from the XAML. What is
    /// left is the constructor, the owner handle, and forwarding the log's
    /// SelectedItems, which is the one thing WPF cannot bind to a ViewModel property.
    /// </summary>
    public partial class BubbleAutoRenumberWindow : Window
    {
        public BubbleAutoRenumberWindow(UIDocument uidoc, UIApplication uiapp)
        {
            InitializeComponent();

            DataContext = new BubbleAutoRenumberViewModel(uidoc);

            // Modeless, owned by Revit's main window so it stays on top of it and
            // minimises with it (CLAUDE.md).
            new WindowInteropHelper(this) { Owner = uiapp.MainWindowHandle };
        }

        private BubbleAutoRenumberViewModel ViewModel => DataContext as BubbleAutoRenumberViewModel;

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null || sender is not ListBox list) return;

            ViewModel.SelectedLogEntries = list.SelectedItems.OfType<LogEntry>().ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
