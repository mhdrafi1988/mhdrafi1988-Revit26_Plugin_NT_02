using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>State of the live 2D preview: the exact result that Run will write.</summary>
    public sealed partial class PreviewViewModel : ObservableObject
    {
        [ObservableProperty]
        private Pattern2DResult? _result;

        /// <summary>Board ID / code / thickness under the mouse (set by the preview control).</summary>
        [ObservableProperty]
        private string _hoverText = "Hover a board to see its ID, code and thickness.";

        [ObservableProperty]
        private bool _isComputing;
    }
}
