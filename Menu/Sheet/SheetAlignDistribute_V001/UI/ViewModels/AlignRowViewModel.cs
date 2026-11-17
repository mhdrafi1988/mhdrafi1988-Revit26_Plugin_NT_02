using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.ViewModels
{
    /// <summary>
    /// One line in the Elements grid. Wraps the planned <see cref="AlignMove"/> and adds the tick,
    /// which is the only thing the user edits here; everything else is read back from the plan.
    /// </summary>
    public sealed partial class AlignRowViewModel : ObservableObject
    {
        private readonly Action<AlignRowViewModel> _tickChanged;
        private bool _isTicked;

        /// <summary>Wraps <paramref name="move"/>; <paramref name="tickChanged"/> is called when the user ticks or unticks.</summary>
        public AlignRowViewModel(AlignMove move, Action<AlignRowViewModel> tickChanged)
        {
            Move = move ?? throw new ArgumentNullException(nameof(move));
            _tickChanged = tickChanged;
            _isTicked = move.IsTicked;
        }

        /// <summary>What the plan does with this element.</summary>
        public AlignMove Move { get; }

        /// <summary>The element id's value; identifies this line across refreshes.</summary>
        public long Key => Move.Key;

        /// <summary>True while this element takes part in the run.</summary>
        public bool IsTicked
        {
            get => _isTicked;
            set
            {
                if (!CanTick || _isTicked == value) return;
                _isTicked = value;
                OnPropertyChanged();
                _tickChanged?.Invoke(this);
            }
        }

        /// <summary>False for elements that are left alone whatever the tick says.</summary>
        public bool CanTick => Move.CanTick;

        // ── Columns ────────────────────────────────────────────────────────

        /// <summary>The viewport's Detail Number, or "–".</summary>
        public string DetailNumber => string.IsNullOrWhiteSpace(Move.Element?.DetailNumber)
            ? AlignMove.NoValue : Move.Element.DetailNumber;

        /// <summary>View or schedule name.</summary>
        public string Name => Move.Element?.Name ?? "";

        /// <summary>View type (Floor Plan, Drafting, Legend, Schedule, ...).</summary>
        public string TypeName => Move.Element?.TypeName ?? "";

        /// <summary>"W × H" in whole mm.</summary>
        public string SizeText => Move.SizeText;

        /// <summary>1-based row on the sheet.</summary>
        public string RowLabel => Move.RowLabel;

        /// <summary>1-based column on the sheet.</summary>
        public string ColumnLabel => Move.ColumnLabel;

        /// <summary>This element's own clearance, or "–" when it uses the sheet gap.</summary>
        public string PadLabel => Move.PadLabel;

        /// <summary>X movement in mm, signed.</summary>
        public string DeltaXLabel => Move.DeltaXLabel;

        /// <summary>Y movement in mm, signed, positive up.</summary>
        public string DeltaYLabel => Move.DeltaYLabel;

        /// <summary>How far it travels, in mm.</summary>
        public string DistanceLabel => Move.DistanceLabel;

        /// <summary>Status as shown in the grid.</summary>
        public string StatusLabel => Move.StatusLabel;

        /// <summary>Status, for the pill colour.</summary>
        public AlignStatus Status => Move.Status;

        /// <summary>Short explanation, or null.</summary>
        public string Note => Move.Note;

        /// <summary>Everything about this line in one tooltip.</summary>
        public string Tooltip => string.IsNullOrEmpty(Note)
            ? $"{Name} ({TypeName}) — {StatusLabel}"
            : $"{Name} ({TypeName}) — {StatusLabel}. {Note}";

        /// <summary>Group header text when the grid is grouped by row.</summary>
        public string GroupName => Move.Row is int row ? $"Row {row}" : "Not on a row";
    }
}
