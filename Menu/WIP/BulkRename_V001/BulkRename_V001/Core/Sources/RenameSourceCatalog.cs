using System.Collections.Generic;

namespace Revit26_Plugin.BulkRename.V001.Core.Sources
{
    /// <summary>The kinds of item Bulk Rename offers. Add a new <see cref="RenameSource"/> here to show it.</summary>
    public static class RenameSourceCatalog
    {
        /// <summary>Creates one of each source, in the order of the "What to rename" list.</summary>
        public static IReadOnlyList<RenameSource> CreateAll() => new RenameSource[]
        {
            new LineStylesSource(),
            new LinePatternsSource(),
            new ArrowheadsSource(),
            new FillPatternsSource()
        };
    }
}
