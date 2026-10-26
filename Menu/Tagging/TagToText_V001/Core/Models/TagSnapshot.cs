using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.TagToText.V001.Core.Models
{
    /// <summary>
    /// What Tag To Text read from one tag: its text and where its text and leader sit.
    /// Points are in model coordinates, already on the view plane.
    /// </summary>
    public sealed class TagSnapshot
    {
        /// <summary>The tag's element id.</summary>
        public ElementId TagId { get; init; }

        /// <summary>The kind of tag.</summary>
        public TagKind Kind { get; init; }

        /// <summary>Category or family name shown in the table, e.g. "Roof Tags".</summary>
        public string TypeLabel { get; init; }

        /// <summary>
        /// Text lines as the tag shows them. Spot Coordinates have two (N, E);
        /// everything else normally has one.
        /// </summary>
        public string[] Lines { get; init; }

        /// <summary>Where the centre of the tag text is.</summary>
        public XYZ TextPoint { get; init; }

        /// <summary>Where the leader points (the tagged point). Null when the tag has no leader.</summary>
        public XYZ LeaderEnd { get; init; }

        /// <summary>The leader's bend point, or null.</summary>
        public XYZ LeaderElbow { get; init; }

        /// <summary>Tag rotation in the view, in radians.</summary>
        public double Rotation { get; init; }

        /// <summary>True when the tag is in a model group (it can't be deleted on its own).</summary>
        public bool InGroup { get; init; }

        /// <summary>User who owns the tag in a workshared model, or null when it is free or yours.</summary>
        public string OwnedBy { get; init; }

        /// <summary>Why the tag can't be copied, or null when it can.</summary>
        public string Problem { get; init; }

        /// <summary>True when the tag had a leader.</summary>
        public bool HasLeader => LeaderEnd != null;
    }
}
