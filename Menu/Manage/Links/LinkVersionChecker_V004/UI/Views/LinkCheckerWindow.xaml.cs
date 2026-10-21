using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.ViewModels;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.Views
{
    public partial class LinkCheckerWindow : Window
    {
        private readonly LinkCheckerViewModel _vm;

        public LinkCheckerWindow(LinkCheckerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            var s = vm.Settings;
            if (s.WindowWidth >= MinWidth) Width = s.WindowWidth;
            if (s.WindowHeight >= MinHeight) Height = s.WindowHeight;
            if (s.WindowMaximized) WindowState = WindowState.Maximized;

            Loaded += async (_, __) => await _vm.OnLoadedAsync();
            Closing += OnClosing;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_vm.IsBusy)
            {
                _vm.CancelRunning();
                e.Cancel = true;   // let the running step stop cleanly; close again afterwards
                return;
            }

            var s = _vm.Settings;
            s.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                s.WindowWidth = ActualWidth;
                s.WindowHeight = ActualHeight;
            }
            _vm.SaveSettings();
        }
        private void CheckBoxCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb)
            {
                cb.IsChecked = !cb.IsChecked;
                e.Handled = true;
            }
        }
    }
}
