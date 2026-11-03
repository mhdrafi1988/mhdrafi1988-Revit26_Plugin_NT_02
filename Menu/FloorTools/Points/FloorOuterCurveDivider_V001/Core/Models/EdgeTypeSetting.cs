// =======================================================
// File: EdgeTypeSetting.cs
// Location: Core/Models/
// Changes vs V002: converted from hand-rolled INotifyPropertyChanged to
// CommunityToolkit.Mvvm's ObservableObject + [ObservableProperty], per
// this suite's stack convention. ModeByDistance/ModeByCount are mutually
// exclusive radio-style flags — that cross-field side effect can't be
// expressed by [ObservableProperty] alone, so both keep their manual
// setters (using ObservableObject's SetProperty helper) exactly as
// before; only the notification plumbing changed.
// =======================================================

using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models
{
    /// <summary>
    /// Default division rule for one curve type (e.g. all Arcs). Edges of that type follow
    /// this unless overridden or manually edited. Default mode is Count so each edge shows
    /// its length-seeded point count on open; the type's Count field bulk-fills that type's
    /// (non-manual) edges.
    /// </summary>
    public partial class EdgeTypeSetting : ObservableObject
    {
        public string TypeName { get; set; }

        private bool _modeByDistance;
        public bool ModeByDistance
        {
            get => _modeByDistance;
            set
            {
                if (SetProperty(ref _modeByDistance, value))
                {
                    if (value && _modeByCount) { _modeByCount = false; OnPropertyChanged(nameof(ModeByCount)); }
                    OnPropertyChanged(nameof(Mode));
                }
            }
        }

        private bool _modeByCount = true;
        public bool ModeByCount
        {
            get => _modeByCount;
            set
            {
                if (SetProperty(ref _modeByCount, value))
                {
                    if (value && _modeByDistance) { _modeByDistance = false; OnPropertyChanged(nameof(ModeByDistance)); }
                    OnPropertyChanged(nameof(Mode));
                }
            }
        }

        [ObservableProperty]
        private double targetMeters = 0.50;

        [ObservableProperty]
        private int fixedCount = 8;

        public DivisionMode Mode => ModeByCount ? DivisionMode.ByCount : DivisionMode.ByDistance;
    }
}
