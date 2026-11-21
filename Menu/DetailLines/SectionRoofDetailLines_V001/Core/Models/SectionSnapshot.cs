using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models
{
    /// <summary>One section view the run can cover, as read from the model.</summary>
    public sealed class SectionTarget
    {
        /// <summary>Element id of the section view.</summary>
        public long ViewKey { get; set; }

        /// <summary>The view's name.</summary>
        public string ViewName { get; set; } = string.Empty;

        /// <summary>Detail number of its viewport on the sheet, or empty when it is not on one.</summary>
        public string DetailNumber { get; set; } = string.Empty;

        /// <summary>The view's scale denominator (0 when Revit cannot give one).</summary>
        public int Scale { get; set; }

        /// <summary>True when the view's crop box is switched off, so more may be visible in it
        /// than the crop box describes.</summary>
        public bool CropInactive { get; set; }

        /// <summary>Roofs visible in this section, from the host model and every link.</summary>
        public int RoofCount { get; set; }

        /// <summary>Walls visible in this section.</summary>
        public int WallCount { get; set; }

        /// <summary>Every element visible in this section.</summary>
        public int ElementCount { get; set; }

        /// <summary>Why nothing can be drawn in this view — it or its sheet is owned by someone
        /// else, or it changed in central — or null when it can be drawn in.</summary>
        public string LockReason { get; set; }

        /// <summary>True when this view can take lines.</summary>
        public bool CanDraw => LockReason == null;
    }

    /// <summary>One element visible in one section view, for the list and the export.</summary>
    public sealed class VisibleElementRow
    {
        /// <summary>Which element, and which document it is in.</summary>
        public ElementKey Key { get; set; }

        /// <summary>Its type, and the document the type is in.</summary>
        public TypeKey Type { get; set; }

        /// <summary>"Host model", or the link instance's name.</summary>
        public string SourceLabel { get; set; } = string.Empty;

        /// <summary>Where the element comes from.</summary>
        public ElementSourceKind Source { get; set; }

        /// <summary>Its category's name, or "(no category)".</summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>Its family's name; for system families, its category's name — the way Revit's
        /// own Project Browser groups them.</summary>
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>Its type's name.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>What the tool can do with it.</summary>
        public ElementRole Role { get; set; }

        /// <summary>Element id of the section view it is visible in.</summary>
        public long ViewKey { get; set; }

        /// <summary>That view's name.</summary>
        public string ViewName { get; set; } = string.Empty;
    }

    /// <summary>
    /// The model as read when the window opened or was last refreshed: the sections on offer and
    /// every element visible in them, with the line styles the project has.
    /// </summary>
    public sealed class ModelSnapshot
    {
        /// <summary>Sections the run can cover, in sheet order.</summary>
        public List<SectionTarget> Sections { get; set; } = new();

        /// <summary>How those sections were chosen.</summary>
        public SectionSourceKind SectionSource { get; set; }

        /// <summary>Number and name of the sheet the sections came from, or empty.</summary>
        public string SheetLabel { get; set; } = string.Empty;

        /// <summary>Every element visible in every section read, one row per element per view.</summary>
        public List<VisibleElementRow> Elements { get; set; } = new();

        /// <summary>Names of the project's detail line styles, for the two style pickers.</summary>
        public List<string> LineStyleNames { get; set; } = new();

        /// <summary>Links that are loaded but could not be read, with the reason.</summary>
        public List<LogEntry> Notes { get; set; } = new();
    }

    /// <summary>What one run did, for the log and the header cards.</summary>
    public sealed class RunReport
    {
        /// <summary>Sections the run covered.</summary>
        public int SectionsDone { get; set; }

        /// <summary>Roofs whose cut profile was traced.</summary>
        public int RoofsDrawn { get; set; }

        /// <summary>Walls that got a line.</summary>
        public int WallsDrawn { get; set; }

        /// <summary>Detail lines created.</summary>
        public int LinesCreated { get; set; }

        /// <summary>Lines left out because the view already had the same line.</summary>
        public int LinesSkippedExisting { get; set; }

        /// <summary>Elements the section did not actually cut, or whose geometry could not be read.</summary>
        public int ElementsSkipped { get; set; }

        /// <summary>Lines per section, keyed by view id, for the per-section status.</summary>
        public Dictionary<long, int> LinesByView { get; } = new();

        /// <summary>Log lines the run produced.</summary>
        public List<LogEntry> Details { get; } = new();
    }
}
