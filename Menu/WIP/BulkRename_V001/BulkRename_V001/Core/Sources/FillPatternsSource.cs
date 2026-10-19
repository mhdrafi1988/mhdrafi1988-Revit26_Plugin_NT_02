using Autodesk.Revit.DB;
using Revit26_Plugin.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.BulkRename.V001.Core.Sources
{
    /// <summary>
    /// Fill patterns: drafting and model hatch patterns (Manage &gt; Additional Settings &gt; Fill Patterns).
    /// The built-in solid fill cannot be renamed.
    /// </summary>
    public sealed class FillPatternsSource : RenameSource
    {
        /// <inheritdoc/>
        public override string Key => "FillPatterns";

        /// <inheritdoc/>
        public override string DisplayName => "Fill patterns";

        /// <inheritdoc/>
        public override string Description
            => "Drafting and model hatch patterns (Additional Settings > Fill Patterns). The solid fill cannot be renamed.";

        /// <inheritdoc/>
        protected override IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log)
        {
            var found = new List<FoundItem>();

            foreach (var element in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                var pattern = element.GetFillPattern();
                string kind = pattern == null ? string.Empty
                    : pattern.Target == FillPatternTarget.Model ? "Model" : "Drafting";
                string builtIn = pattern != null && pattern.IsSolidFill ? "Built-in solid fill." : null;

                found.Add(new FoundItem(element, kind, null, builtIn));
            }

            return found;
        }
    }
}
