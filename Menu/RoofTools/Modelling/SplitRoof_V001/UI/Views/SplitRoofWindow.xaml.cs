using System;
using System.Windows;
using Revit26_Plugin_Revised.SplitRoof.V001.UI.ViewModels;

namespace Revit26_Plugin_Revised.SplitRoof.V001.UI.Views
{
    /// <summary>Split Roof window: roof, cut lines, piece preview, Check / Split roof.</summary>
    public partial class SplitRoofWindow : Window
    {
        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SplitRoofWindow(SplitRoofViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = viewModel.Title;
        }
    }
}
