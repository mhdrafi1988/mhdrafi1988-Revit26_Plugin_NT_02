using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// Titleblock usable-area margins (mm) plus ONE global gap between views.
    ///
    /// V012 CHANGE: global gap defaults changed to 20mm (was 10mm), per
    /// explicit request.
    /// </summary>
    public partial class GapSettings : ObservableObject
    {
        [ObservableProperty] private double marginTopMm = 30;
        [ObservableProperty] private double marginBottomMm = 150;
        [ObservableProperty] private double marginLeftMm = 30;
        [ObservableProperty] private double marginRightMm = 200;

        [ObservableProperty] private double globalHorizontalGapMm = 20;
        [ObservableProperty] private double globalVerticalGapMm = 20;
    }
}
