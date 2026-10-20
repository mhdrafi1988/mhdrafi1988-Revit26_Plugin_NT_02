using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>Which elements <see cref="ModelReader"/> looks at: the whole model, a selection, or one view.</summary>
    public sealed class ReadScope
    {
        /// <summary>The whole model (the 1.0.0 behaviour).</summary>
        public static readonly ReadScope WholeModel = new() { Mode = ScopeMode.WholeModel };

        /// <summary>Whole model, selection or active view.</summary>
        public ScopeMode Mode { get; set; } = ScopeMode.WholeModel;

        /// <summary>Selected element ids (Selection mode).</summary>
        public HashSet<long> ElementIds { get; set; } = new();

        /// <summary>View id (ActiveView mode).</summary>
        public long ViewId { get; set; }

        /// <summary>Short description for the log, e.g. "12 selected elements".</summary>
        public string Describe(string viewName = null) => Mode switch
        {
            ScopeMode.Selection => $"{ElementIds.Count} selected element(s)",
            ScopeMode.ActiveView => $"elements visible in view '{viewName ?? ViewId.ToString()}'",
            _ => "the whole model"
        };
    }
}
