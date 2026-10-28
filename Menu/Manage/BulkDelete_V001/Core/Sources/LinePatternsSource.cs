using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources
{
    /// <summary>
    /// Line patterns: the dash and dot patterns (Manage &gt; Additional Settings &gt; Line Patterns), which
    /// CAD users call line types. Includes patterns that came in with imported DWG files. Revit does not
    /// report where a pattern is used, so the Used By column stays empty.
    /// </summary>
    public sealed class LinePatternsSource : DeleteSource
    {
        /// <inheritdoc/>
        public override string Key => "LinePatterns";

        /// <inheritdoc/>
        public override string DisplayName => "Line patterns";

        /// <inheritdoc/>
        public override string Description
            => "Dash and dot patterns (Additional Settings > Line Patterns), including patterns that came in with DWG imports. "
             + "Revit cannot report where a pattern is used: anything that still uses a deleted pattern changes appearance.";

        /// <inheritdoc/>
        protected override IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Select(pattern => new FoundItem(pattern))
                .ToList();
    }
}
