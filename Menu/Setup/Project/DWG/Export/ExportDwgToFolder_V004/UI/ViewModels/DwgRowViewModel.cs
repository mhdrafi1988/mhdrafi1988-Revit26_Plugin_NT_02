using System.IO;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels
{
    /// <summary>Status of one scanned DWG row.</summary>
    public enum DwgRowStatus
    {
        /// <summary>Checked at load time: can be exported.</summary>
        Ready,

        /// <summary>Checked at load time: export will fail (source not found / no host view).</summary>
        Issue,

        /// <summary>Ticked and waiting in the current export.</summary>
        Queued,

        /// <summary>Being exported now.</summary>
        Exporting,

        /// <summary>Written to the output folder.</summary>
        Exported,

        /// <summary>Not written: the file already exists and Overwrite is off.</summary>
        Skipped,

        /// <summary>Export failed (see the note / log).</summary>
        Failed,

        /// <summary>Exported, then removed from the model after the user confirmed the review.</summary>
        Deleted,

        /// <summary>
        /// Exported, then replaced with an import of the same file after the user confirmed the
        /// review. NEW in V004.
        /// </summary>
        Converted,

        /// <summary>
        /// Exported, then replaced with a CAD link to the exported file after the user confirmed the
        /// review. NEW in V004.
        /// </summary>
        Relinked
    }

    /// <summary>
    /// One row of the scanned-DWG grid. Starts unticked. The constructor pre-checks whether the
    /// DWG can be exported (linked source file present / imported DWG has a host view).
    /// </summary>
    public partial class DwgRowViewModel : ObservableObject
    {
        /// <summary>Wraps a scanned DWG and pre-checks it.</summary>
        public DwgRowViewModel(DwgInstanceInfo info)
        {
            Info = info;
            Name = string.IsNullOrWhiteSpace(info.SymbolName) ? $"CAD_{info.ElementId?.Value}" : info.SymbolName;
            SourceGroup = info.SourceType == DwgSourceType.Link ? "Link" : "Import";
            SourceOrder = (int)info.SourceType;
            ViewGroup = info.ViewCategory switch
            {
                DwgViewCategory.Plan => "Plan",
                DwgViewCategory.Section => "Section",
                DwgViewCategory.Drafting => "Drafting",
                _ => "No Views"
            };
            ViewOrder = (int)info.ViewCategory;
            HostView = string.IsNullOrWhiteSpace(info.ViewName) ? "–" : info.ViewName;
            Sheet = info.SheetInfo ?? "Not on Sheet";
            OutputFileName = info.OutputFileName;
            ElementIdValue = info.ElementId?.Value ?? 0;

            PreCheck();
        }

        /// <summary>The scanned DWG.</summary>
        public DwgInstanceInfo Info { get; }

        /// <summary>Import symbol name.</summary>
        public string Name { get; }

        /// <summary>"Link" or "Import" — first grouping level.</summary>
        public string SourceGroup { get; }

        /// <summary>Sort key keeping Link before Import.</summary>
        public int SourceOrder { get; }

        /// <summary>"Plan" / "Section" / "Drafting" / "No Views" — second grouping level.</summary>
        public string ViewGroup { get; }

        /// <summary>Sort key keeping Plan → Section → Drafting → No Views.</summary>
        public int ViewOrder { get; }

        /// <summary>Host view name, or "–".</summary>
        public string HostView { get; }

        /// <summary>"A101 - Sheet Name" or "Not on Sheet".</summary>
        public string Sheet { get; }

        /// <summary>Output file name (no folder).</summary>
        public string OutputFileName { get; }

        /// <summary>Revit element ID.</summary>
        public long ElementIdValue { get; }

        /// <summary>Ticked for export. Every row starts unticked.</summary>
        [ObservableProperty] private bool isSelected;

        /// <summary>Row status.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private DwgRowStatus status;

        /// <summary>Detail for the status (reason for an issue / failure, or the written path).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string statusNote = string.Empty;

        /// <summary>Status cell text.</summary>
        public string StatusText => Status switch
        {
            DwgRowStatus.Ready => "✔ Ready",
            DwgRowStatus.Issue => "⚠ " + StatusNote,
            DwgRowStatus.Queued => "◌ Queued",
            DwgRowStatus.Exporting => "⟳ Exporting…",
            DwgRowStatus.Exported => "✔ Exported",
            DwgRowStatus.Skipped => "◌ Skipped (exists)",
            DwgRowStatus.Failed => "✘ Failed",
            DwgRowStatus.Deleted => "🗑 Deleted from model",
            DwgRowStatus.Converted => "⇄ Converted to import",
            DwgRowStatus.Relinked => "🔗 Linked to exported file",
            _ => string.Empty
        };

        // ── Path surface (V004) ─────────────────────────────────────────────

        /// <summary>
        /// Where this row's DWG will be (or was) written, in full. Pushed in by the ViewModel via
        /// <see cref="SetDestination"/> rather than computed here: the destination depends on the
        /// output folder and the "subfolders by view type" option, and a row has no reference back
        /// to the parent ViewModel to read them from.
        /// </summary>
        [ObservableProperty] private string destinationPath = string.Empty;

        /// <summary>
        /// Where the DWG comes from: the link's source file, or a plain statement for an import,
        /// which has no external file — its geometry only exists inside the model.
        /// </summary>
        public string SourceDescription =>
            Info.SourceType == DwgSourceType.Link
                ? (string.IsNullOrWhiteSpace(Info.LinkedSourcePath)
                    ? "Source path unresolved"
                    : Info.LinkedSourcePath)
                : "Embedded in the model (import — no external file)";

        /// <summary>True when <see cref="SourceDescription"/> is an actual path, for mono-spacing it.</summary>
        public bool HasSourcePath =>
            Info.SourceType == DwgSourceType.Link && !string.IsNullOrWhiteSpace(Info.LinkedSourcePath);

        /// <summary>Sets the full destination path shown in the hover tooltip.</summary>
        public void SetDestination(string fullPath) => DestinationPath = fullPath ?? string.Empty;

        /// <summary>
        /// Layers this CAD instance brings into the model, as a sortable number. 0 when it could not
        /// be read, so an unreadable row sorts with the suspicious ones rather than the healthy ones.
        /// </summary>
        public int LayerCount => Info.LayerCount < 0 ? 0 : Info.LayerCount;

        /// <summary>
        /// Layer count for the grid cell. "?" when it could not be read; "1" is itself the useful
        /// signal — the DWG was already flattened to one layer when it came into the model, so no
        /// export setting will get its layers back.
        /// </summary>
        public string LayerText => Info.LayerCount < 0 ? "?" : Info.LayerCount.ToString();

        /// <summary>True when the instance has one layer or fewer, which is worth flagging.</summary>
        public bool LayersSuspicious => Info.LayerCount >= 0 && Info.LayerCount <= 1;

        /// <summary>True when the load-time check found a problem.</summary>
        public bool HasIssue { get; private set; }

        /// <summary>Lower-case text searched by the grid filter box.</summary>
        public string SearchText =>
            $"{Name} {SourceGroup} {ViewGroup} {HostView} {Sheet} {OutputFileName} {ElementIdValue} {StatusText}".ToLowerInvariant();

        /// <summary>Sets status and note together.</summary>
        public void SetStatus(DwgRowStatus newStatus, string note = "")
        {
            StatusNote = note ?? string.Empty;
            Status = newStatus;
        }

        /// <summary>Re-runs the load-time check (used to restore the status before a new export).</summary>
        public void PreCheck()
        {
            string issue = null;

            if (Info.SourceType == DwgSourceType.Link)
            {
                if (string.IsNullOrEmpty(Info.LinkedSourcePath))
                    issue = "Source path unresolved";
                else
                {
                    bool exists;
                    try { exists = File.Exists(Info.LinkedSourcePath); }
                    catch { exists = false; }
                    if (!exists) issue = "Source not found";
                }
            }
            else if (Info.OwnerViewId == null || Info.OwnerViewId == ElementId.InvalidElementId)
            {
                issue = "No host view";
            }

            HasIssue = issue != null;
            SetStatus(HasIssue ? DwgRowStatus.Issue : DwgRowStatus.Ready, issue ?? string.Empty);
        }
    }
}
