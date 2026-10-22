using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.PlanFromScopeBox.V005.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.PlanFromScopeBox.V005.UI.Views
{
    /// <summary>
    /// Modeless window for the Plan From Scope Box tool. Parented to the Revit main window.
    /// Auto-scrolls the activity log, pushes log selection to the VM for Copy Selected, and
    /// saves settings on close.
    /// </summary>
    public partial class PlanFromScopeBoxWindow : Window
    {
        private readonly PlanFromScopeBoxViewModel _vm;

        public PlanFromScopeBoxWindow(UIApplication uiApp)
        {
            InitializeComponent();
            LoadSharedStyles();

            _vm = new PlanFromScopeBoxViewModel(uiApp);
            DataContext = _vm;

            new WindowInteropHelper(this).Owner = uiApp.MainWindowHandle;

            _vm.LogEntries.CollectionChanged += OnLogChanged;

            Closed += (_, __) =>
            {
                _vm.LogEntries.CollectionChanged -= OnLogChanged;
                _vm.Dispose();   // saves settings + disposes external event
            };
        }

        /// <summary>
        /// V004 — defensive fallback only, mirrors SmartViewToSheetPlacer_V221's
        /// window. The primary SharedStyles.xaml merge happens statically in XAML
        /// (Window.Resources) via an assembly-relative pack URI, resolved at build
        /// time. This is a safe no-op in the normal case; it exists only in case
        /// that relative pack URI ever fails to resolve in some Revit deployment/
        /// runtime scenario.
        /// </summary>
        private void LoadSharedStyles()
        {
            try
            {
                bool alreadyMerged = Resources.MergedDictionaries
                    .Any(d => d.Source != null && d.Source.OriginalString.Contains("SharedStyles.xaml"));
                if (alreadyMerged)
                    return;

                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                string sharedStylesPath = Path.Combine(asmDir, "Shared", "SharedStyles.xaml");

                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(sharedStylesPath, UriKind.Absolute)
                };

                Resources.MergedDictionaries.Insert(0, dictionary);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Plan From Scope Box — Style Load Warning",
                    $"Could not load shared styles: {ex.Message}\n\nThe window will display with default WPF styling.");
            }
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (LogList.Items.Count == 0) return;
            // Deferred: scrolling inside CollectionChanged can make WPF throw
            // "ItemsControl is inconsistent with its items source".
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new System.Action(() => FindScrollViewer(LogList)?.ScrollToBottom()));
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedLogEntries = LogList.SelectedItems.Cast<LogEntry>().ToList();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                ScrollViewer found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
