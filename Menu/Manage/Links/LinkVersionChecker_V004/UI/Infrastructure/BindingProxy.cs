using System.Windows;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.Infrastructure
{
    /// <summary>
    /// Lets DataGrid columns (which are not in the visual tree) bind to the window's DataContext,
    /// e.g. to show the Host column only in Folder mode.
    /// </summary>
    public class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
