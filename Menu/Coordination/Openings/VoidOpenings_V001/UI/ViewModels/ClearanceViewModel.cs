using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>A choice in a drop-down: the stored key and the text shown.</summary>
    public sealed class Option
    {
        /// <summary>Creates an option.</summary>
        public Option(string key, string label)
        {
            Key = key;
            Label = label;
        }

        /// <summary>Value stored in the settings.</summary>
        public string Key { get; }

        /// <summary>Text shown to the user.</summary>
        public string Label { get; }
    }

    /// <summary>One editable clearance rule.</summary>
    public sealed class ClearanceRuleRowViewModel : ObservableObject
    {
        private string _category = nameof(MepCategory.Pipe);
        private double _fromMm;
        private double _toMm = 100;
        private double _clearanceMm = 50;
        private string _mode = nameof(ClearanceMode.PerSide);
        private string _shape = nameof(OpeningShapePolicy.Auto);

        /// <summary>MEP category key.</summary>
        public string Category { get => _category; set => SetProperty(ref _category, value); }

        /// <summary>Smallest size covered, millimetres.</summary>
        public double FromMm { get => _fromMm; set => SetProperty(ref _fromMm, value); }

        /// <summary>Largest size covered, millimetres.</summary>
        public double ToMm { get => _toMm; set => SetProperty(ref _toMm, value); }

        /// <summary>Offset, millimetres.</summary>
        public double ClearanceMm { get => _clearanceMm; set => SetProperty(ref _clearanceMm, value); }

        /// <summary>PerSide or Total.</summary>
        public string Mode { get => _mode; set => SetProperty(ref _mode, value); }

        /// <summary>Auto, Circular or Rectangular.</summary>
        public string Shape { get => _shape; set => SetProperty(ref _shape, value); }
    }

    /// <summary>The OPENING CLEARANCE section: the rule list and the run options, all in millimetres.</summary>
    public sealed class ClearanceViewModel : ObservableObject
    {
        private ClearanceRuleRowViewModel _selectedRule;
        private double _defaultClearanceMm = 50;
        private string _defaultMode = nameof(ClearanceMode.PerSide);
        private double _overshootMm = 50;
        private double _roundUpToMm = 10;
        private bool _includeInsulation = true;
        private string _axisMode = nameof(OpeningAxisMode.PerpendicularToStructure);
        private double _maxCircularAngleDeg = 15;
        private string _customRoundFamily = string.Empty;
        private string _customRectFamily = string.Empty;

        /// <summary>Creates the section with an empty rule list.</summary>
        public ClearanceViewModel()
        {
            AddRuleCommand = new RelayCommand(AddRule);
            DeleteRuleCommand = new RelayCommand(DeleteRule, () => SelectedRule != null);
        }

        /// <summary>MEP categories a rule can be for.</summary>
        public IReadOnlyList<Option> CategoryOptions { get; } = CategoryCatalog.Mep
            .Select(c => new Option(c.ToString(), CategoryCatalog.NameOf(c))).ToList();

        /// <summary>How an offset is applied.</summary>
        public IReadOnlyList<Option> ModeOptions { get; } = new[]
        {
            new Option(nameof(ClearanceMode.PerSide), "Each side"),
            new Option(nameof(ClearanceMode.Total), "Total"),
        };

        /// <summary>Opening shape a rule asks for.</summary>
        public IReadOnlyList<Option> ShapeOptions { get; } = new[]
        {
            new Option(nameof(OpeningShapePolicy.Auto), "Same as MEP"),
            new Option(nameof(OpeningShapePolicy.Circular), "Circular"),
            new Option(nameof(OpeningShapePolicy.Rectangular), "Rectangular"),
        };

        /// <summary>Which way openings point.</summary>
        public IReadOnlyList<Option> AxisOptions { get; } = new[]
        {
            new Option(nameof(OpeningAxisMode.PerpendicularToStructure), "Perpendicular to structure"),
            new Option(nameof(OpeningAxisMode.FollowMep), "Follow MEP"),
        };

        /// <summary>The rules.</summary>
        public ObservableCollection<ClearanceRuleRowViewModel> Rules { get; } = new ObservableCollection<ClearanceRuleRowViewModel>();

        /// <summary>The rule selected in the grid.</summary>
        public ClearanceRuleRowViewModel SelectedRule
        {
            get => _selectedRule;
            set
            {
                ClearanceRuleRowViewModel old = _selectedRule;
                if (!SetProperty(ref _selectedRule, value))
                    return;
                if (old != null) old.PropertyChanged -= OnSelectedRuleChanged;
                if (value != null) value.PropertyChanged += OnSelectedRuleChanged;
                DeleteRuleCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedRuleExample));
            }
        }

        /// <summary>Worked example of the selected rule, shown under the grid.</summary>
        public string SelectedRuleExample => SelectedRule == null
            ? "Select a rule to see how it sizes an opening."
            : "Selected rule: " + Example(SelectedRule) + " opening.";

        private void OnSelectedRuleChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
            => OnPropertyChanged(nameof(SelectedRuleExample));

        /// <summary>Adds a rule after the selected one.</summary>
        public IRelayCommand AddRuleCommand { get; }

        /// <summary>Deletes the selected rule.</summary>
        public IRelayCommand DeleteRuleCommand { get; }

        /// <summary>Offset when no rule fits, millimetres.</summary>
        public double DefaultClearanceMm { get => _defaultClearanceMm; set => SetProperty(ref _defaultClearanceMm, value); }

        /// <summary>How the default offset is applied.</summary>
        public string DefaultMode { get => _defaultMode; set => SetProperty(ref _defaultMode, value); }

        /// <summary>Overshoot beyond the structure on each side, millimetres.</summary>
        public double OvershootMm { get => _overshootMm; set => SetProperty(ref _overshootMm, value); }

        /// <summary>Rounding module, millimetres; 0 for none.</summary>
        public double RoundUpToMm { get => _roundUpToMm; set => SetProperty(ref _roundUpToMm, value); }

        /// <summary>True to add insulation to the MEP size.</summary>
        public bool IncludeInsulation { get => _includeInsulation; set => SetProperty(ref _includeInsulation, value); }

        /// <summary>Opening axis mode key.</summary>
        public string AxisMode { get => _axisMode; set => SetProperty(ref _axisMode, value); }

        /// <summary>Largest angle at which a round element keeps a circular opening, degrees.</summary>
        public double MaxCircularAngleDeg { get => _maxCircularAngleDeg; set => SetProperty(ref _maxCircularAngleDeg, value); }

        /// <summary>Name of a loaded family for circular voids; empty for the built-in one.</summary>
        public string CustomRoundFamily { get => _customRoundFamily; set => SetProperty(ref _customRoundFamily, value ?? string.Empty); }

        /// <summary>Name of a loaded family for rectangular voids; empty for the built-in one.</summary>
        public string CustomRectFamily { get => _customRectFamily; set => SetProperty(ref _customRectFamily, value ?? string.Empty); }

        /// <summary>Worked example for the selected rule, such as "40 + 2 × 30 = 100 mm".</summary>
        public string Example(ClearanceRuleRowViewModel rule)
        {
            if (rule == null) return string.Empty;
            double size = Math.Max(rule.FromMm, Math.Min(rule.ToMm, (rule.FromMm + rule.ToMm) / 2));
            bool perSide = rule.Mode == nameof(ClearanceMode.PerSide);
            double opening = size + (perSide ? 2 : 1) * rule.ClearanceMm;
            string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            return perSide ? $"{F(size)} + 2 × {F(rule.ClearanceMm)} = {F(opening)} mm" : $"{F(size)} + {F(rule.ClearanceMm)} = {F(opening)} mm";
        }

        /// <summary>Shows saved settings.</summary>
        public void Load(VoidOpeningsSettings settings)
        {
            Rules.Clear();
            foreach (ClearanceRuleDto rule in settings.Rules)
                Rules.Add(new ClearanceRuleRowViewModel
                {
                    Category = rule.Category, FromMm = rule.FromMm, ToMm = rule.ToMm,
                    ClearanceMm = rule.ClearanceMm, Mode = rule.Mode, Shape = rule.Shape,
                });
            DefaultClearanceMm = settings.DefaultClearanceMm;
            DefaultMode = settings.DefaultMode;
            OvershootMm = settings.OvershootMm;
            RoundUpToMm = settings.RoundUpToMm;
            IncludeInsulation = settings.IncludeInsulation;
            AxisMode = settings.AxisMode;
            MaxCircularAngleDeg = settings.MaxCircularAngleDeg;
            CustomRoundFamily = settings.CustomRoundFamily;
            CustomRectFamily = settings.CustomRectFamily;
        }

        /// <summary>The section's content as settings.</summary>
        public VoidOpeningsSettings ToSettings() => new VoidOpeningsSettings
        {
            Rules = Rules.Select(r => new ClearanceRuleDto
            {
                Category = r.Category, FromMm = r.FromMm, ToMm = r.ToMm, ClearanceMm = r.ClearanceMm, Mode = r.Mode, Shape = r.Shape,
            }).ToList(),
            DefaultClearanceMm = DefaultClearanceMm,
            DefaultMode = DefaultMode,
            OvershootMm = OvershootMm,
            RoundUpToMm = RoundUpToMm,
            IncludeInsulation = IncludeInsulation,
            AxisMode = AxisMode,
            MaxCircularAngleDeg = MaxCircularAngleDeg,
            CustomRoundFamily = CustomRoundFamily.Trim(),
            CustomRectFamily = CustomRectFamily.Trim(),
        };

        private void AddRule()
        {
            ClearanceRuleRowViewModel basis = SelectedRule ?? Rules.LastOrDefault();
            var rule = new ClearanceRuleRowViewModel();
            if (basis != null)
            {
                rule.Category = basis.Category;
                rule.FromMm = basis.ToMm + 1;
                rule.ToMm = basis.ToMm + Math.Max(1, basis.ToMm - basis.FromMm) + 1;
                rule.ClearanceMm = basis.ClearanceMm;
                rule.Mode = basis.Mode;
                rule.Shape = basis.Shape;
            }
            int index = SelectedRule == null ? Rules.Count : Rules.IndexOf(SelectedRule) + 1;
            Rules.Insert(index, rule);
            SelectedRule = rule;
        }

        private void DeleteRule()
        {
            if (SelectedRule != null)
                Rules.Remove(SelectedRule);
        }
    }
}
