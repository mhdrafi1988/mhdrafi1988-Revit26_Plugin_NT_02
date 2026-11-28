using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.UI.ViewModels
{
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

        [ObservableProperty] private bool _createRoofs;
        [ObservableProperty] private bool _createPlans;
        [ObservableProperty] private bool _createSections;
        [ObservableProperty] private string _selectedPlanType;
        [ObservableProperty] private string _selectedPlanTemplate;
        [ObservableProperty] private string _cropMarginText;
        [ObservableProperty] private string _planScaleText;
        [ObservableProperty] private string _planNamePattern;
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

        /// <summary>Loads the Part B fields from the settings. Called once, from the constructor.</summary>
        private void InitViews()
        {
            _createRoofs = _settings.CreateRoofs;
            _createPlans = _settings.CreatePlans;
            _createSections = _settings.CreateSections;
            _cropMarginText = Text(_settings.CropMarginMm);
            _planScaleText = _settings.PlanScale.ToString(CultureInfo.CurrentCulture);
            _planNamePattern = _settings.PlanNamePattern;
            _calloutScaleText = _settings.CalloutScale.ToString(CultureInfo.CurrentCulture);
            _calloutMarginText = Text(_settings.CalloutMarginMm);
            _sectionScaleText = _settings.SectionScale.ToString(CultureInfo.CurrentCulture);
            _sectionDepthText = Text(_settings.SectionDepthMm);
            _sectionBottomText = Text(_settings.SectionBottomMm);
            _sectionTopText = Text(_settings.SectionTopMm);
            InitMapping();
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

            ApplyMappingSources(snapshot);
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

            if (!PushMapping()) return false;

            _settings.CreateRoofs = CreateRoofs;
            _settings.CreatePlans = CreatePlans;
            _settings.CreateSections = CreateSections;
            _settings.PlanViewTypeName = SelectedPlanType ?? string.Empty;
            _settings.PlanTemplateName = SelectedPlanTemplate == NoTemplate ? string.Empty : SelectedPlanTemplate ?? string.Empty;
            _settings.CropMarginMm = margin;
            _settings.PlanScale = (int)Math.Round(scale);
            _settings.PlanNamePattern = PlanNamePattern;
            _settings.CalloutViewTypeName = SelectedCalloutViewType ?? string.Empty;
            _settings.CalloutScale = (int)Math.Round(calloutScale);
            _settings.CalloutMarginMm = calloutMargin;
            _settings.SectionViewTypeName = SelectedSectionViewType ?? string.Empty;
            _settings.SectionScale = (int)Math.Round(sectionScale);
            _settings.SectionDepthMm = sectionDepth;
            _settings.SectionBottomMm = sectionBottom;
            _settings.SectionTopMm = sectionTop;
            return true;
        }
    }
}
