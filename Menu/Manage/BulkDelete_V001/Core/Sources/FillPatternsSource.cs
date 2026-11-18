using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources
{
    /// <summary>
    /// Fill patterns: drafting and model hatch patterns (Manage &gt; Additional Settings &gt; Fill Patterns).
    /// The built-in solid fill can never be deleted. Revit does not report where a pattern is used, so the
    /// Used By column stays empty.
    /// </summary>
    public sealed class FillPatternsSource : DeleteSource
    {
        private const string SolidReason = "Built-in solid fill.";

        /// <inheritdoc/>
        public override string Key => "FillPatterns";

        /// <inheritdoc/>
        public override string DisplayName => "Fill patterns";

        /// <inheritdoc/>
        public override string Description
            => "Drafting and model hatch patterns (Additional Settings > Fill Patterns). The solid fill cannot be deleted. "
             + "Revit cannot report where a pattern is used: materials and regions that still use a deleted pattern change appearance.";

        /// <inheritdoc/>
        public override Func<Element, string> CreateRecheck(Document doc)
            => element => element is FillPatternElement fill && fill.GetFillPattern()?.IsSolidFill == true ? SolidReason : null;

        /// <inheritdoc/>
        protected override IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log)
        {
            var found = new List<FoundItem>();

            foreach (var element in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                var pattern = element.GetFillPattern();
                string kind = pattern == null ? string.Empty
                    : pattern.Target == FillPatternTarget.Model ? "Model" : "Drafting";

                found.Add(new FoundItem(element, kind, lockReason: pattern != null && pattern.IsSolidFill ? SolidReason : null));
            }

            return found;
        }
    }
}
