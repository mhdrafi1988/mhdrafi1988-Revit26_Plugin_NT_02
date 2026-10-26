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
            isSelected = snapshot.Problem == null;
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

        /// <summary>True when the tag can be copied.</summary>
        public bool IsCopyable => Snapshot.Problem == null;

        /// <summary>Status shown in the table.</summary>
        public string Status =>
            Snapshot.Problem ?? (Snapshot.OwnedBy != null ? $"OK · owned by {Snapshot.OwnedBy}"
                : Snapshot.InGroup ? "OK · in a group" : "OK");

        /// <summary>True when the status needs attention (problem, other owner or group).</summary>
        public bool HasWarning => Snapshot.Problem != null || Snapshot.OwnedBy != null || Snapshot.InGroup;

        /// <summary>The lines to write.</summary>
        public string[] Lines => TextRules.SplitLines(Text);
    }
}
