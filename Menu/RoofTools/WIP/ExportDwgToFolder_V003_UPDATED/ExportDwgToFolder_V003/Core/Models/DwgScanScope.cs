using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Models
{
    /// <summary>
    /// What a scan collects: which source types (link / import) and which host-view
    /// categories. Chosen by the user before each scan; never persisted (every window
    /// opens with nothing in scope).
    /// </summary>
    public sealed class DwgScanScope
    {
        /// <summary>Collect CAD links.</summary>
        public bool Linked { get; set; }

        /// <summary>Collect CAD imports.</summary>
        public bool Imported { get; set; }

        /// <summary>Collect DWGs hosted in plan views.</summary>
        public bool Plan { get; set; }

        /// <summary>Collect DWGs hosted in section / elevation views.</summary>
        public bool Section { get; set; }

        /// <summary>Collect DWGs hosted in drafting views.</summary>
        public bool Drafting { get; set; }

        /// <summary>Collect DWGs with no resolvable host view.</summary>
        public bool NoViews { get; set; }

        /// <summary>True when at least one source type is in scope.</summary>
        public bool HasSource => Linked || Imported;

        /// <summary>True when at least one view category is in scope.</summary>
        public bool HasViewType => Plan || Section || Drafting || NoViews;

        /// <summary>True when a scan can run (at least one source and one view category).</summary>
        public bool IsValid => HasSource && HasViewType;

        /// <summary>Whether DWGs of this source type are collected.</summary>
        public bool Includes(DwgSourceType source)
            => source == DwgSourceType.Link ? Linked : Imported;

        /// <summary>Whether DWGs hosted in this view category are collected.</summary>
        public bool Includes(DwgViewCategory category) => category switch
        {
            DwgViewCategory.Plan => Plan,
            DwgViewCategory.Section => Section,
            DwgViewCategory.Drafting => Drafting,
            _ => NoViews
        };

        /// <summary>One-line description for the log, e.g. "Sources: linked | Views: plan, section".</summary>
        public override string ToString()
        {
            var sources = new List<string>();
            if (Linked) sources.Add("linked");
            if (Imported) sources.Add("imported");

            var views = new List<string>();
            if (Plan) views.Add("plan");
            if (Section) views.Add("section");
            if (Drafting) views.Add("drafting");
            if (NoViews) views.Add("no views");

            return $"Sources: {(sources.Count > 0 ? string.Join(", ", sources) : "none")} | " +
                   $"Views: {(views.Count > 0 ? string.Join(", ", views) : "none")}";
        }
    }
}
