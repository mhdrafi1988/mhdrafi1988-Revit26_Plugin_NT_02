// =======================================================
// File: CurveEdgeModel.cs
// Location: Core/Models/
// Changes vs V002: converted from hand-rolled INotifyPropertyChanged to
// CommunityToolkit.Mvvm's ObservableObject, per this suite's stack
// convention. IsSelected/IsManual use [ObservableProperty] (plain
// storage + notify). PointCount (value-clamping), HasOverride
// (multi-field side effects on turning on), OverrideMode/
// OverrideTargetMeters (each also raises FinalPointCount/rule-changed
// notifications) keep manual property bodies — using ObservableObject's
// SetProperty helper instead of hand-rolled field-compare-then-notify —
// since their setters do real work beyond plain storage, which
// [ObservableProperty] alone doesn't express. OverrideModeText and
// FinalPointCount are pure computed pass-throughs (no backing field) and
// stay as manual properties, same as before.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models
{
    /// <summary>
    /// One non-linear edge of the picked floor (one grid row). Every edge resolves to a single
    /// <see cref="FinalPointCount"/> — the exact number of points Apply places. Count-driven
    /// edges use the editable, length-seeded <see cref="PointCount"/>; distance-driven edges
    /// compute it from spacing.
    /// </summary>
    public partial class CurveEdgeModel : ObservableObject
    {
        public int    Index         { get; set; }
        public string CurveTypeName { get; set; }
        public double LengthM       { get; set; }
        public Curve  Geometry      { get; set; }
        public EdgeTypeSetting TypeSetting { get; set; }

        /// <summary>Length-bucket default number of POINTS, assigned at extraction.</summary>
        public int LengthDefaultPointCount { get; set; }

        [ObservableProperty]
        private bool isSelected = true;

        private int _pointCount;
        public int PointCount
        {
            get => _pointCount;
            set
            {
                int v = Math.Max(0, value);
                if (SetProperty(ref _pointCount, v))
                    OnPropertyChanged(nameof(FinalPointCount));
            }
        }

        [ObservableProperty]
        private bool isManual;

        private bool _hasOverride;
        public bool HasOverride
        {
            get => _hasOverride;
            set
            {
                if (SetProperty(ref _hasOverride, value))
                {
                    if (value)
                    {
                        _overrideMode         = TypeSetting?.Mode         ?? DivisionMode.ByCount;
                        _overrideTargetMeters = TypeSetting?.TargetMeters ?? 0.50;
                        OnPropertyChanged(nameof(OverrideMode));
                        OnPropertyChanged(nameof(OverrideModeText));
                        OnPropertyChanged(nameof(OverrideTargetMeters));
                    }
                    RaiseRuleChanged();
                }
            }
        }

        private DivisionMode _overrideMode = DivisionMode.ByCount;
        public DivisionMode OverrideMode
        {
            get => _overrideMode;
            set
            {
                if (SetProperty(ref _overrideMode, value))
                {
                    OnPropertyChanged(nameof(OverrideModeText));
                    RaiseRuleChanged();
                }
            }
        }

        public string OverrideModeText
        {
            get => OverrideMode == DivisionMode.ByCount ? "Count" : "Distance";
            set => OverrideMode = string.Equals(value, "Count", StringComparison.OrdinalIgnoreCase)
                                    ? DivisionMode.ByCount
                                    : DivisionMode.ByDistance;
        }

        private double _overrideTargetMeters = 0.50;
        public double OverrideTargetMeters
        {
            get => _overrideTargetMeters;
            set
            {
                if (SetProperty(ref _overrideTargetMeters, value))
                    RaiseRuleChanged();
            }
        }

        public bool OverrideDistanceEnabled => HasOverride && OverrideMode == DivisionMode.ByDistance;

        public DivisionMode EffectiveMode         => HasOverride ? OverrideMode         : (TypeSetting?.Mode         ?? DivisionMode.ByCount);
        public double       EffectiveTargetMeters => HasOverride ? OverrideTargetMeters : (TypeSetting?.TargetMeters ?? 0.50);
        public bool         IsCountDriven         => EffectiveMode == DivisionMode.ByCount;

        private int DistancePoints
        {
            get
            {
                double t = EffectiveTargetMeters;
                if (t <= 1e-9) return 0;
                int segs = (int)Math.Round(LengthM / t, MidpointRounding.AwayFromZero);
                return Math.Max(0, segs - 1);
            }
        }

        /// <summary>Exact points placed on Apply (and shown/edited in the grid).</summary>
        public int FinalPointCount
        {
            get => IsCountDriven ? Math.Max(0, PointCount) : DistancePoints;
            set
            {
                if (IsCountDriven) { PointCount = value; IsManual = true; }
                OnPropertyChanged();
            }
        }

        public void NotifyInheritedRuleChanged() => RaiseRuleChanged();

        private void RaiseRuleChanged()
        {
            OnPropertyChanged(nameof(EffectiveMode));
            OnPropertyChanged(nameof(EffectiveTargetMeters));
            OnPropertyChanged(nameof(IsCountDriven));
            OnPropertyChanged(nameof(OverrideDistanceEnabled));
            OnPropertyChanged(nameof(FinalPointCount));
        }
    }
}
