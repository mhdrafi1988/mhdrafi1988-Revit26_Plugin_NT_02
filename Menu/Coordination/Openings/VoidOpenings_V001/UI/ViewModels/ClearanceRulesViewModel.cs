using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// The OPENING CLEARANCE card: the rule rows, the default offset and the sizing options. Every
    /// edit is checked at once; Save writes the settings file, Revert reads it again.
    /// </summary>
    public sealed class ClearanceRulesViewModel : ObservableObject
    {
        private readonly ClearanceConfigurationService _service;
        private readonly Action<string> _showError;
        private readonly Action<string> _showInfo;
        private string _defaultClearanceText = string.Empty;
        private ClearanceMode _defaultMode;
        private string _overshootText = string.Empty;
        private string _roundUpToText = string.Empty;
        private bool _includeInsulation = true;
        private OpeningAxisMode _axisMode;
        private double _maxCircularAngleDeg = 15;
        private bool _isDirty;
        private bool _hasErrors;
        private string _generalIssues = string.Empty;
        private string _summary = string.Empty;
        private ClearanceSetup _validSetup;
        private bool _applying;

        /// <summary>Creates the card.</summary>
        /// <param name="service">Loads, checks and saves the setup.</param>
        /// <param name="showError">Shows an error in the window's notification strip.</param>
        /// <param name="showInfo">Shows information in the window's notification strip.</param>
        public ClearanceRulesViewModel(ClearanceConfigurationService service, Action<string> showError, Action<string> showInfo)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _showError = showError;
            _showInfo = showInfo;
            AddRuleCommand = new RelayCommand(AddRule);
            SaveCommand = new RelayCommand(() => TrySave(), () => IsDirty && !HasErrors);
            RevertCommand = new RelayCommand(Revert, () => IsDirty);
            Apply(ClearanceSetup.CreateDefault());
        }

        /// <summary>The rule rows, top to bottom. The first row that fits an element is used.</summary>
        public ObservableCollection<ClearanceRuleRowViewModel> Rules { get; } = new ObservableCollection<ClearanceRuleRowViewModel>();

        /// <summary>Choices of the default offset's Applies list.</summary>
        public IReadOnlyList<ChoiceItem<ClearanceMode>> ModeOptions => ClearanceOptions.Modes;

        /// <summary>Choices of the Opening axis list.</summary>
        public IReadOnlyList<ChoiceItem<OpeningAxisMode>> AxisOptions => ClearanceOptions.Axes;

        /// <summary>Adds a rule row at the bottom.</summary>
        public IRelayCommand AddRuleCommand { get; }

        /// <summary>Saves the setup to the settings file.</summary>
        public IRelayCommand SaveCommand { get; }

        /// <summary>Drops the unsaved changes and shows the saved setup again.</summary>
        public IRelayCommand RevertCommand { get; }

        /// <summary>Default offset, as typed; used when no rule fits an element.</summary>
        public string DefaultClearanceText
        {
            get => _defaultClearanceText;
            set { if (SetProperty(ref _defaultClearanceText, value ?? string.Empty)) OnEdited(); }
        }

        /// <summary>How the default offset is applied.</summary>
        public ClearanceMode DefaultMode
        {
            get => _defaultMode;
            set { if (SetProperty(ref _defaultMode, value)) OnEdited(); }
        }

        /// <summary>How far a void extends beyond the structure on each side, as typed.</summary>
        public string OvershootText
        {
            get => _overshootText;
            set { if (SetProperty(ref _overshootText, value ?? string.Empty)) OnEdited(); }
        }

        /// <summary>Rounding module, as typed; 0 or empty turns rounding off.</summary>
        public string RoundUpToText
        {
            get => _roundUpToText;
            set { if (SetProperty(ref _roundUpToText, value ?? string.Empty)) OnEdited(); }
        }

        /// <summary>True to add insulation thickness to the MEP size.</summary>
        public bool IncludeInsulation
        {
            get => _includeInsulation;
            set { if (SetProperty(ref _includeInsulation, value)) OnEdited(); }
        }

        /// <summary>Which way openings point.</summary>
        public OpeningAxisMode AxisMode
        {
            get => _axisMode;
            set { if (SetProperty(ref _axisMode, value)) OnEdited(); }
        }

        /// <summary>True when the card differs from the settings file.</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (!SetProperty(ref _isDirty, value))
                    return;
                SaveCommand.NotifyCanExecuteChanged();
                RevertCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>True when something on the card must be fixed before it can be saved.</summary>
        public bool HasErrors
        {
            get => _hasErrors;
            private set
            {
                if (SetProperty(ref _hasErrors, value))
                    SaveCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>True when the card has no rule row.</summary>
        public bool HasNoRules => Rules.Count == 0;

        /// <summary>Problems with the default offset and the options; empty when there are none.</summary>
        public string GeneralIssues
        {
            get => _generalIssues;
            private set
            {
                if (SetProperty(ref _generalIssues, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasGeneralIssues));
            }
        }

        /// <summary>True when <see cref="GeneralIssues"/> has text.</summary>
        public bool HasGeneralIssues => _generalIssues.Length > 0;

        /// <summary>One-line state of the card, shown in its header.</summary>
        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        /// <summary>The setup as it stands on the card; null while the card has an error.</summary>
        public ClearanceSetup ValidSetup => _validSetup;

        /// <summary>
        /// Reads the settings file and shows it. Called once when the window has loaded. Returns false
        /// when the file could not be read cleanly; the notification strip then says why.
        /// </summary>
        public bool Load()
        {
            try
            {
                SettingsLoadResult result = _service.Load();
                Apply(result.Setup);
                if (result.Warning == null)
                    return true;
                _showError?.Invoke(result.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Apply(ClearanceSetup.CreateDefault());
                _showError?.Invoke($"Could not read the saved clearance rules: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves the card. Returns false, with a message in the notification strip, when the card has
        /// an error or the file cannot be written.
        /// </summary>
        public bool TrySave()
        {
            try
            {
                ClearanceValidation validation = _service.Save(BuildInput());
                ShowValidation(validation);
                if (validation.HasErrors)
                {
                    _showError?.Invoke($"The clearance rules were not saved: fix the {validation.ErrorCount} error(s) on the OPENING CLEARANCE card first.");
                    return false;
                }
                IsDirty = false;
                UpdateSummary(validation);
                _showInfo?.Invoke($"Clearance rules saved to {_service.Location}.");
                return true;
            }
            catch (Exception ex)
            {
                _showError?.Invoke($"The clearance rules could not be saved to {_service.Location}: {ex.Message}");
                return false;
            }
        }

        private void Revert()
        {
            if (Load())
                _showInfo?.Invoke("Unsaved changes to the clearance rules were dropped.");
        }

        private void AddRule()
        {
            ClearanceRuleRowViewModel last = Rules.LastOrDefault();
            var input = new ClearanceRuleInput
            {
                Category = last?.Category ?? MepCategory.Pipe,
                // Continue from where the row above stops, when it has an upper limit.
                FromText = last != null && MmText.TryParse(last.ToText, out _) ? last.ToText.Trim() : "0",
                ToText = string.Empty,
                ClearanceText = last?.ClearanceText ?? DefaultClearanceText,
                Mode = last?.Mode ?? DefaultMode,
                Shape = last?.Shape ?? OpeningShapePolicy.Auto,
            };
            Rules.Add(new ClearanceRuleRowViewModel(input, OnEdited, DeleteRule));
            OnEdited();
        }

        private void DeleteRule(ClearanceRuleRowViewModel row)
        {
            if (Rules.Remove(row))
                OnEdited();
        }

        // Shows a setup as the saved state: no unsaved changes afterwards.
        private void Apply(ClearanceSetup setup)
        {
            ClearanceInput input = ClearanceInput.From(setup);
            _applying = true;
            try
            {
                Rules.Clear();
                foreach (ClearanceRuleInput rule in input.Rules)
                    Rules.Add(new ClearanceRuleRowViewModel(rule, OnEdited, DeleteRule));
                DefaultClearanceText = input.DefaultClearanceText;
                DefaultMode = input.DefaultMode;
                OvershootText = input.OvershootText;
                RoundUpToText = input.RoundUpToText;
                IncludeInsulation = input.IncludeInsulation;
                AxisMode = input.AxisMode;
                _maxCircularAngleDeg = input.MaxCircularAngleDeg;
            }
            finally
            {
                _applying = false;
            }
            IsDirty = false;
            Revalidate();
        }

        private void OnEdited()
        {
            if (_applying)
                return;
            IsDirty = true;
            Revalidate();
        }

        private ClearanceInput BuildInput()
        {
            var input = new ClearanceInput
            {
                DefaultClearanceText = _defaultClearanceText,
                DefaultMode = _defaultMode,
                OvershootText = _overshootText,
                RoundUpToText = _roundUpToText,
                IncludeInsulation = _includeInsulation,
                AxisMode = _axisMode,
                MaxCircularAngleDeg = _maxCircularAngleDeg,
            };
            input.Rules.AddRange(Rules.Select(r => r.ToInput()));
            return input;
        }

        private void Revalidate() => ShowValidation(_service.Validate(BuildInput()));

        private void ShowValidation(ClearanceValidation validation)
        {
            // Each row's example uses the rounding typed on the card, when that is a valid number.
            double roundUp = MmText.TryParse(_roundUpToText, out double module) ? module : 0;

            for (int i = 0; i < Rules.Count; i++)
            {
                ClearanceRuleRowViewModel row = Rules[i];
                row.Number = i + 1;
                IReadOnlyList<ClearanceIssue> issues = validation.IssuesFor(i);
                bool hasError = issues.Any(issue => issue.Severity == IssueSeverity.Error);
                row.ShowCheck(
                    hasError ? string.Empty : ExampleOf(row.ToInput(), roundUp),
                    issues.Count == 0 ? "OK" : string.Join(" ", issues.Select(issue => issue.Message)),
                    hasError,
                    issues.Count > 0);
            }

            GeneralIssues = string.Join(" ", validation.IssuesFor(ClearanceIssue.General).Select(issue => issue.Message));
            HasErrors = validation.HasErrors;
            _validSetup = validation.Setup;
            OnPropertyChanged(nameof(ValidSetup));
            OnPropertyChanged(nameof(HasNoRules));
            UpdateSummary(validation);
        }

        private static string ExampleOf(ClearanceRuleInput row, double roundUp)
        {
            // The row has no error here, so these read; empty "from" is 0 and empty "to" is no limit.
            double from = MmText.TryParse(row.FromText, out double f) ? f : 0;
            double to = MmText.TryParse(row.ToText, out double t) ? t : ClearanceRule.NoUpperLimit;
            MmText.TryParse(row.ClearanceText, out double clearance);
            return ClearanceFormula.Example(new ClearanceRule(row.Category, from, to, clearance, row.Mode, row.Shape), roundUp);
        }

        private void UpdateSummary(ClearanceValidation validation)
        {
            var parts = new List<string> { Rules.Count == 1 ? "1 rule" : $"{Rules.Count} rules" };
            if (validation.ErrorCount > 0) parts.Add($"{validation.ErrorCount} error(s)");
            if (validation.WarningCount > 0) parts.Add($"{validation.WarningCount} warning(s)");
            parts.Add(IsDirty ? "unsaved changes" : "saved");
            Summary = string.Join(" · ", parts);
        }
    }
}
