using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.UI.ViewModels
{
    /// <summary>One room line in the grid: the room's data plus its tick.</summary>
    public sealed partial class RoomRowViewModel : ObservableObject
    {
        private readonly Action<RoomRowViewModel> _tickChanged;

        /// <summary>Wraps <paramref name="row"/>; <paramref name="tickChanged"/> is called when the user ticks or unticks it.</summary>
        public RoomRowViewModel(RoomRow row, bool isTicked, string groupKey, Action<RoomRowViewModel> tickChanged)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            _isTicked = isTicked;
            GroupKey = groupKey;
            _tickChanged = tickChanged;
        }

        /// <summary>The room's data.</summary>
        public RoomRow Row { get; }

        /// <summary>Text of the group this row is listed under.</summary>
        public string GroupKey { get; }

        /// <summary>Ticked for the next run.</summary>
        [ObservableProperty] private bool _isTicked;

        partial void OnIsTickedChanged(bool value) => _tickChanged?.Invoke(this);

        /// <summary>Sets the tick without telling the owner (used for bulk changes).</summary>
        public void SetTickSilently(bool value)
        {
#pragma warning disable MVVMTK0034 // bulk update: one notification, no per-row callback
            _isTicked = value;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(IsTicked));
        }

        /// <summary>Room key.</summary>
        public string RoomKey => Row.RoomKey;
        /// <summary>Host or link name.</summary>
        public string Source => Row.Source;
        /// <summary>Level name.</summary>
        public string Level => Row.Level;
        /// <summary>Room number.</summary>
        public string Number => Row.Number;
        /// <summary>Room name.</summary>
        public string Name => Row.Name;
        /// <summary>Room area, m².</summary>
        public double AreaM2 => Row.AreaM2;
        /// <summary>Matched keyword, or empty.</summary>
        public string Matched => Row.Matched ?? string.Empty;
        /// <summary>Slab text.</summary>
        public string SlabText => Row.SlabText;
        /// <summary>True when a slab was found.</summary>
        public bool HasSlab => Row.SlabTopMm.HasValue;
        /// <summary>Room state.</summary>
        public RoomStatus Status => Row.Status;
        /// <summary>Why the room has that state.</summary>
        public string StatusDetail => Row.StatusDetail;
        /// <summary>True when the row comes from a link.</summary>
        public bool IsLinked => Row.RoomKey != null && !Row.RoomKey.StartsWith("Host:", StringComparison.Ordinal);

        /// <summary>Short status text for the grid.</summary>
        public string StatusText => Row.Status switch
        {
            RoomStatus.Ready => "Ready",
            RoomStatus.Repaired => "Repaired",
            RoomStatus.AreaWarning => Row.StatusDetail,
            RoomStatus.Approximate => "Approximate",
            RoomStatus.NoSlab => "No slab",
            RoomStatus.Owned => Row.StatusDetail,
            RoomStatus.NotEnclosed => "Not enclosed",
            RoomStatus.AlreadyDone => "Already done",
            RoomStatus.NotChecked => "Not checked",
            _ => "No outline"
        };

        /// <summary>ok, warn or error: picks the status chip colour.</summary>
        public string StatusKind => Row.Status switch
        {
            RoomStatus.Ready => "ok",
            RoomStatus.AlreadyDone or RoomStatus.NotChecked => "info",
            RoomStatus.Repaired or RoomStatus.AreaWarning or RoomStatus.Approximate or RoomStatus.NoSlab => "warn",
            _ => "error"
        };

        /// <summary>True when a run can make a roof for this room.</summary>
        public bool CanRun => Row.Status is not (RoomStatus.Owned or RoomStatus.NotEnclosed or RoomStatus.Failed);

        /// <summary>True when the user should look at this room (repair, warning, or it cannot run).</summary>
        public bool NeedsAttention => StatusKind is "warn" or "error";
    }

    /// <summary>A level button above the grid.</summary>
    public sealed partial class LevelButtonViewModel : ObservableObject
    {
        /// <summary>Creates the button for <paramref name="level"/>.</summary>
        public LevelButtonViewModel(string level, int count, bool isOn)
        {
            Level = level;
            Count = count;
            _isOn = isOn;
        }

        /// <summary>Level name; null for the "All" button.</summary>
        public string Level { get; }
        /// <summary>Rooms on the level.</summary>
        public int Count { get; }
        /// <summary>"L01 · 2", or "All".</summary>
        public string Label => Level == null ? "All" : $"{Level} · {Count}";
        /// <summary>Shown in the grid.</summary>
        [ObservableProperty] private bool _isOn;
    }

    /// <summary>One value in a column's tick-list filter.</summary>
    public sealed partial class FilterItemViewModel : ObservableObject
    {
        /// <summary>Creates the item.</summary>
        public FilterItemViewModel(string text, bool isChecked)
        {
            Text = text;
            _isChecked = isChecked;
        }

        /// <summary>The value.</summary>
        public string Text { get; }
        /// <summary>Shown in the grid.</summary>
        [ObservableProperty] private bool _isChecked;
    }
}
