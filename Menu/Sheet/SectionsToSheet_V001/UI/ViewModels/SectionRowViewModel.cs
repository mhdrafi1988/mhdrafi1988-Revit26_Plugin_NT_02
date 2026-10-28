using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.UI.ViewModels
{
    /// <summary>Colour of a row's sheet chip.</summary>
    public enum ChipKind
    {
        /// <summary>Not on a sheet.</summary>
        Neutral,

        /// <summary>On another sheet — it will move.</summary>
        OnSheet,

        /// <summary>Locked (worksharing) — it can't move.</summary>
        Locked
    }

    /// <summary>Colour of a row's status line.</summary>
    public enum RowStatusKind
    {
        /// <summary>No status (unticked).</summary>
        None,

        /// <summary>Goes on the new sheet.</summary>
        Placed,

        /// <summary>Comes off another sheet onto the new one.</summary>
        Moved,

        /// <summary>Skipped on purpose (setting).</summary>
        Warning,

        /// <summary>Can't be placed.</summary>
        Problem
    }

    /// <summary>One section in the list: tick box, name, sheet chip, size and what will happen to it.</summary>
    public sealed partial class SectionRowViewModel : ObservableObject
    {
        private readonly Action<SectionRowViewModel> _onTickChanged;
        private bool _silent;

        /// <summary>Creates the row; <paramref name="onTickChanged"/> runs when the user ticks or unticks it.</summary>
        public SectionRowViewModel(SectionCandidate data, bool isTicked, Action<SectionRowViewModel> onTickChanged)
        {
            _onTickChanged = onTickChanged;
            _silent = true;
            IsTicked = isTicked;
            _silent = false;
            Update(data, null, null);
        }

        /// <summary>The section as last read.</summary>
        public SectionCandidate Data { get; private set; }

        /// <summary>Section view key.</summary>
        public long Key => Data.Key;

        [ObservableProperty] private bool isTicked;
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string meta = string.Empty;
        [ObservableProperty] private string chip = string.Empty;
        [ObservableProperty] private ChipKind chipKind;
        [ObservableProperty] private string status = string.Empty;
        [ObservableProperty] private RowStatusKind statusKind;
        [ObservableProperty] private bool hasStatus;

        partial void OnIsTickedChanged(bool value)
        {
            if (!_silent)
                _onTickChanged?.Invoke(this);
        }

        /// <summary>Sets the tick without telling the view model (bulk changes re-plan once).</summary>
        public void SetTickSilently(bool value)
        {
            _silent = true;
            IsTicked = value;
            _silent = false;
        }

        /// <summary>Refreshes the texts from a new read and its measured size (null while not measured).</summary>
        public void Update(SectionCandidate data, Footprint footprint, string measureNote)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Name = data.Name;

            string size = footprint != null
                ? $"{Math.Round(footprint.Width * 304.8):0} × {Math.Round(footprint.Height * 304.8):0} mm with title"
                : measureNote ?? "measuring…";
            Meta = data.ScaleText + " · " + size + (data.InSourceView ? string.Empty : " · not shown in this view");

            if (data.LockReason != null)
            {
                Chip = (data.CurrentSheetNumber ?? "Locked") + " · locked";
                ChipKind = ChipKind.Locked;
            }
            else if (data.IsOnSheet)
            {
                Chip = "On " + data.CurrentSheetNumber;
                ChipKind = ChipKind.OnSheet;
            }
            else
            {
                Chip = "Not on a sheet";
                ChipKind = ChipKind.Neutral;
            }
        }

        /// <summary>Sets the status line.</summary>
        public void SetStatus(string text, RowStatusKind kind)
        {
            Status = text ?? string.Empty;
            StatusKind = string.IsNullOrEmpty(text) ? RowStatusKind.None : kind;
            HasStatus = !string.IsNullOrEmpty(text);
        }
    }

    /// <summary>One tab above the preview (a new sheet).</summary>
    public sealed class PageTabViewModel : ObservableObject
    {
        private readonly Action<int> _select;
        private bool _isCurrent;

        /// <summary>Creates the tab.</summary>
        public PageTabViewModel(int index, string label, bool isCurrent, Action<int> select)
        {
            Index = index;
            Label = label;
            _select = select;
            _isCurrent = isCurrent;
        }

        /// <summary>0-based sheet.</summary>
        public int Index { get; }

        /// <summary>Sheet number.</summary>
        public string Label { get; }

        /// <summary>Bound to the tab's radio button; selecting it shows that sheet.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (!SetProperty(ref _isCurrent, value)) return;
                if (value) _select?.Invoke(Index);
            }
        }
    }

    /// <summary>A line in the confirm dialog: a section that comes off its sheet.</summary>
    public sealed record MoveLine(string Name, string From, string To);
}
