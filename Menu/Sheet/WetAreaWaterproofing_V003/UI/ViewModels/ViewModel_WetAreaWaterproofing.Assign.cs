using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.UI.ViewModels
{
    /// <summary>
    /// One line on the left of the "Assign drafting views" panel: the shared view, or one target's view.
    /// Reads and writes the name straight on the target (or the shared setting), so the inline boxes and the
    /// panel always show the same thing.
    /// </summary>
    public sealed partial class AssignSlotViewModel : ObservableObject
    {
        private readonly Func<string> _get;
        private readonly Action<string> _set;
        private readonly Func<IEnumerable<DraftingViewInfo>> _views;

        /// <summary>Creates a slot titled <paramref name="title"/> that reads and writes the view name through the delegates.</summary>
        public AssignSlotViewModel(string title, string note, bool canAssign, Func<string> get, Action<string> set,
            Func<IEnumerable<DraftingViewInfo>> views)
        {
            Title = title;
            Note = note;
            CanAssign = canAssign;
            _get = get;
            _set = set;
            _views = views;
        }

        /// <summary>"All targets" or the target's label.</summary>
        public string Title { get; }
        /// <summary>Why a slot cannot be assigned ("Callout: makes its own detail view"), or empty.</summary>
        public string Note { get; }
        /// <summary>False for callout targets and targets that are off.</summary>
        public bool CanAssign { get; }

        /// <summary>The drafting view name this slot points to.</summary>
        public string ViewName
        {
            get => _get() ?? string.Empty;
            set
            {
                if (value == ViewName) return;
                _set(value ?? string.Empty);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Status));
            }
        }

        /// <summary>"A-501 / 3 · Wet area details", "Not on a sheet" or "New view: created at the run".</summary>
        public string Status => CanAssign ? DraftingViewSearch.Describe(_views(), ViewName) : Note;

        /// <summary>Re-reads the name and status (after a model refresh or an inline edit).</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(ViewName));
            OnPropertyChanged(nameof(Status));
        }
    }

    public sealed partial class ViewModel_WetAreaWaterproofing
    {
        /// <summary>Choices for the picker's scope chips, in order.</summary>
        public static IReadOnlyList<string> PickerScopes { get; } = new[] { "All", "On a sheet", "Not on a sheet" };

        /// <summary>Left list of the panel.</summary>
        public ObservableCollection<AssignSlotViewModel> AssignSlots { get; } = new();
        /// <summary>Right list of the panel: the drafting views matching the search and scope.</summary>
        public ObservableCollection<DraftingViewInfo> PickerResults { get; } = new();

        [ObservableProperty] private bool _assignVisible;
        [ObservableProperty] private AssignSlotViewModel _selectedSlot;
        [ObservableProperty] private string _pickerQuery = string.Empty;
        [ObservableProperty] private string _pickerScope = "All";
        [ObservableProperty] private DraftingViewInfo _selectedPickerView;
        [ObservableProperty] private string _pickerSummary = string.Empty;

        partial void OnPickerQueryChanged(string value) { UpdatePicker(); AssignNewNameCommand.NotifyCanExecuteChanged(); }
        partial void OnPickerScopeChanged(string value) => UpdatePicker();
        partial void OnSelectedPickerViewChanged(DraftingViewInfo value) => AssignPickedCommand.NotifyCanExecuteChanged();
        partial void OnSelectedSlotChanged(AssignSlotViewModel value)
        {
            AssignPickedCommand.NotifyCanExecuteChanged();
            AssignNewNameCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Opens the "Assign drafting views" panel with a slot per target (or one shared slot).</summary>
        [RelayCommand]
        private void OpenAssign()
        {
            BuildSlots();
            PickerQuery = string.Empty;
            UpdatePicker();
            AssignVisible = true;
        }

        /// <summary>Closes the panel. Assignments are already applied.</summary>
        [RelayCommand]
        private void CloseAssign()
        {
            AssignVisible = false;
            OnMappingChanged();
        }

        private bool CanAssignPicked() => SelectedSlot?.CanAssign == true && SelectedPickerView != null;

        /// <summary>Points the selected slot to the selected drafting view, then moves to the next slot.</summary>
        [RelayCommand(CanExecute = nameof(CanAssignPicked))]
        private void AssignPicked()
        {
            if (!CanAssignPicked()) return;
            SelectedSlot.ViewName = SelectedPickerView.Name;
            AdvanceSlot();
        }

        private bool CanAssignNewName() => SelectedSlot?.CanAssign == true && !string.IsNullOrWhiteSpace(PickerQuery);

        /// <summary>Points the selected slot to the typed text as a new view name (created at the run when it does not exist).</summary>
        [RelayCommand(CanExecute = nameof(CanAssignNewName))]
        private void AssignNewName()
        {
            if (!CanAssignNewName()) return;
            DraftingViewInfo byReference = DraftingViewSearch.ResolveReference(DraftingViewItems, PickerQuery);
            SelectedSlot.ViewName = byReference?.Name ?? PickerQuery.Trim();
            AdvanceSlot();
        }

        /// <summary>Sets the panel's scope chip.</summary>
        [RelayCommand]
        private void SetPickerScope(string scope) => PickerScope = PickerScopes.Contains(scope) ? scope : PickerScopes[0];

        private void AdvanceSlot()
        {
            int index = AssignSlots.IndexOf(SelectedSlot);
            AssignSlotViewModel next = AssignSlots.Skip(index + 1).FirstOrDefault(s => s.CanAssign);
            if (next != null) SelectedSlot = next;
        }

        private void BuildSlots()
        {
            AssignSlots.Clear();
            foreach (MappingRowViewModel row in MappingRows)
            {
                string note = !row.On ? "Off: no section is placed" : row.UsesDraftingView ? string.Empty : row.CreatesText;
                AssignSlots.Add(new AssignSlotViewModel(row.Name, note, row.On && row.UsesDraftingView,
                    () => row.DraftingViewName, v => row.DraftingViewName = v, () => DraftingViewItems));
            }
            SelectedSlot = AssignSlots.FirstOrDefault(s => s.CanAssign);
        }

        /// <summary>Re-runs the panel's search; also refreshes the slot statuses.</summary>
        private void UpdatePicker()
        {
            DraftingViewScope scope = PickerScope == PickerScopes[1] ? DraftingViewScope.OnSheet
                : PickerScope == PickerScopes[2] ? DraftingViewScope.NotOnSheet : DraftingViewScope.All;
            IReadOnlyList<DraftingViewInfo> hits = DraftingViewSearch.Filter(DraftingViewItems, PickerQuery, scope);
            DraftingViewInfo keep = SelectedPickerView;
            PickerResults.Clear();
            foreach (DraftingViewInfo v in hits) PickerResults.Add(v);
            SelectedPickerView = keep != null && PickerResults.Contains(keep) ? keep : PickerResults.FirstOrDefault();
            int placed = DraftingViewItems.Count(v => v.IsOnSheet);
            PickerSummary = $"{PickerResults.Count} of {DraftingViewItems.Count} drafting views · {placed} on a sheet";
            foreach (AssignSlotViewModel slot in AssignSlots) slot.Refresh();
        }
    }
}
