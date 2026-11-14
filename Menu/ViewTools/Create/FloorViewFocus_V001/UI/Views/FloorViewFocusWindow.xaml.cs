using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Revit26_Plugin_Revised.FloorViewFocus.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.UI.Views
{
    /// <summary>Modeless window. Never auto-closes; Esc / Close button close it.</summary>
    public partial class FloorViewFocusWindow : Window
    {
        private readonly FloorViewFocusViewModel _vm;

        public FloorViewFocusWindow(FloorViewFocusViewModel vm)
        {
            InitializeComponent();

            _vm = vm;
            DataContext = vm;

            ((INotifyCollectionChanged)vm.Log).CollectionChanged += OnLogChanged;

            Closing += (_, _) => _vm.OnWindowClosing();
            Closed += (_, _) => ((INotifyCollectionChanged)_vm.Log).CollectionChanged -= OnLogChanged;
        }

        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || LogList.Items.Count == 0) return;

            // Defer until the new row's container exists.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }), DispatcherPriority.Background);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
