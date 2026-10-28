using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models
{
    /// <summary>A title block or viewport type the user can choose.</summary>
    public sealed record NamedType(long Key, string Name);

    /// <summary>
    /// Everything read from the model in one pass: the sections on offer, the sheets they are on,
    /// sheet numbers in use, and the title block / viewport type choices. Keys are element id values.
    /// </summary>
    public sealed class SourceSnapshot
    {
        /// <summary>The view the sections are taken from.</summary>
        public long SourceViewKey { get; init; }

        /// <summary>Its name.</summary>
        public string SourceViewName { get; init; } = string.Empty;

        /// <summary>Its scale (100 for 1:100); 1 when unknown.</summary>
        public int SourceViewScale { get; init; } = 1;

        /// <summary>Sections whose marker is visible in the source view.</summary>
        public IReadOnlyList<long> InViewKeys { get; init; } = Array.Empty<long>();

        /// <summary>Sections selected in Revit (markers, or views in the Project Browser).</summary>
        public IReadOnlyList<long> SelectionKeys { get; init; } = Array.Empty<long>();

        /// <summary>Selected elements that are not section markers or section views.</summary>
        public int IgnoredSelectionCount { get; init; }

        /// <summary>Every section read, by key.</summary>
        public IReadOnlyDictionary<long, SectionCandidate> Sections { get; init; } = new Dictionary<long, SectionCandidate>();

        /// <summary>The sheets those sections are on now, by key.</summary>
        public IReadOnlyDictionary<long, SheetContent> Sheets { get; init; } = new Dictionary<long, SheetContent>();

        /// <summary>Every sheet number in the model.</summary>
        public IReadOnlyCollection<string> ExistingSheetNumbers { get; init; } = Array.Empty<string>();

        /// <summary>Title block types in the project, by name.</summary>
        public IReadOnlyList<NamedType> TitleBlocks { get; init; } = Array.Empty<NamedType>();

        /// <summary>Viewport types in the project, by name.</summary>
        public IReadOnlyList<NamedType> ViewportTypes { get; init; } = Array.Empty<NamedType>();

        /// <summary>The project's default viewport type, or 0.</summary>
        public long DefaultViewportTypeKey { get; init; }
    }

    /// <summary>Footprints of the sections for one viewport type, and the title block's extent.</summary>
    public sealed class MeasureResult
    {
        /// <summary>Title block used.</summary>
        public long TitleBlockKey { get; init; }

        /// <summary>Viewport type used (0 = Revit's default).</summary>
        public long ViewportTypeKey { get; init; }

        /// <summary>The title block's extent on a new sheet (sheet feet), or null.</summary>
        public Layout.LayoutRect? Frame { get; init; }

        /// <summary>Why <see cref="Frame"/> is null.</summary>
        public string FrameProblem { get; init; }

        /// <summary>Footprints by section key.</summary>
        public Dictionary<long, Footprint> Footprints { get; init; } = new();

        /// <summary>Why a section cannot be measured or placed, by key.</summary>
        public Dictionary<long, string> Problems { get; init; } = new();
    }
}
