using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>
    /// Everything the planner needs to know about one placed element, read once from the model.
    /// Plain data, so the ID format can change in the window without touching Revit again.
    /// Coordinates are Revit internal units (feet).
    /// </summary>
    public sealed class ElementSnapshot
    {
        /// <summary>Revit element id (<c>ElementId.Value</c>).</summary>
        public long ElementId { get; set; }

        /// <summary>Revit UniqueId; different for a copy of the element.</summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>Revit category id.</summary>
        public long CategoryId { get; set; }

        /// <summary>Category name.</summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>Revit type id.</summary>
        public long TypeId { get; set; }

        /// <summary>Family name.</summary>
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>Type name.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Type Mark, empty when not set.</summary>
        public string TypeMark { get; set; } = string.Empty;

        /// <summary>Name of the element's level, empty when none was found.</summary>
        public string LevelName { get; set; } = string.Empty;

        /// <summary>Level elevation in millimetres, null when no level was found.</summary>
        public double? LevelElevationMm { get; set; }

        /// <summary>Name of the smallest scope box around the element's centre in plan, empty when none.</summary>
        public string ScopeBoxName { get; set; } = string.Empty;

        /// <summary>True when <see cref="CenterX"/> / <see cref="CenterY"/> hold a real point.</summary>
        public bool HasCenter { get; set; }

        /// <summary>Plan X of the element's centre point (bounding-box centre), feet.</summary>
        public double CenterX { get; set; }

        /// <summary>Plan Y of the element's centre point (bounding-box centre), feet.</summary>
        public double CenterY { get; set; }

        /// <summary>
        /// Values of the parameters the format asks for, keyed by parameter name: element (instance)
        /// parameters first, then type parameters. A missing key means the parameter does not exist.
        /// </summary>
        public Dictionary<string, string> ParameterValues { get; set; } = new();

        /// <summary>Values of the level's own parameters the format asks for, keyed by parameter name.</summary>
        public Dictionary<string, string> LevelParameterValues { get; set; } = new();

        /// <summary>Current Tracker_ID, empty when none.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>Current Tracker_SourceGuid, empty when none.</summary>
        public string SourceGuid { get; set; } = string.Empty;

        /// <summary>Current Tracker_Zone.</summary>
        public string StoredZone { get; set; } = string.Empty;

        /// <summary>Current Tracker_Level.</summary>
        public string StoredLevel { get; set; } = string.Empty;

        /// <summary>Current Tracker_Grid.</summary>
        public string StoredGrid { get; set; } = string.Empty;

        /// <summary>Current Tracker_Status.</summary>
        public string StoredStatus { get; set; } = string.Empty;

        /// <summary>Current Tracker_Progress (0–100), null when empty.</summary>
        public double? StoredProgress { get; set; }

        /// <summary>Current Tracker_StatusDate.</summary>
        public string StoredStatusDate { get; set; } = string.Empty;

        /// <summary>The Revit file the element is in; the same object for every element of a run.</summary>
        public ModelIdentity Model { get; set; }

        /// <summary>Why the element cannot be edited, or null when it can.</summary>
        public string LockReason { get; set; }
    }
}
