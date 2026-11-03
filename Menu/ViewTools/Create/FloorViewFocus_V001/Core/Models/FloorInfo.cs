namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models
{
    /// <summary>Display + lookup data for one selected floor.</summary>
    public sealed class FloorInfo
    {
        /// <summary>Revit ElementId value (display / logging only).</summary>
        public long Id { get; init; }

        /// <summary>Stable lookup key used by the ExternalEvent handler.</summary>
        public string UniqueId { get; init; } = string.Empty;

        /// <summary>e.g. "Basic Floor : Generic - 300mm".</summary>
        public string Description { get; init; } = string.Empty;
    }
}
