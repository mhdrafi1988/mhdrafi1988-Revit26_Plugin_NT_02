using System.Windows;
using BatchDwgFamilyLinker.ViewModels;

namespace BatchDwgFamilyLinker.UI
{
    public partial class BatchLinkWindow : Window
    {
        public BatchLinkWindow(BatchLinkViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
