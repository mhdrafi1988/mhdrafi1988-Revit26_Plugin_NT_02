using System;
using System.Windows;
using Revit26_Plugin_Revised.MergeRoofs.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.UI.Views
{
    /// <summary>Merge Roofs window: roof list, gap tolerance, Check / Create roof.</summary>
    public partial class MergeRoofsWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public MergeRoofsWindow(MergeRoofsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = viewModel.Title;
        }
    }
}
