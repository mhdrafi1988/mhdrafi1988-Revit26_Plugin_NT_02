using Autodesk.Revit.DB;
using Revit26_Plugin.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.BulkRename.V001.Core.Sources
{
    /// <summary>
    /// Line patterns: the dash and dot patterns (Manage &gt; Additional Settings &gt; Line Patterns), which
    /// CAD users call line types. Includes patterns that came in with imported DWG files.
    /// </summary>
    public sealed class LinePatternsSource : RenameSource
    {
        /// <inheritdoc/>
        public override string Key => "LinePatterns";

        /// <inheritdoc/>
        public override string DisplayName => "Line patterns";

        /// <inheritdoc/>
        public override string Description
            => "Dash and dot patterns (Additional Settings > Line Patterns), including patterns that came in with DWG imports.";

        /// <inheritdoc/>
        protected override IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Select(pattern => new FoundItem(pattern))
                .ToList();
    }
}
