using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>Layers card: the four layers, contour interval, labels, grouping options.</summary>
    public sealed partial class LayersViewModel : ObservableObject
    {
        public LayersViewModel(IReadOnlyList<LineStyleOption> styles, IReadOnlyList<TextNoteTypeOption> textTypes)
        {
            Grid = new LayerOptionViewModel(PatternLayer.Grid, "Board grid", true, styles, LineStyleProvider.DefaultGrid);
            HipRidge = new LayerOptionViewModel(PatternLayer.HipRidge, "Hips / ridges", true, styles, LineStyleProvider.DefaultHipRidge);
            Contours = new LayerOptionViewModel(PatternLayer.Contour, "Thickness contours", true, styles, LineStyleProvider.DefaultContour);
            ContourInterval = new NumericFieldViewModel("Contour interval", "mm",
                SettingsValidator.MinContourIntervalMm, SettingsValidator.MaxContourIntervalMm, 10, "0");
            TextTypes = textTypes;
            _selectedTextType = textTypes.FirstOrDefault();
            _selectedLabelMode = LabelModes[0];
            _selectedGrouping = GroupingModes[0];

            foreach (LayerOptionViewModel layer in LineLayers)
            {
                layer.Changed += (_, _) => RaiseChanged();
            }

            ContourInterval.ValueChanged += (_, _) => RaiseChanged();
        }

        public LayerOptionViewModel Grid { get; }
        public LayerOptionViewModel HipRidge { get; }
        public LayerOptionViewModel Contours { get; }
        public IReadOnlyList<LayerOptionViewModel> LineLayers => new[] { Grid, HipRidge, Contours };

        public NumericFieldViewModel ContourInterval { get; }

        /// <summary>The fourth layer checkbox: board labels (default off = "None").</summary>
        [ObservableProperty]
        private bool _labelsEnabled;

        public IReadOnlyList<ChoiceOption<LabelMode>> LabelModes { get; } = new[]
        {
            new ChoiceOption<LabelMode>(LabelMode.Id, "Board ID"),
            new ChoiceOption<LabelMode>(LabelMode.Code, "Board code")
        };

        [ObservableProperty]
        private ChoiceOption<LabelMode> _selectedLabelMode;

        /// <summary>Contour rows mode: contour interval = board width × slope, so every row line is a contour.</summary>
        [ObservableProperty]
        private bool _contoursFollowRows = true;

        /// <summary>Set by the main view model: true when the interval comes from the board rows.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIntervalEditable))]
        private bool _intervalLocked;

        [ObservableProperty]
        private string _lockedIntervalText = string.Empty;

        public bool IsIntervalEditable => Contours.IsEnabled && !IntervalLocked;

        public IReadOnlyList<TextNoteTypeOption> TextTypes { get; }

        [ObservableProperty]
        private TextNoteTypeOption? _selectedTextType;

        public IReadOnlyList<ChoiceOption<GroupingMode>> GroupingModes { get; } = new[]
        {
            new ChoiceOption<GroupingMode>(GroupingMode.SingleGroup, "One detail group"),
            new ChoiceOption<GroupingMode>(GroupingMode.GroupPerSide, "One group per side (grid + hatch + labels of each side)"),
            new ChoiceOption<GroupingMode>(GroupingMode.GroupPerLayer, "One group per layer (grid split per edge)"),
            new ChoiceOption<GroupingMode>(GroupingMode.NoGroup, "No group (loose lines — align / rotate each)")
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReplace))]
        private ChoiceOption<GroupingMode> _selectedGrouping;

        [ObservableProperty]
        private bool _replacePrevious = true;

        /// <summary>Replace needs a group to find the previous pattern, so it is only available when grouping.</summary>
        public bool CanReplace => SelectedGrouping.Value != GroupingMode.NoGroup;

        public LabelMode EffectiveLabelMode => LabelsEnabled ? SelectedLabelMode.Value : LabelMode.None;

        public bool AnyLayerEnabled => Grid.IsEnabled || HipRidge.IsEnabled || Contours.IsEnabled || LabelsEnabled;

        public bool HasErrors =>
            (Contours.IsEnabled && !IntervalLocked && ContourInterval.HasError) ||
            (LabelsEnabled && SelectedTextType == null);

        public event EventHandler? Changed;

        public Pattern2DSettings ApplyTo(Pattern2DSettings settings) => settings with
        {
            DrawGrid = Grid.IsEnabled,
            DrawHipRidge = HipRidge.IsEnabled,
            DrawContours = Contours.IsEnabled,
            ContourIntervalMm = ContourInterval.Value ?? ContourInterval.Default,
            ContoursFollowRows = ContoursFollowRows,
            Labels = EffectiveLabelMode
        };

        public IReadOnlyDictionary<PatternLayer, ElementId> StyleIds() =>
            LineLayers.Where(l => l.SelectedStyle != null)
                      .ToDictionary(l => l.Layer, l => l.SelectedStyle!.GraphicsStyleId);

        public void Reset()
        {
            foreach (LayerOptionViewModel layer in LineLayers)
            {
                layer.Reset();
            }

            ContourInterval.Reset();
            LabelsEnabled = false;
            ContoursFollowRows = true;
            SelectedLabelMode = LabelModes[0];
            SelectedTextType = TextTypes.FirstOrDefault();
            SelectedGrouping = GroupingModes[0];
            ReplacePrevious = true;
        }

        partial void OnLabelsEnabledChanged(bool value) => RaiseChanged();

        partial void OnContoursFollowRowsChanged(bool value) => RaiseChanged();

        partial void OnIntervalLockedChanged(bool value) => OnPropertyChanged(nameof(HasErrors));

        partial void OnSelectedLabelModeChanged(ChoiceOption<LabelMode> value) => RaiseChanged();

        partial void OnSelectedTextTypeChanged(TextNoteTypeOption? value) => RaiseChanged();

        partial void OnSelectedGroupingChanged(ChoiceOption<GroupingMode> value) => RaiseChanged();

        partial void OnReplacePreviousChanged(bool value) => RaiseChanged();

        private void RaiseChanged()
        {
            OnPropertyChanged(nameof(IsIntervalEditable));
            OnPropertyChanged(nameof(AnyLayerEnabled));
            OnPropertyChanged(nameof(HasErrors));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
