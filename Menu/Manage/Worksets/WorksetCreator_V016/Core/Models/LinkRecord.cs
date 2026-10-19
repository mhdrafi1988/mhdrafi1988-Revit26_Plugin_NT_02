// ==============================================
// File: LinkRecord.cs
// Layer: Core/Models  (no Revit API — CLAUDE.md)
// NEW in Workset Creator V016.
//
// One scanned RevitLinkType, reduced to plain data. The service produces these
// in a single pass (WorksetService.ScanLinks) and the ViewModel builds its grid
// rows from them, so the model is read once per load instead of once per link
// per question — V015 ran a FilteredWorksetCollector inside a per-instance
// loop, which is O(links x instances x worksets) on a model like the one this
// version was written against (59 links).
//
// The link type's ElementId travels as a plain int (ElementId.IntegerValue) so
// this layer keeps its no-Revit-API rule; the service turns it back into an
// ElementId at the boundary.
// ==============================================

using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetCreator.V016.Core.Models
{
    public sealed class LinkRecord
    {
        /// <summary>ElementId.IntegerValue of the RevitLinkType. The identity of a row.</summary>
        public int TypeId { get; set; }

        /// <summary>
        /// The file name with a trailing ".rvt" removed and nothing else. This is the
        /// matcher's anchor and the value {name} resolves to — NOT what the grid shows.
        /// For an IFC shadow file "Z3.ifc.rvt" it is "Z3.ifc": the .ifc stays, because
        /// only ".rvt" comes off.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// File name as Revit holds it, extension included — "WAMI-AR-Z1-Tower.rvt",
        /// "Z3_IFC_21-08-2026.ifc.rvt". What the Linked file column shows, so a double
        /// extension is visible rather than silently trimmed.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Revit linked an .ifc through a generated "&lt;file&gt;.ifc.rvt" shadow model.
        /// Presentation only: such a file ends in .rvt, so it is always listed and always
        /// gets a workset. The flag tints the row and feeds the report count; it never
        /// changes what the run does.
        /// </summary>
        public bool IsIfc { get; set; }

        public LinkLoadState LoadState { get; set; } = LinkLoadState.Loaded;

        /// <summary>ACC Docs status of the link type, shown as the green radio dot in the ACC column.</summary>
        public AccLinkState AccState { get; set; } = AccLinkState.NotAcc;

        /// <summary>RevitLinkInstance elements of this type found in the host, loaded or not.</summary>
        public int InstanceCount { get; set; }

        /// <summary>
        /// Distinct workset names across those instances. Empty when none of them
        /// has a readable workset parameter.
        /// </summary>
        public List<string> WorksetNames { get; } = new();

        /// <summary>Instances whose ELEM_PARTITION_PARAM was null or unset.</summary>
        public int UnassignedInstances { get; set; }

        public bool HasInstances => InstanceCount > 0;

        public bool IsUnloaded => LoadState == LinkLoadState.Unloaded;

        /// <summary>"None" / the single workset / "MIXED" — the Current workset column.</summary>
        public string CurrentWorksetName =>
            WorksetNames.Count == 0 ? "None" :
            WorksetNames.Count == 1 ? WorksetNames[0] : "MIXED";

        public bool IsMixedWorkset => WorksetNames.Count > 1;

        /// <summary>
        /// True when every instance of this link already sits in
        /// <paramref name="worksetName"/> — i.e. Grid 1, nothing to do.
        /// Pure: answered from the scan, with no further document reads.
        /// </summary>
        public bool IsFullyAssignedTo(string worksetName)
        {
            if (!HasInstances)              return false;
            if (UnassignedInstances > 0)    return false;
            if (WorksetNames.Count != 1)    return false;
            return WorksetNames[0].Equals(worksetName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Result of one scan: the links this tool will act on, plus what it left
    /// out and why. The exclusion counts are shown in the window and written
    /// into the run report, so a link missing from the list is explained rather
    /// than simply absent.
    /// </summary>
    public sealed class LinkScan
    {
        public List<LinkRecord> Links { get; } = new();

        /// <summary>
        /// Bare .ifc links left out because they are not .rvt files. A Revit-linked IFC
        /// is a ".ifc.rvt" shadow model and is never counted here — it is listed.
        /// </summary>
        public int ExcludedIfc { get; set; }

        /// <summary>Links left out because their file is neither .rvt nor .ifc.</summary>
        public int ExcludedNonRevit { get; set; }

        /// <summary>
        /// Listed links that came in from IFC, i.e. a ".ifc.rvt" shadow file. These get
        /// worksets like any other Revit link; the count exists so the report can say so.
        /// </summary>
        public int IncludedIfc { get; set; }

        /// <summary>Nested links — their workset follows the parent link, so they are not ours to set.</summary>
        public int ExcludedNested { get; set; }

        public int ExcludedTotal => ExcludedIfc + ExcludedNested + ExcludedNonRevit;

        /// <summary>
        /// The toolbar note. Reports what was hidden and, when the user has cleared the
        /// IFC box, that IFC links are in the list — so the count in the window is
        /// always explained either way. Empty only when there is nothing to say.
        /// </summary>
        public string ExclusionText()
        {
            var hidden = new List<string>();
            if (ExcludedIfc      > 0) hidden.Add($"{ExcludedIfc} bare .ifc");
            if (ExcludedNonRevit > 0) hidden.Add($"{ExcludedNonRevit} non-Revit");
            if (ExcludedNested   > 0) hidden.Add($"{ExcludedNested} nested");

            string hiddenText = hidden.Count > 0
                ? $"{string.Join(", ", hidden)} link(s) not listed"
                : string.Empty;

            string includedText = IncludedIfc > 0
                ? $"{IncludedIfc} IFC (.ifc.rvt) link(s) included"
                : string.Empty;

            if (includedText.Length == 0) return hiddenText;
            if (hiddenText.Length  == 0) return includedText;
            return $"{includedText} · {hiddenText}";
        }

        /// <summary>True when the note has anything to show.</summary>
        public bool HasExclusionText => ExclusionText().Length > 0;

        public bool AnyLoadedFailure => Links.Any(l => l.LoadState == LinkLoadState.NotFound);
    }
}
