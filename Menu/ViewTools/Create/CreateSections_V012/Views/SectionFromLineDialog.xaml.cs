using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Revit26_Plugin_Revised.CreateSections.V012.ViewModels;

namespace Revit26_Plugin_Revised.CreateSections.V012.Views.SectionFromLineDialog
{
    /// <summary>
    /// Interaction logic for SectionFromLineDialog.xaml. View wiring only.
    ///
    /// V008 changes from V07:
    /// - Dialog no longer no-ops on CreateRequested; the Command now wires
    ///   CreateRequested directly to the ExternalEvent's Raise() (see
    ///   CreateSectionsFromDetailLinesCommand).
    /// - Added accordion toggle for the Live Log panel, Copy All/Copy
    ///   Selected/Clear handlers, and settings save on close.
    ///
    /// V009 changes from V008:
    /// - Added the same accordion toggle pattern for the new View Crop
    ///   card. Per confirmed spec, expanded/collapsed state is NOT
    ///   persisted to settings.json — always starts collapsed.
    /// - Added PreviewRowCheckBox_PreviewMouseLeftButtonDown, per suite
    ///   DATAGRID SPEC rule 2 — blocks the checkbox click from also
    ///   triggering DataGridRow's native row-select cascade.
    ///
    /// V010 fix (log dialog no longer auto-opens):
    /// - Added BrowseLogFolder wiring — the VM raises BrowseLogFolderRequested
    ///   (no dialog types in the ViewModel, per MVVM) and this code-behind
    ///   shows the folder picker, owned by this window. OpenFolderDialog
    ///   (.NET 8+ Microsoft.Win32) is used instead of the WinForms
    ///   FolderBrowserDialog per existing suite convention.
    /// </summary>
    public partial class SectionFromLineDialog : Window
    {
        public SectionFromLineViewModel ViewModel { get; }

        private bool _logExpanded = true;   // V012: log starts open (fixed footer)
        private bool _viewCropExpanded = false;

        public SectionFromLineDialog(UIDocument uiDoc, UIApplication uiApp)
        {
            InitializeComponent();

            ViewModel = new SectionFromLineViewModel(uiDoc.Document);
            DataContext = ViewModel;

            // Auto-scroll to the bottom when a new log entry is added
            ViewModel.LiveLog.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    // Deferred: scrolling inside CollectionChanged makes WPF throw
                    // "ItemsControl is inconsistent with its items source".
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
                    {
                        var last = ViewModel.LiveLog.LastOrDefault();
                        if (last != null) LogListBox.ScrollIntoView(last);
                    }));
                }
            };

            ViewModel.CloseRequested += OnCloseRequested;
            ViewModel.BrowseLogFolderRequested += OnBrowseLogFolderRequested;
            Closing += (_, __) => ViewModel.SaveSettings();
        }

        private void OnCloseRequested() => Close();

        // ================= LOG SAVE FOLDER (V010 fix) =================

        private void OnBrowseLogFolderRequested()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose a folder for Create Sections From Detail Lines logs"
            };

            if (dialog.ShowDialog(this) == true)
                ViewModel.LogSaveFolder = dialog.FolderName;
        }

        // ================= LIVE LOG ACCORDION =================

        private void LogHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _logExpanded = !_logExpanded;
            LogListBox.Visibility = _logExpanded ? Visibility.Visible : Visibility.Collapsed;
            LogChevron.Text = _logExpanded ? "▼" : "▶";
        }

        // ================= VIEW CROP ACCORDION (V009 — new) =================

        private void ViewCropHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _viewCropExpanded = !_viewCropExpanded;
            ViewCropBody.Visibility = _viewCropExpanded ? Visibility.Visible : Visibility.Collapsed;
            ViewCropChevron.Text = _viewCropExpanded ? "▼" : "▶";
        }

        // ================= LOG ACTIONS =================

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var entry in ViewModel.LiveLog)
                sb.AppendLine(entry.ToString());

            if (sb.Length > 0)
                System.Windows.Clipboard.SetText(sb.ToString());
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = LogListBox.SelectedItems.Cast<object>().ToList();
            if (selected.Count == 0)
                return;

            var sb = new StringBuilder();
            foreach (var item in selected)
                sb.AppendLine(item.ToString());

            System.Windows.Clipboard.SetText(sb.ToString());
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LiveLog.Clear();
        }

        // ================= SECTIONS TO CREATE GRID (V009 — new) =================

        /// <summary>
        /// Per suite DATAGRID SPEC rule 2: blocks the checkbox click from
        /// also triggering the native DataGridRow selection cascade — the
        /// row's checkbox binding (IsIncluded) is the only thing that
        /// should react to this click.
        /// </summary>
        private void PreviewRowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false; // let the CheckBox itself toggle...
            if (sender is System.Windows.Controls.CheckBox cb)
            {
                cb.IsChecked = !(cb.IsChecked ?? false);
                e.Handled = true; // ...but stop it from bubbling into row selection.
            }
        }
    }
}
