using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.ViewModels;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.Views
{
    /// <summary>
    /// Code-behind is intentionally minimal — all behavior is driven by
    /// WorksetsElementsBrowserViewModel via data binding and commands (MVVM).
    /// The only exception is the Type-row click, which needs the raw mouse
    /// event (a TreeView item click doesn't distinguish "clicked the
    /// checkbox" from "clicked the label" through commands alone).
    /// </summary>
    public partial class WorksetsElementsBrowserWindow : Window
    {
        public WorksetsElementsBrowserWindow()
        {
            InitializeComponent();
        }

        private void TypeName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not TreeNodeViewModel node) return;
            if (node.Kind != ElementTreeNodeKind.Type) return;

            (DataContext as WorksetsElementsBrowserViewModel)?.OnTypeRowClicked(node);
            e.Handled = true;
        }

        private void ShowTypeIn3D_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not TreeNodeViewModel node) return;
            if (node.Kind == ElementTreeNodeKind.Workset) return;

            (DataContext as WorksetsElementsBrowserViewModel)?.OnTypeRowClicked(node);
            e.Handled = true;
        }

        private void SelectRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not TreeNodeViewModel node) return;
            if (node.Kind == ElementTreeNodeKind.Workset) return;

            (DataContext as WorksetsElementsBrowserViewModel)?.SelectRow(node);
            e.Handled = true;
        }

        private void CheckSameInAllWorksets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not TreeNodeViewModel node) return;

            (DataContext as WorksetsElementsBrowserViewModel)?.CheckSameInAllWorksets(node);
            e.Handled = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            (DataContext as WorksetsElementsBrowserViewModel)?.Dispose();
        }
    }
}
