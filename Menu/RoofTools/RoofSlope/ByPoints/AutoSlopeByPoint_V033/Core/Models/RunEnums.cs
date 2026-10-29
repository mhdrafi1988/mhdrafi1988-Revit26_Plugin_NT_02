// =======================================================
// File: RunEnums.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// New in V032.
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    /// <summary>How each picked point becomes a drain (V032 D2).</summary>
    public enum DrainMode
    {
        /// <summary>Use the closest existing roof shape point (in plan). Always resolves.</summary>
        SnapToNearestPoint = 0,

        /// <summary>Add a new shape point exactly at the pick, then use it.</summary>
        AddPointAtPick = 1
    }

    public enum MarkerGroupKind
    {
        Drain,
        HighestPoint,
        AllowedOffset
    }

    /// <summary>Window state. Drives button text, badges and what is enabled.</summary>
    public enum RunState
    {
        Ready,
        Picking,
        Running,
        Done,
        Cancelled,
        Failed
    }

    /// <summary>Log filter chips on the Processing log.</summary>
    public enum LogFilter
    {
        All,
        Warnings,
        Errors
    }

    /// <summary>Why a vertex was not sloped.</summary>
    public enum VertexOutcome
    {
        Processed,
        BeyondDistance,
        NoPath
    }
}
