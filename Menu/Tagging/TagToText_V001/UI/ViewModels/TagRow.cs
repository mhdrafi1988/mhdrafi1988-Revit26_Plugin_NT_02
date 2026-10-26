using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using Revit26_Plugin_Revised.TagToText.V001.Core.Services;

namespace Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels
{
    /// <summary>One row of the tag table. The text can be edited before Create Text.</summary>
    public partial class TagRow : ObservableObject
    {
        /// <summary>Creates a row for <paramref name="snapshot"/>; copyable rows start ticked.</summary>
        public TagRow(TagSnapshot snapshot)
        {
            Snapshot = snapshot;
            text = string.Join("\n", snapshot.Lines);
            isSelected = IsCopyable;
        }

        /// <summary>The tag as read.</summary>
        public TagSnapshot Snapshot { get; }

        /// <summary>Ticked rows are copied.</summary>
        [ObservableProperty] private bool isSelected;

        /// <summary>The text to write, one line per line. Spot Coordinates: N line, then E line.</summary>
        [ObservableProperty] private string text;

        /// <summary>Kind shown in the Type column.</summary>
        public string KindLabel => Snapshot.Kind switch
        {
            TagKind.SpotElevation => "Spot Elevation",
            TagKind.SpotCoordinate => "Spot Coordinate",
            TagKind.SpotSlope => "Spot Slope",
            TagKind.SpatialTag => Snapshot.TypeLabel,
            _ => Snapshot.TypeLabel
        };

        /// <summary>Tag element id, for the tooltip.</summary>
        public string IdText => $"Id {Snapshot.TagId.Value}";

        /// <summary>"Yes" / "No".</summary>
        public string LeaderText => Snapshot.HasLeader ? "Yes" : "No";

        /// <summary>
        /// True when the tag can be copied: it was read without error, has a position, and the row has
        /// text (read from the tag, or typed by the user when Revit gave none).
        /// </summary>
        public bool IsCopyable => Snapshot.Problem == null && Snapshot.TextPoint != null && Lines.Length > 0;

        /// <summary>True when Revit gave no text for the tag and none has been typed yet.</summary>
        public bool NeedsText => Snapshot.Problem == null && Lines.Length == 0;

        partial void OnTextChanging(string value) => _wasCopyable = IsCopyable;

        private bool _wasCopyable;

        partial void OnTextChanged(string value)
        {
            OnPropertyChanged(nameof(IsCopyable));
            OnPropertyChanged(nameof(NeedsText));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(HasWarning));
            if (!IsCopyable) IsSelected = false;
            else if (!_wasCopyable) IsSelected = true;   // text typed into an empty row: tick it
        }

        /// <summary>Status shown in the table.</summary>
        public string Status =>
            Snapshot.Problem ?? (NeedsText ? "No text read · double-click Text to type it" : Snapshot.OwnedBy != null ? $"OK · owned by {Snapshot.OwnedBy}"
                : Snapshot.InGroup ? "OK · in a group" : "OK");

        /// <summary>True when the status needs attention (problem, other owner or group).</summary>
        public bool HasWarning => Snapshot.Problem != null || NeedsText || Snapshot.OwnedBy != null || Snapshot.InGroup;

        /// <summary>The lines to write.</summary>
        public string[] Lines => TextRules.SplitLines(Text);
    }
}
