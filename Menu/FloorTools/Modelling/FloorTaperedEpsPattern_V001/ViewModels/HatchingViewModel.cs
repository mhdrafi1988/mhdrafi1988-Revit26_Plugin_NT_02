using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>Hatching card: colour-code pieces with filled regions, and where the fill types come from.</summary>
    public sealed partial class HatchingViewModel : ObservableObject
    {
        public HatchingViewModel(IReadOnlyList<FillTypeOption> fillTypes)
        {
            FillTypes = fillTypes;
            _selectedMode = Modes[0];
            _selectedSource = Sources[0];
            _slot1 = fillTypes.ElementAtOrDefault(0);
            _slot2 = fillTypes.ElementAtOrDefault(1) ?? _slot1;
            _slot3 = fillTypes.ElementAtOrDefault(2) ?? _slot1;
            _slot4 = fillTypes.ElementAtOrDefault(3) ?? _slot2;
        }

        public IReadOnlyList<ChoiceOption<HatchMode>> Modes { get; } = new[]
        {
            new ChoiceOption<HatchMode>(HatchMode.None, "No hatching"),
            new ChoiceOption<HatchMode>(HatchMode.AlternateRows, "Alternate rows (odd / even)"),
            new ChoiceOption<HatchMode>(HatchMode.AlternateBoards, "Alternate boards (checkerboard)"),
            new ChoiceOption<HatchMode>(HatchMode.BoardCode, "By board code (F01, H01-L …)"),
            new ChoiceOption<HatchMode>(HatchMode.PieceType, "By piece type (full / hip / straight / ridge)")
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEnabled))]
        [NotifyPropertyChangedFor(nameof(IsSlotPickEnabled))]
        private ChoiceOption<HatchMode> _selectedMode;

        public IReadOnlyList<ChoiceOption<HatchTypeSource>> Sources { get; } = new[]
        {
            new ChoiceOption<HatchTypeSource>(HatchTypeSource.CreateToolTypes, "Tool types EPS2D_Fill_nn (created if missing)"),
            new ChoiceOption<HatchTypeSource>(HatchTypeSource.PickExisting, "Pick existing filled region types")
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSlotPickEnabled))]
        private ChoiceOption<HatchTypeSource> _selectedSource;

        public IReadOnlyList<FillTypeOption> FillTypes { get; }

        [ObservableProperty] private FillTypeOption? _slot1;
        [ObservableProperty] private FillTypeOption? _slot2;
        [ObservableProperty] private FillTypeOption? _slot3;
        [ObservableProperty] private FillTypeOption? _slot4;

        public bool IsEnabled => SelectedMode.Value != HatchMode.None;

        public bool IsSlotPickEnabled => IsEnabled && SelectedSource.Value == HatchTypeSource.PickExisting;

        public bool HasErrors => IsSlotPickEnabled && PickedSlots().Count == 0;

        public event EventHandler? Changed;

        /// <summary>The picked types in slot order (empty slots skipped); more colour slots repeat these.</summary>
        public IReadOnlyList<FillTypeOption> PickedSlots() =>
            new[] { Slot1, Slot2, Slot3, Slot4 }.Where(s => s != null).Select(s => s!).ToList();

        public IReadOnlyList<ElementId> PickedTypeIds() => PickedSlots().Select(s => s.TypeId).ToList();

        public Pattern2DSettings ApplyTo(Pattern2DSettings settings) => settings with { Hatch = SelectedMode.Value };

        /// <summary>Colour and type name for a slot, as it will look in Revit.</summary>
        public (string Hex, string TypeName) SlotLook(int slot)
        {
            if (SelectedSource.Value == HatchTypeSource.PickExisting)
            {
                IReadOnlyList<FillTypeOption> picks = PickedSlots();
                if (picks.Count > 0)
                {
                    FillTypeOption p = picks[slot % picks.Count];
                    return (p.ColorHex, p.Name);
                }
            }

            int toolSlot = slot % FillTypeProvider.MaxToolTypes;
            return (HatchPalette.ForSlot(toolSlot).Hex, FillTypeProvider.ToolTypePrefix + (toolSlot + 1).ToString("00"));
        }

        public void Reset()
        {
            SelectedMode = Modes[0];
            SelectedSource = Sources[0];
        }

        partial void OnSelectedModeChanged(ChoiceOption<HatchMode> value) => RaiseChanged();
        partial void OnSelectedSourceChanged(ChoiceOption<HatchTypeSource> value) => RaiseChanged();
        partial void OnSlot1Changed(FillTypeOption? value) => RaiseChanged();
        partial void OnSlot2Changed(FillTypeOption? value) => RaiseChanged();
        partial void OnSlot3Changed(FillTypeOption? value) => RaiseChanged();
        partial void OnSlot4Changed(FillTypeOption? value) => RaiseChanged();

        private void RaiseChanged()
        {
            OnPropertyChanged(nameof(HasErrors));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
