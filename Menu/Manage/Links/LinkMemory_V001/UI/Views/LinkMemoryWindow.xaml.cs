using Revit26_Plugin_Revised.LinkMemory.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Windows;

namespace Revit26_Plugin_Revised.LinkMemory.V001.UI.Views
{
    /// <summary>
    /// Link Memory window (modal); everything is bound to <see cref="LinkMemoryViewModel"/>.
    /// </summary>
    public partial class LinkMemoryWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public LinkMemoryWindow(LinkMemoryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = ToolCatalog.LinkMemory.Title;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
