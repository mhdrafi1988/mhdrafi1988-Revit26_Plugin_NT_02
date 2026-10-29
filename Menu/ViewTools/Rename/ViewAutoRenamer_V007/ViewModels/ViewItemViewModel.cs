using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;

public partial class ViewItemViewModel : ObservableObject
{
    // ── Immutable Revit data ────────────────────────────────────────────────
    public ElementId    ElementId    { get; }
    public string       OriginalName { get; }
    public ViewType     ViewType     { get; }
    public ViewTypeGroup TypeGroup   { get; }

    /// <summary>Display label for the View Type pill/column (e.g. "Floor Plan").</summary>
    public string ViewTypeDisplay { get; }

    /// <summary>
    /// First sheet number found this view is placed on (Sections/Callouts/
    /// Elevations/Drafting views placed directly). Null if not placed.
    /// For Schedules/Legends this is populated separately via
    /// PlacedSheetNumbers since a single view can be on several sheets.
    /// </summary>
    public string? SheetNumber  { get; private set; }
    public string? DetailNumber { get; }

    /// <summary>
    /// All sheets this view is placed on (only meaningful for Legends and
    /// Schedules, which can be placed multiple times). SheetNumber above
    /// is simply the first entry here for display in the grid's Sheet column.
    /// </summary>
    public IReadOnlyList<string> PlacedSheetNumbers { get; private set; }

    public bool IsPlaced => PlacedSheetNumbers.Count > 0;

    /// <summary>Replaces the sheet list (used when the full project load completes a Legend/Schedule's placements).</summary>
    public void UpdatePlacement(IReadOnlyList<string> sheets)
    {
        PlacedSheetNumbers = sheets;
        SheetNumber = sheets.Count > 0 ? sheets[0] : null;
        OnPropertyChanged(nameof(PlacedSheetNumbers));
        OnPropertyChanged(nameof(SheetNumber));
        OnPropertyChanged(nameof(IsPlaced));
    }
    public bool IsPinned { get; }

    // ── Observable mutable state ────────────────────────────────────────────
    [ObservableProperty] private bool   isSelected;
    [ObservableProperty] private string editableName;
    [ObservableProperty] private string previewName;
    [ObservableProperty] private bool   isDuplicate;

    public ViewItemViewModel(
        View view,
        ViewTypeGroup typeGroup,
        string viewTypeDisplay,
        IReadOnlyList<string> placedSheetNumbers,
        string? detailNumber)
    {
        ElementId          = view.Id;
        OriginalName       = view.Name;
        ViewType           = view.ViewType;
        TypeGroup          = typeGroup;
        ViewTypeDisplay    = viewTypeDisplay;
        PlacedSheetNumbers = placedSheetNumbers;
        SheetNumber        = placedSheetNumbers.Count > 0 ? placedSheetNumbers[0] : null;
        DetailNumber       = detailNumber;
        IsPinned           = view.Pinned;

        editableName = OriginalName;
        previewName  = OriginalName;
    }
}
