using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Revit26_Plugin_Revised.RoofTypeCreator.V002.Core.Models;
using Revit26_Plugin_Revised.RoofTypeCreator.V002.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RoofTypeCreator.V002.UI.Views
{
    public partial class RoofTypeManagerWindow : Window
    {
        private RoofTypeManagerViewModel Vm => DataContext as RoofTypeManagerViewModel;

        public RoofTypeManagerWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Vm?.WindowLoadedCommand.Execute(null);

            // Auto-scroll log when new entries are added
            if (Vm != null)
            {
                Vm.LogEntries.CollectionChanged += (_, args) =>
                {
                    if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                        && LogListBox.Items.Count > 0)
                    {
                        // Deferred: scrolling inside CollectionChanged makes WPF throw
                        // "ItemsControl is inconsistent with its items source".
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
                        {
                            if (LogListBox.Items.Count > 0)
                                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                        }));
                    }
                };
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            Vm?.Dispose();
            base.OnClosed(e);
        }

        private void ExpandRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is RoofTypeItem item)
            {
                item.IsExpanded = !item.IsExpanded;
                var row = ExportGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row != null)
                    row.DetailsVisibility = item.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ExpandImportRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ImportPreviewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
                var row = ImportGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row != null)
                    row.DetailsVisibility = item.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CopySelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = LogListBox.SelectedItems
                .OfType<LogEntry>()
                .Select(l => l.ToString());
            var text = string.Join(System.Environment.NewLine, selected);
            if (!string.IsNullOrEmpty(text))
            {
                try { System.Windows.Clipboard.SetText(text); }
                catch { /* clipboard locked */ }
            }
        }
    }
}
