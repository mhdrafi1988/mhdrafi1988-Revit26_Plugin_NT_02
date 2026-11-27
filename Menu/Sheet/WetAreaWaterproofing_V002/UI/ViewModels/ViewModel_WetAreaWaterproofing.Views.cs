using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.ViewModels
{
    /// <summary>One row of the "Section targets" table.</summary>
    public sealed partial class TargetRowViewModel : ObservableObject
    {
        private readonly Action _changed;

        /// <summary>Wraps a saved target row; <paramref name="changed"/> is called on every edit.</summary>
        public TargetRowViewModel(SectionTargetSetting setting, Action changed)
        {
            Kind = setting.Kind;
            _on = setting.On;
            _perItem = setting.AllMatches ? PerItemChoices[1] : PerItemChoices[0];
            _lengthText = setting.LengthMm.ToString("0.#", CultureInfo.CurrentCulture);
            _draftingViewName = setting.DraftingViewName;
            _selectedMode = SectionModes.Label(setting.Mode);
            _changed = changed;
        }

        /// <summary>Choices for "Per item".</summary>
        public static IReadOnlyList<string> PerItemChoices { get; } = new[] { "One (nearest centre)", "All matches" };
        /// <summary>Choices for "Mode".</summary>
        public static IReadOnlyList<string> ModeChoices => SectionModes.Labels;

        /// <summary>Choices for this row's "Per item" box.</summary>
        public IReadOnlyList<string> Choices => PerItemChoices;
        /// <summary>Choices for this row's "Mode" box.</summary>
        public IReadOnlyList<string> Modes => ModeChoices;

        /// <summary>What is cut.</summary>
        public SectionTargetKind Kind { get; }
        /// <summary>Text shown for the target.</summary>
        public string Label => SectionTargets.Label(Kind);
        /// <summary>True when there can be several matches (doors, drains, fixtures, corners).</summary>
        public bool CanHaveMany => Kind is not (SectionTargetKind.FullRoomH or SectionTargetKind.FullRoomV or SectionTargetKind.WallFloor);
        /// <summary>"Past room" for the full-room cuts, "Length" for the others.</summary>
        public string LengthLabel => Kind is SectionTargetKind.FullRoomH or SectionTargetKind.FullRoomV ? "Past room" : "Length";
        /// <summary>The row's mode.</summary>
        public SectionMode Mode => SectionModes.FromLabel(SelectedMode);
        /// <summary>Whether this row is in callout mode.</summary>
        public bool IsCallout => Mode == SectionMode.Callout;
        /// <summary>Whether this row points to a drafting view (Ref. Section or Ref. Callout).</summary>
        public bool UsesDraftingView => SectionModes.UsesDraftingView(Mode);
        /// <summary>Why "Points to" is greyed out for this row, or empty.</summary>
        public string NoDraftingNote => Mode switch
        {
            SectionMode.Callout => "Callout: makes its own detail view",
            SectionMode.Section => "Section: makes its own section view",
            _ => string.Empty
        };

        [ObservableProperty] private bool _on;
        [ObservableProperty] private string _perItem;
        [ObservableProperty] private string _lengthText;
        [ObservableProperty] private string _draftingViewName;
        [ObservableProperty] private string _selectedMode;

        partial void OnOnChanged(bool value) => _changed?.Invoke();
        partial void OnPerItemChanged(string value) => _changed?.Invoke();
        partial void OnLengthTextChanged(string value) => _changed?.Invoke();
        partial void OnDraftingViewNameChanged(string value) => _changed?.Invoke();
        partial void OnSelectedModeChanged(string value)
        {
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsCallout));
            OnPropertyChanged(nameof(UsesDraftingView));
            OnPropertyChanged(nameof(NoDraftingNote));
            _changed?.Invoke();
        }

        /// <summary>The row as a setting; null (with <paramref name="error"/>) when the length is not a valid number.</summary>
        public SectionTargetSetting ToSetting(out string error)
        {
            error = null;
            bool ok = double.TryParse(LengthText, NumberStyles.Float, CultureInfo.CurrentCulture, out double length)
                   || double.TryParse(LengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out length);
            if (!ok || length <= 0 || length > 20000)
            {
                error = $"\"{Label}\": the length must be a number from 1 to 20000 mm.";
                return null;
            }
            return new SectionTargetSetting
            {
                Kind = Kind, On = On, AllMatches = CanHaveMany && PerItem == PerItemChoices[1],
                LengthMm = length, DraftingViewName = (DraftingViewName ?? string.Empty).Trim(),
                Mode = Mode
            };
        }
    }

    /// <summary>A category under a model in the "Search targets in" list.</summary>
    public sealed partial class SourceCategoryViewModel : ObservableObject
    {
        private readonly Action _changed;

        /// <summary>Wraps <paramref name="source"/>.</summary>
        public SourceCategoryViewModel(TargetSource source, Action changed)
        {
            Key = source.Key;
            Category = source.Category;
            Count = source.Count;
            _isOn = source.On;
            _changed = changed;
        }

        /// <summary>"Model|Category".</summary>
        public string Key { get; }
        /// <summary>Category name.</summary>
        public string Category { get; }
        /// <summary>Doors, drains and fixtures found in it.</summary>
        public int Count { get; }
        /// <summary>Searched.</summary>
        [ObservableProperty] private bool _isOn;

        partial void OnIsOnChanged(bool value) => _changed?.Invoke();
    }

    /// <summary>A model (host or link) in the "Search targets in" list.</summary>
    public sealed class SourceModelViewModel
    {
        /// <summary>Groups the categories of <paramref name="model"/>.</summary>
        public SourceModelViewModel(string model, IEnumerable<SourceCategoryViewModel> categories)
        {
            Model = model == RoomCollector.HostKey ? "Host model" : model;
            Categories = categories.ToList();
        }

        /// <summary>"Host model" or the link's name.</summary>
        public string Model { get; }
        /// <summary>Its categories.</summary>
        public IReadOnlyList<SourceCategoryViewModel> Categories { get; }
        /// <summary>"Host model · 48 found".</summary>
        public string Header => $"{Model} · {Categories.Sum(c => c.Count)} found";
    }

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Text shown for "no view template".</summary>
        public const string NoTemplate = "(none)";

        /// <summary>Floor plan types in the project.</summary>
        public ObservableCollection<string> PlanTypes { get; } = new();
        /// <summary>Floor plan view templates, with <see cref="NoTemplate"/> first.</summary>
        public ObservableCollection<string> PlanTemplates { get; } = new();
        /// <summary>Drafting view names in the project.</summary>
        public ObservableCollection<string> DraftingViews { get; } = new();
        /// <summary>Drafting views with sheet, bubble and title, for the "Points to" search and the picker panel.</summary>
        public ObservableCollection<DraftingViewInfo> DraftingViewItems { get; } = new();
        /// <summary>Section view family types in the project (for "Section" targets).</summary>
        public ObservableCollection<string> SectionViewTypes { get; } = new();
        /// <summary>Detail view family types in the project (for callout sections).</summary>
        public ObservableCollection<string> DetailViewTypes { get; } = new();
        /// <summary>The "Section targets" table, in order.</summary>
        public ObservableCollection<TargetRowViewModel> Targets { get; } = new();
        /// <summary>The "Search targets in" list.</summary>
        public ObservableCollection<SourceModelViewModel> Sources { get; } = new();
        /// <summary>Choices for "Per item", for the table's combo boxes.</summary>
        public IReadOnlyList<string> PerItemChoices => TargetRowViewModel.PerItemChoices;

        [ObservableProperty] private bool _createRoofs;
        [ObservableProperty] private bool _createPlans;
        [ObservableProperty] private bool _createSections;
        [ObservableProperty] private string _selectedPlanType;
        [ObservableProperty] private string _selectedPlanTemplate;
        [ObservableProperty] private string _cropMarginText;
        [ObservableProperty] private string _planScaleText;
        [ObservableProperty] private string _planNamePattern;
        [ObservableProperty] private bool _oneDraftingViewForAll;
        [ObservableProperty] private bool _draftingViewPerTarget;
        [ObservableProperty] private string _sharedDraftingViewName;
        [ObservableProperty] private string _drainKeywordsText;
        [ObservableProperty] private string _wcKeywordsText;
        [ObservableProperty] private string _showerKeywordsText;
        [ObservableProperty] private string _sectionsSummary = string.Empty;
        [ObservableProperty] private string _selectedCalloutViewType;
        [ObservableProperty] private string _calloutScaleText;
        [ObservableProperty] private string _calloutMarginText;
        [ObservableProperty] private string _selectedSectionViewType;
        [ObservableProperty] private string _sectionScaleText;
        [ObservableProperty] private string _sectionDepthText;
        [ObservableProperty] private string _sectionBottomText;
        [ObservableProperty] private string _sectionTopText;

        partial void OnCreateRoofsChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
        partial void OnCreatePlansChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
        partial void OnCreateSectionsChanged(bool value) => RunCommand.NotifyCanExecuteChanged();
        partial void OnOneDraftingViewForAllChanged(bool value) => DraftingViewPerTarget = !value;
        partial void OnDraftingViewPerTargetChanged(bool value) => OneDraftingViewForAll = !value;
        partial void OnDrainKeywordsTextChanged(string value) => MarkNeedsRefresh();
        partial void OnWcKeywordsTextChanged(string value) => MarkNeedsRefresh();
        partial void OnShowerKeywordsTextChanged(string value) => MarkNeedsRefresh();

        /// <summary>Loads the Part B fields from the settings. Called once, from the constructor.</summary>
        private void InitViews()
        {
            _createRoofs = _settings.CreateRoofs;
            _createPlans = _settings.CreatePlans;
            _createSections = _settings.CreateSections;
            _cropMarginText = Text(_settings.CropMarginMm);
            _planScaleText = _settings.PlanScale.ToString(CultureInfo.CurrentCulture);
            _planNamePattern = _settings.PlanNamePattern;
            _oneDraftingViewForAll = _settings.OneDraftingViewForAll;
            _draftingViewPerTarget = !_settings.OneDraftingViewForAll;
            _sharedDraftingViewName = _settings.SharedDraftingViewName;
            _calloutScaleText = _settings.CalloutScale.ToString(CultureInfo.CurrentCulture);
            _calloutMarginText = Text(_settings.CalloutMarginMm);
            _sectionScaleText = _settings.SectionScale.ToString(CultureInfo.CurrentCulture);
            _sectionDepthText = Text(_settings.SectionDepthMm);
            _sectionBottomText = Text(_settings.SectionBottomMm);
            _sectionTopText = Text(_settings.SectionTopMm);
            _drainKeywordsText = string.Join(", ", _settings.DrainKeywords);
            _wcKeywordsText = string.Join(", ", _settings.WcKeywords);
            _showerKeywordsText = string.Join(", ", _settings.ShowerKeywords);
            foreach (SectionTargetSetting target in _settings.Targets)
                Targets.Add(new TargetRowViewModel(target, OnTargetsChanged));
            UpdateSectionsSummary();
        }

        /// <summary>Fills the plan types, templates, drafting views and the sources list from a model read.</summary>
        private void ApplyViewLists(ModelSnapshot snapshot)
        {
            Replace(PlanTypes, snapshot.PlanTypes);
            string wantedType = SelectedPlanType ?? _settings.PlanViewTypeName;
            SelectedPlanType = PlanTypes.Contains(wantedType) ? wantedType : PlanTypes.FirstOrDefault();

            Replace(PlanTemplates, new[] { NoTemplate }.Concat(snapshot.PlanTemplates));
            string wantedTemplate = SelectedPlanTemplate ?? _settings.PlanTemplateName;
            SelectedPlanTemplate = PlanTemplates.Contains(wantedTemplate) ? wantedTemplate : NoTemplate;

            // Editable boxes show this list: it is updated in place (never cleared), so a box whose text
            // matches an entry does not go blank.
            var names = snapshot.DraftingViews.Select(v => v.Name).ToList();
            var wanted = new HashSet<string>(names);
            for (int i = DraftingViews.Count - 1; i >= 0; i--)
                if (!wanted.Contains(DraftingViews[i])) DraftingViews.RemoveAt(i);
            foreach (string name in names)
                if (!DraftingViews.Contains(name)) DraftingViews.Add(name);
            Replace(DraftingViewItems, snapshot.DraftingViews);
            UpdatePicker();

            Replace(SectionViewTypes, snapshot.SectionViewTypes);
            string wantedSectionType = SelectedSectionViewType ?? _settings.SectionViewTypeName;
            SelectedSectionViewType = SectionViewTypes.Contains(wantedSectionType) ? wantedSectionType : SectionViewTypes.FirstOrDefault();

            Replace(DetailViewTypes, snapshot.DetailViewTypes);
            string wantedCalloutType = SelectedCalloutViewType ?? _settings.CalloutViewTypeName;
            SelectedCalloutViewType = DetailViewTypes.Contains(wantedCalloutType) ? wantedCalloutType : DetailViewTypes.FirstOrDefault();

            Sources.Clear();
            foreach (var model in snapshot.Sources.GroupBy(x => x.Model))
                Sources.Add(new SourceModelViewModel(model.Key, model.Select(x => new SourceCategoryViewModel(x, MarkNeedsRefresh))));
        }

        private void OnTargetsChanged()
        {
            UpdateSectionsSummary();
            MarkNeedsRefresh();
        }

        private void UpdateSectionsSummary()
        {
            int on = Targets.Count(t => t.On);
            SectionsSummary = on == 0 ? "No target is on: no section will be placed."
                : $"{on} target(s) on · at most {SectionTargets.MaxPerRoom} sections per room · rows higher up win";
        }

        /// <summary>Moves a target one row up (higher rows win when a room would get more than six).</summary>
        [RelayCommand]
        private void TargetUp(TargetRowViewModel row) => MoveTarget(row, -1);

        /// <summary>Moves a target one row down.</summary>
        [RelayCommand]
        private void TargetDown(TargetRowViewModel row) => MoveTarget(row, +1);

        private void MoveTarget(TargetRowViewModel row, int step)
        {
            int index = row == null ? -1 : Targets.IndexOf(row);
            int target = index + step;
            if (index < 0 || target < 0 || target >= Targets.Count) return;
            Targets.Move(index, target);
            OnTargetsChanged();
        }

        /// <summary>Copies the Part B fields into the settings; false (with a log line) when a value is not valid.</summary>
        private bool PushViewSettings()
        {
            if (!Number(CropMarginText, "Crop margin", 0, 10000, out double margin)
                || !Number(PlanScaleText, "Plan scale", 1, 5000, out double scale)
                || !Number(CalloutScaleText, "Callout scale", 1, 5000, out double calloutScale)
                || !Number(CalloutMarginText, "Callout margin", 0, 10000, out double calloutMargin)
                || !Number(SectionScaleText, "Section scale", 1, 5000, out double sectionScale)
                || !Number(SectionDepthText, "Section depth", 1, 20000, out double sectionDepth)
                || !Number(SectionBottomText, "Section below level", 0, 10000, out double sectionBottom)
                || !Number(SectionTopText, "Section above level", 1, 20000, out double sectionTop)) return false;

            var targets = new List<SectionTargetSetting>();
            foreach (TargetRowViewModel row in Targets)
            {
                SectionTargetSetting setting = row.ToSetting(out string error);
                if (setting == null)
                {
                    AddLog(LogLevel.Error, error);
                    return false;
                }
                targets.Add(setting);
            }

            _settings.CreateRoofs = CreateRoofs;
            _settings.CreatePlans = CreatePlans;
            _settings.CreateSections = CreateSections;
            _settings.PlanViewTypeName = SelectedPlanType ?? string.Empty;
            _settings.PlanTemplateName = SelectedPlanTemplate == NoTemplate ? string.Empty : SelectedPlanTemplate ?? string.Empty;
            _settings.CropMarginMm = margin;
            _settings.PlanScale = (int)Math.Round(scale);
            _settings.PlanNamePattern = PlanNamePattern;
            _settings.Targets = targets;
            _settings.OneDraftingViewForAll = OneDraftingViewForAll;
            _settings.SharedDraftingViewName = SharedDraftingViewName;
            _settings.CalloutViewTypeName = SelectedCalloutViewType ?? string.Empty;
            _settings.CalloutScale = (int)Math.Round(calloutScale);
            _settings.CalloutMarginMm = calloutMargin;
            _settings.SectionViewTypeName = SelectedSectionViewType ?? string.Empty;
            _settings.SectionScale = (int)Math.Round(sectionScale);
            _settings.SectionDepthMm = sectionDepth;
            _settings.SectionBottomMm = sectionBottom;
            _settings.SectionTopMm = sectionTop;
            _settings.DrainKeywords = Split(DrainKeywordsText);
            _settings.WcKeywords = Split(WcKeywordsText);
            _settings.ShowerKeywords = Split(ShowerKeywordsText);
            _settings.SourcesOff = Sources.SelectMany(m => m.Categories).Where(c => !c.IsOn).Select(c => c.Key).ToList();
            return true;
        }

        private static List<string> Split(string text)
            => (text ?? string.Empty).Split(new[] { ',', ';' }).ToList();
    }
}
