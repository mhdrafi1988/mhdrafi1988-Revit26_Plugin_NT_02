using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// One of the four target-parameter slots in the window: which parameter it writes to, what kind that
    /// parameter is, and the New Parameter… button that defines a missing one. The data type is fixed by the
    /// slot, so only parameters of the right type are offered.
    /// </summary>
    public sealed partial class SlotViewModel : ObservableObject
    {
        private readonly Action<SlotViewModel> _nameChanged;
        private readonly Action<SlotViewModel> _newParameter;

        /// <summary>Creates the slot.</summary>
        public SlotViewModel(TargetSlot slot, Action<SlotViewModel> nameChanged, Action<SlotViewModel> newParameter)
        {
            Slot = slot;
            _nameChanged = nameChanged;
            _newParameter = newParameter;
        }

        /// <summary>Which of the four slots this is.</summary>
        public TargetSlot Slot { get; }

        /// <summary>1 to 4, as the window numbers them.</summary>
        public int Number => TargetSlots.Number(Slot);

        /// <summary>"Base offset" and so on.</summary>
        public string Label => TargetSlots.Label(Slot);

        /// <summary>"Text" or "Length".</summary>
        public string TypeName => TargetSlots.TypeName(Slot);

        /// <summary>The default name offered when the slot is empty.</summary>
        public string SuggestedName => Slot switch
        {
            TargetSlot.BaseLevelName => "Base Level Name",
            TargetSlot.BaseOffset => "Base Level Offset",
            TargetSlot.NearestLevelName => "Nearest Level Name",
            TargetSlot.NearestOffset => "Nearest Level Offset",
            _ => Label
        };

        /// <summary>Only the project parameters that can serve this slot.</summary>
        public ObservableCollection<ProjectParameterOption> Options { get; } = new();

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _kind = string.Empty;
        [ObservableProperty] private NewParameterSpec _pending;
        [ObservableProperty] private CheckResult _state = CheckResult.Blocked;

        partial void OnNameChanged(string value) => _nameChanged?.Invoke(this);

        /// <summary>Refills <see cref="Options"/> from every project parameter.</summary>
        public void SetOptions(IEnumerable<ProjectParameterOption> all)
        {
            Options.Clear();
            foreach (ProjectParameterOption option in (all ?? Enumerable.Empty<ProjectParameterOption>()).Where(o => o.Fits(Slot)))
                Options.Add(option);
        }

        /// <summary>Updates <see cref="Kind"/> and <see cref="State"/> for the current name.</summary>
        public void Describe(IReadOnlyList<ProjectParameterOption> all)
        {
            string name = (Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                Kind = "Not chosen";
                State = CheckResult.Blocked;
                return;
            }

            ProjectParameterOption match = all?.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                Kind = match.Kind;
                State = match.Fits(Slot) ? CheckResult.Pass : CheckResult.Blocked;
                if (!match.Fits(Slot))
                    Kind += $" — this slot needs an instance {TypeName} parameter";
                return;
            }

            if (Pending != null && string.Equals(Pending.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                Kind = $"New · Shared · Instance · {TypeName} (created when you write)";
                State = CheckResult.Warning;
                return;
            }

            Kind = "Not in the project · use New Parameter…";
            State = CheckResult.Blocked;
        }

        [RelayCommand]
        private void NewParameter() => _newParameter?.Invoke(this);
    }
}
