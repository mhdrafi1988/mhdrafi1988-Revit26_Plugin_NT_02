using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.ViewModels
{
    /// <summary>How a row's status is coloured.</summary>
    public enum RowStatusKind
    {
        /// <summary>Nothing happens (grey).</summary>
        None = 0,

        /// <summary>Stays where it is on the target.</summary>
        Stay = 1,

        /// <summary>Re-packed on the target (green).</summary>
        Repack = 2,

        /// <summary>Moves from another sheet (blue).</summary>
        Move = 3,

        /// <summary>Legend copy (navy).</summary>
        Copy = 4,

        /// <summary>Skipped (red).</summary>
        Skip = 5
    }

    /// <summary>One view in the Combined Views grid.</summary>
    public sealed partial class ViewRowViewModel : ObservableObject
    {
        private readonly Action<ViewRowViewModel> _onTickChanged;
        private bool _ticked;

        /// <summary>Creates a row for <paramref name="view"/> from sheet <paramref name="sheet"/> in slot <paramref name="slot"/>.</summary>
        public ViewRowViewModel(PlacedView view, SheetContent sheet, int slot, bool ticked, Action<ViewRowViewModel> onTickChanged)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            Slot = slot;
            _ticked = ticked;
            _onTickChanged = onTickChanged;
            string prefix = slot switch { 1 => "Sheet 1", 2 => "Sheet 2", _ => "Target" };
            SheetGroupKey = $"{slot}|{prefix} · {sheet?.Label}";
            TypeGroupKey = $"{slot}|{view.ViewTypeLabel}";
            SizeText = string.Format(CultureInfo.InvariantCulture, "{0:0} × {1:0}",
                view.Footprint.Width * 304.8, view.Footprint.Height * 304.8);
        }

        /// <summary>The view as read.</summary>
        public PlacedView View { get; }

        /// <summary>1 = Sheet 1, 2 = Sheet 2, 3 = other target sheet.</summary>
        public int Slot { get; }

        /// <summary>Viewport key.</summary>
        public long Key => View.ViewportKey;

        /// <summary>View name.</summary>
        public string Name => View.Name;

        /// <summary>View type label.</summary>
        public string ViewTypeLabel => View.ViewTypeLabel;

        /// <summary>Scale label.</summary>
        public string ScaleLabel => View.ScaleLabel;

        /// <summary>Detail number on its sheet now.</summary>
        public string DetailNumber => View.DetailNumber;

        /// <summary>"480 × 340" (mm on paper, box plus title).</summary>
        public string SizeText { get; }

        /// <summary>Outer group: "slot|Sheet 1 · A-101 - Name".</summary>
        public string SheetGroupKey { get; }

        /// <summary>Inner group: "slot|View type".</summary>
        public string TypeGroupKey { get; }

        /// <summary>Ticked by the user (or forced by "Re-pack them too").</summary>
        public bool IsTicked
        {
            get => _ticked || IsForced;
            set
            {
                if (IsForced || _ticked == value) return;
                _ticked = value;
                OnPropertyChanged();
                _onTickChanged?.Invoke(this);
            }
        }

        /// <summary>The user's own tick, ignoring <see cref="IsForced"/>.</summary>
        public bool UserTicked => _ticked;

        /// <summary>Sets the tick without telling the window (bulk changes recompute once).</summary>
        public void SetTickQuietly(bool value)
        {
            if (_ticked == value) return;
            _ticked = value;
            OnPropertyChanged(nameof(IsTicked));
        }

        /// <summary>True when "Re-pack them too" includes this target view regardless of the tick.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTicked))]
        [NotifyPropertyChangedFor(nameof(CanTick))]
        private bool isForced;

        /// <summary>The tick box is enabled.</summary>
        public bool CanTick => !IsForced;

        /// <summary>What happens to the view.</summary>
        [ObservableProperty] private string status = string.Empty;

        /// <summary>Colour of <see cref="Status"/>.</summary>
        [ObservableProperty] private RowStatusKind statusKind;
    }

    /// <summary>One line of the Confirm &amp; Move plan.</summary>
    public sealed class PlanLine
    {
        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Sheet it is on now.</summary>
        public string From { get; init; } = string.Empty;

        /// <summary>Sheet it goes to.</summary>
        public string To { get; init; } = string.Empty;

        /// <summary>Detail number after the run (with a note when it changes).</summary>
        public string Detail { get; init; } = string.Empty;

        /// <summary>Where on the sheet.</summary>
        public string Location { get; init; } = string.Empty;

        /// <summary>Action text.</summary>
        public string Action { get; init; } = string.Empty;

        /// <summary>Colour of <see cref="Action"/>.</summary>
        public RowStatusKind Kind { get; init; }

        /// <summary>1 = Sheet 1, 2 = Sheet 2, 3 = target.</summary>
        public int Slot { get; init; }
    }

    /// <summary>One line of the Move Complete report.</summary>
    public sealed class ResultLine
    {
        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Moved / Re-packed / Copied / Skipped / Failed.</summary>
        public string Outcome { get; init; } = string.Empty;

        /// <summary>Where it went, or why it was skipped.</summary>
        public string Detail { get; init; } = string.Empty;

        /// <summary>Colour of <see cref="Outcome"/>.</summary>
        public RowStatusKind Kind { get; init; }

        /// <summary>1 = Sheet 1, 2 = Sheet 2, 3 = target.</summary>
        public int Slot { get; init; }
    }

    /// <summary>A tab above the preview, one per sheet in the plan.</summary>
    public sealed partial class PageTabViewModel : ObservableObject
    {
        private readonly Action<int> _select;

        /// <summary>Creates a tab.</summary>
        public PageTabViewModel(int index, string label, bool isCurrent, Action<int> select)
        {
            Index = index;
            Label = label;
            this.isCurrent = isCurrent;
            _select = select;
        }

        /// <summary>Index into the plan's sheets.</summary>
        public int Index { get; }

        /// <summary>Sheet number (and view count).</summary>
        public string Label { get; }

        /// <summary>Shown in the preview.</summary>
        [ObservableProperty] private bool isCurrent;

        partial void OnIsCurrentChanged(bool value)
        {
            if (value) _select?.Invoke(Index);
        }
    }
}
