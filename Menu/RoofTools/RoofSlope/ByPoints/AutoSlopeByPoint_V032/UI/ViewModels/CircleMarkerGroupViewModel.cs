// =======================================================
// File: CircleMarkerGroupViewModel.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.UI.ViewModels
// Replaces V030's Core/Models/CircleMarkerGroup (an ObservableObject with
// an ElementId living in Core). This is UI state; the run receives an
// immutable MarkerStyle snapshot (B7). One class drives one DataTemplate
// for all three groups (plan 2.4); group-only extras are flags.
// V032: IsAtDefaults (drives the section's "Custom" tag) and ResetToDefaults.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.RoofOps;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.UI.ViewModels
{
    public partial class CircleMarkerGroupViewModel : ObservableObject
    {
        public CircleMarkerGroupViewModel(
            MarkerGroupKind kind, string label, string description,
            string defaultColor, IReadOnlyList<LineStyleInfo> lineStyles)
        {
            Kind = kind;
            Label = label;
            Description = description;
            LineStyleOptions = lineStyles;
            _defaultColor = defaultColor;
            _defaultLineStyle = lineStyles.FirstOrDefault(s => s.Name == "Thin Lines") ?? lineStyles.FirstOrDefault();
            ResetToDefaults();
        }

        private readonly string _defaultColor;
        private readonly LineStyleInfo _defaultLineStyle;

        /// <summary>True when every value equals the tool default (no "Custom" tag).</summary>
        public bool IsAtDefaults =>
            IsEnabled
            && ColorName == _defaultColor
            && ReferenceEquals(SelectedLineStyle, _defaultLineStyle)
            && Same(RadiusText, AutoSlopeConstants.DefaultMarkerRadiusMm)
            && (!HasOffsetTextOption || (ShowOffsetText && !LabelCornerPoints && !LabelAllPoints))
            && (!HasThresholdOption || Same(ThresholdText, AutoSlopeConstants.DefaultAllowedOffsetThresholdMm));

        public void ResetToDefaults()
        {
            IsEnabled = true;
            ColorName = _defaultColor;
            SelectedLineStyle = _defaultLineStyle;
            RadiusText = InputParsing.Format(AutoSlopeConstants.DefaultMarkerRadiusMm);
            ThresholdText = InputParsing.Format(AutoSlopeConstants.DefaultAllowedOffsetThresholdMm);
            ShowOffsetText = true;
            LabelCornerPoints = false;
            LabelAllPoints = false;
        }

        private static bool Same(string text, double expected) =>
            InputParsing.TryParse(text, out double v) && Math.Abs(v - expected) < 1e-9;

        public MarkerGroupKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        public IReadOnlyList<LineStyleInfo> LineStyleOptions { get; }
        public IReadOnlyList<string> ColorPalette => NamedColors.PaletteNames;

        public bool HasOffsetTextOption => Kind == MarkerGroupKind.HighestPoint;
        public bool HasThresholdOption => Kind == MarkerGroupKind.AllowedOffset;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChipText))]
        private bool isEnabled = true;

        [ObservableProperty]
        private LineStyleInfo selectedLineStyle;

        [ObservableProperty]
        private string colorName;

        [ObservableProperty]
        private string radiusText;

        [ObservableProperty]
        private string radiusError;

        [ObservableProperty]
        private bool showOffsetText = true;

        [ObservableProperty]
        private bool labelCornerPoints;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanLabelCorners))]
        private bool labelAllPoints;

        /// <summary>The corner tick is redundant (and disabled) while "all points" is ticked.</summary>
        public bool CanLabelCorners => !LabelAllPoints;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChipText))]
        private string thresholdText;

        [ObservableProperty]
        private string thresholdError;

        /// <summary>Short label for the collapsed Circle markers header.</summary>
        public string ChipText => Kind == MarkerGroupKind.AllowedOffset ? $"Offset ≥ {ThresholdText} mm" : Label;

        /// <summary>Sets the inline errors and returns them (empty when valid or disabled).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            RadiusError = null;
            ThresholdError = null;
            if (!IsEnabled) return errors;

            RadiusError = InputParsing.CheckRange(RadiusText,
                AutoSlopeConstants.MarkerRadiusMinMm, AutoSlopeConstants.MarkerRadiusMaxMm,
                $"{Label} radius", "mm", out _);
            if (RadiusError != null) errors.Add(RadiusError);

            if (HasThresholdOption)
            {
                ThresholdError = InputParsing.CheckRange(ThresholdText,
                    AutoSlopeConstants.OffsetThresholdMinMm, AutoSlopeConstants.OffsetThresholdMaxMm,
                    "Offset threshold", "mm", out _);
                if (ThresholdError != null) errors.Add(ThresholdError);
            }
            return errors;
        }

        public double ThresholdMm =>
            InputParsing.TryParse(ThresholdText, out double v) ? v : AutoSlopeConstants.DefaultAllowedOffsetThresholdMm;

        public MarkerStyle ToStyle()
        {
            InputParsing.TryParse(RadiusText, out double radius);
            return new MarkerStyle(
                Kind, Label, IsEnabled,
                SelectedLineStyle?.IdValue ?? 0,
                SelectedLineStyle?.Name,
                ColorName,
                radius,
                HasOffsetTextOption && ShowOffsetText,
                HasOffsetTextOption && LabelCornerPoints,
                HasOffsetTextOption && LabelAllPoints);
        }

        public void Apply(CircleMarkerGroupSettings saved)
        {
            if (saved == null) return;
            IsEnabled = saved.IsEnabled;
            if (NamedColors.IsKnown(saved.ColorName)) ColorName = saved.ColorName;
            if (saved.RadiusMm > 0) RadiusText = InputParsing.Format(saved.RadiusMm);
            ShowOffsetText = saved.ShowOffsetText;
            LabelCornerPoints = saved.LabelCornerPoints;
            LabelAllPoints = saved.LabelAllPoints;

            if (!string.IsNullOrWhiteSpace(saved.LineStyleName))
            {
                var match = LineStyleOptions.FirstOrDefault(s => s.Name == saved.LineStyleName);
                if (match != null) SelectedLineStyle = match; // missing in this project → keep the default
            }
        }

        public CircleMarkerGroupSettings ToSettings()
        {
            InputParsing.TryParse(RadiusText, out double radius);
            return new CircleMarkerGroupSettings
            {
                IsEnabled = IsEnabled,
                LineStyleName = SelectedLineStyle?.Name,
                ColorName = ColorName,
                RadiusMm = radius > 0 ? radius : AutoSlopeConstants.DefaultMarkerRadiusMm,
                ShowOffsetText = ShowOffsetText,
                LabelCornerPoints = LabelCornerPoints,
                LabelAllPoints = LabelAllPoints
            };
        }
    }
}
