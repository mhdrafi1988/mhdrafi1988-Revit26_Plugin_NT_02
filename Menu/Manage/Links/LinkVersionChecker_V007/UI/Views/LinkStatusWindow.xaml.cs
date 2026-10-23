using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.UI.ViewModels;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.UI.Views
{
    /// <summary>Link Version Checker V007 window: status of ACC RVT links (Desktop Connector and cloud).</summary>
    public partial class LinkStatusWindow : Window
    {
        private readonly LinkStatusViewModel _vm;
        private bool _restoring;

        /// <summary>Creates the window over <paramref name="vm"/>.</summary>
        public LinkStatusWindow(LinkStatusViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.RestoreSelection += OnRestoreSelection;
            Closed += (_, _) => vm.RestoreSelection -= OnRestoreSelection;
        }

        // The grid's multi-selection cannot be bound, so it is passed to the view model here.
        private void LinkGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoring) return;
            _vm.Selection = LinkGrid.SelectedItems.OfType<LinkStatusRow>().ToList();
        }

        // After a refresh the rows are new objects; select the same links again.
        private void OnRestoreSelection(IReadOnlyList<LinkStatusRow> rows)
        {
            _restoring = true;
            try
            {
                LinkGrid.SelectedItems.Clear();
                foreach (LinkStatusRow r in rows)
                    if (LinkGrid.Items.Contains(r)) LinkGrid.SelectedItems.Add(r);   // a filtered-out row cannot be selected
            }
            finally
            {
                _restoring = false;
            }
            _vm.Selection = LinkGrid.SelectedItems.OfType<LinkStatusRow>().ToList();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
