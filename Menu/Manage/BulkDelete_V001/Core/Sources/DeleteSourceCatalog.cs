using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources
{
    /// <summary>The kinds of item Bulk Delete offers. Add a new <see cref="DeleteSource"/> here to show it.</summary>
    public static class DeleteSourceCatalog
    {
        /// <summary>Creates one of each source, in the order of the "What to delete" list.</summary>
        public static IReadOnlyList<DeleteSource> CreateAll() => new DeleteSource[]
        {
            new LineStylesSource(),
            new LinePatternsSource(),
            new ArrowheadsSource(),
            new FillPatternsSource()
        };
    }
}
