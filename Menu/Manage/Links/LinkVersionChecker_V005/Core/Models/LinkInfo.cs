using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker_V005.UI.Infrastructure;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V005.Core.Models
{
    public enum LinkKind { RVT, IFC, CAD, PointCloud, Image, Other }

    public enum Severity { Ok = 0, Info = 1, Warning = 2, Critical = 3 }

    /// <summary>
    /// One linked file as seen from one host. Holds raw FACTS gathered by the scanner;
    /// the rule engine turns facts into <see cref="Findings"/>.
    /// </summary>
    public class LinkInfo : ObservableObject
    {
        // ---------- identity ----------
        public string HostPath { get; set; } = "";
        public string HostName { get; set; } = "";
        public string Name { get; set; } = "";
        public LinkKind Kind { get; set; } = LinkKind.Other;
        public ElementId RefId { get; set; }

        // ---------- path ----------
        public string StoredPath { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string PathType { get; set; } = "";        // Relative / Absolute / Server
        public bool IsUnc { get; set; }
        public bool IsCloud { get; set; }

        // ---------- source (V004) ----------
        /// <summary>Where this link physically lives — ACC, Desktop Connector, network, local, Revit Server…</summary>
        public LinkSource Source { get; set; } = LinkSource.Unknown;
        /// <summary>Mapped drives only: the share the letter points at.</summary>
        public string ResolvedUnc { get; set; } = "";
        /// <summary>Cloud links: the path Revit shows in Manage Links.</summary>
        public string InSessionPath { get; set; } = "";
        public string CloudProjectId { get; set; } = "";
        public string CloudModelId { get; set; } = "";
        public string CloudRegion { get; set; } = "";
        /// <summary>True when this row came from the open model (not a link table read from disk).</summary>
        public bool InModel { get; set; }

        // ---------- sync (V005) ----------
        private AccSyncState _sync = AccSyncState.NotApplicable;
        /// <summary>Whether the copy we are using is the current one. See <see cref="AccSyncState"/>.</summary>
        public AccSyncState Sync { get => _sync; set { _sync = value; NotifySync(); } }
        /// <summary>Cloud links: the version string Revit last loaded, e.g. "v27". Empty when unknown.</summary>
        public string CloudVersion { get; set; } = "";
        /// <summary>Why the sync state is what it is — the raw status name, or the failure message.</summary>
        public string SyncNote { get; set; } = "";
        /// <summary>When the cloud status was last read. Null until the check runs.</summary>
        public DateTime? SyncCheckedAt { get; set; }

        /// <summary>Sets the sync state, its version and its note in one go, and refreshes the UI.</summary>
        public void SetSync(AccSyncState state, string version, string note)
        {
            CloudVersion = version ?? "";
            SyncNote = note ?? "";
            if (AccSyncInfo.IsResolved(state)) SyncCheckedAt = DateTime.Now;
            Sync = state;                                  // raises
        }

        // ---------- file ----------
        public bool Exists { get; set; }
        public double SizeMb { get; set; }

        // ---------- link state ----------
        public string Status { get; set; } = "";          // Loaded / Unloaded / NotFound / Imported ...
        public bool IsNested { get; set; }
        public string ParentName { get; set; } = "";
        public string Attachment { get; set; } = "";      // Attachment / Overlay / Unknown
        public int Instances { get; set; } = -1;          // in-model only
        public bool IsImport { get; set; }                // CAD imported (not linked)
        public int WorksetsOpen { get; set; } = -1;       // in-model, loaded workshared RVT links
        public int WorksetsTotal { get; set; } = -1;

        // ---------- RVT header ----------
        public string SavedYear { get; set; } = "";
        public bool HeaderRead { get; set; }
        public bool IsCurrentVersion { get; set; }
        public bool IsLaterVersion { get; set; }
        public bool IsWorkshared { get; set; }
        public bool IsCentral { get; set; }
        public bool IsLocal { get; set; }
        public string HeaderError { get; set; } = "";

        // ---------- IFC ----------
        public string IfcSchema { get; set; } = "";
        public string IfcPath { get; set; } = "";
        public string CachePath { get; set; } = "";
        public bool CacheExists { get; set; }
        public DateTime? CacheTimeUtc { get; set; }
        public DateTime? IfcTimeUtc { get; set; }
        public string CacheYear { get; set; } = "";
        public bool CacheIsCurrent { get; set; }
        public bool CacheFolderWritable { get; set; } = true;

        // ---------- other ----------
        public string CadVersion { get; set; } = "";

        // ---------- derived (rule engine) ----------
        public List<Finding> Findings { get; set; } = new List<Finding>();
        public Severity Severity { get; set; }
        public int EstSeconds { get; set; }
        public bool CountsInTotal { get; set; } = true;   // false for nested links found pre-open

        // ---------- measured (load-time log) ----------
        private double? _measuredSeconds;
        public double? MeasuredSeconds
        {
            get => _measuredSeconds;
            set { _measuredSeconds = value; Raise(); Raise(nameof(MeasuredText)); }
        }
        public DateTime? MeasuredAt { get; set; }

        // ---------- UI selection ----------
        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set { _isChecked = value; Raise(); } }

        // ================= display helpers =================
        public bool IsUnloaded =>
            Status.IndexOf("Unloaded", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsProblem => Severity >= Severity.Warning;

        public Finding Primary => Findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.EstSeconds).FirstOrDefault();

        public string CheckText => Primary?.Title ?? (Kind == LinkKind.IFC ? "Cache OK" : Kind == LinkKind.RVT ? "Current" : "OK");

        public string DisplayName => IsNested ? "↳ " + Name : Name;

        public string KindText =>
            Kind == LinkKind.PointCloud ? "Point Cloud" :
            Kind == LinkKind.Image ? "PDF/Image" :
            IsImport ? "CAD (import)" : Kind.ToString();

        public string SavedInText =>
            Kind == LinkKind.RVT ? (HeaderRead ? SavedYear : "—") :
            Kind == LinkKind.IFC ? (string.IsNullOrEmpty(IfcSchema) ? "IFC" : IfcSchema) :
            Kind == LinkKind.CAD ? (string.IsNullOrEmpty(CadVersion) ? "—" : CadVersion) :
            Kind == LinkKind.PointCloud ? "ReCap" : "—";

        public string WorksharedText =>
            Kind != LinkKind.RVT || !HeaderRead ? "—" :
            !IsWorkshared ? "No" : IsCentral ? "Central" : IsLocal ? "Local" : "Workshared";

        public string SizeText => SizeMb <= 0 ? "—" : SizeMb >= 1024 ? $"{SizeMb / 1024:0.0} GB" : $"{SizeMb:0} MB";

        public string PathText =>
            IsCloud ? "Cloud" :
            Source == LinkSource.RevitServer ? "Revit Server" :
            Source == LinkSource.MappedDrive ? PathType + " (mapped)" :
            IsUnc ? PathType + " (network)" : PathType;

        // ---------- source display (V004) ----------
        /// <summary>True only when there is a real file on disk to probe, size and version-check.</summary>
        public bool HasLocalFile => LinkSourceInfo.HasLocalFile(Source) && !IsImport;

        public string SourceText => LinkSourceInfo.Text(Source);

        public string SourceGroupText => LinkSourceInfo.Group(Source).ToString();

        public string SourceDetectedBy => LinkSourceInfo.DetectedBy(Source);

        /// <summary>Hex colour for the Source cell — group colour, amber for the ones worth a second look.</summary>
        public string SourceColour => LinkSourceInfo.Colour(Source);

        public string SourceTip =>
            SourceText + "  —  detected by " + SourceDetectedBy +
            (string.IsNullOrEmpty(ResolvedUnc) ? "" : "\n" + PathRoot + " → " + ResolvedUnc);

        public string PathRoot
        {
            get
            {
                try { return System.IO.Path.GetPathRoot(FullPath ?? "") ?? ""; }
                catch { return ""; }
            }
        }

        // ---------- sync display (V005) ----------
        public string SyncText => AccSyncInfo.Text(Sync);

        public string SyncGlyph => AccSyncInfo.Glyph(Sync);

        public string SyncColour => AccSyncInfo.Colour(Sync);

        public string SyncDetectedBy => AccSyncInfo.DetectedBy(Sync);

        /// <summary>Radio-dot state for the ACC Docs column (shared <c>AccStatusDot</c>).</summary>
        public Revit26_Plugin_Revised.Shared.Services.AccLinkState AccDot => AccSyncInfo.ToDot(Sync);

        /// <summary>Sort key for the Sync column — problems first, blanks last.</summary>
        public int SyncSortOrder => AccSyncInfo.SortOrder(Sync);

        public bool IsSyncProblem => AccSyncInfo.IsProblem(Sync);

        /// <summary>What the grid cell says: the state, plus the version when we have one.</summary>
        public string SyncCellText =>
            Sync == AccSyncState.NotApplicable ? "" :
            string.IsNullOrEmpty(CloudVersion) ? SyncText : SyncText + " " + CloudVersion;

        public string SyncVersionText => string.IsNullOrEmpty(CloudVersion) ? "\u2014" : CloudVersion;

        public string SyncCheckedText =>
            Sync == AccSyncState.NotApplicable ? "\u2014" :
            Sync == AccSyncState.NotChecked ? "not yet \u2014 press Check Cloud Status" :
            Sync == AccSyncState.NotAvailableInMode ? "not available in this mode" :
            SyncCheckedAt.HasValue ? SyncCheckedAt.Value.ToString("dd-MMM-yy HH:mm") : "\u2014";

        public string SyncTip =>
            Sync == AccSyncState.NotApplicable
                ? "Not an ACC or cloud-sync link \u2014 there is no sync state to report."
                : SyncText + "  \u2014  " + SyncDetectedBy +
                  (string.IsNullOrEmpty(CloudVersion) ? "" : "\nVersion loaded: " + CloudVersion) +
                  (string.IsNullOrEmpty(SyncNote) ? "" : "\n" + SyncNote);

        public string CloudIdText =>
            string.IsNullOrEmpty(CloudProjectId) && string.IsNullOrEmpty(CloudModelId) ? "—" :
            $"project {Short(CloudProjectId)} · model {Short(CloudModelId)}" +
            (string.IsNullOrEmpty(CloudRegion) ? "" : " · " + CloudRegion);

        private static string Short(string id) =>
            string.IsNullOrEmpty(id) ? "—" : id.Length <= 12 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 4);

        public string EstText => EstSeconds <= 0 ? "—" : CountsInTotal ? $"+{EstSeconds}s" : $"(+{EstSeconds}s)";

        public string MeasuredText => MeasuredSeconds.HasValue ? $"{MeasuredSeconds.Value:0.0}s" : "—";

        public string InstancesText => Instances < 0 ? "—" : Instances.ToString();

        public string WorksetsText => WorksetsTotal < 0 ? "—" : $"{WorksetsOpen} of {WorksetsTotal} open";

        public string CacheText =>
            Kind != LinkKind.IFC ? "—" :
            !CacheExists ? (CacheFolderWritable ? "Missing" : "Missing (folder read-only)") :
            $"Revit {CacheYear}, {CacheTimeUtc?.ToLocalTime():dd-MMM-yy HH:mm}";

        public string Recommendation =>
            Findings.Count == 0 ? "No issues found." :
            string.Join("\n\n", Findings.OrderByDescending(f => f.Severity).Select(f => "• " + f.Message));

        // ================= action eligibility =================
        public bool CanUpgrade =>
            Kind == LinkKind.RVT && HasLocalFile && Exists && HeaderRead && !IsCurrentVersion && !IsLaterVersion && !IsWorkshared && !IsCloud;

        public bool CanRebuildIfc =>
            Kind == LinkKind.IFC && HasLocalFile && !IsNested && (!CacheExists || !CacheIsCurrent || CacheIsOlderThanIfc);

        public bool CacheIsOlderThanIfc =>
            CacheTimeUtc.HasValue && IfcTimeUtc.HasValue && CacheTimeUtc.Value < IfcTimeUtc.Value;

        /// <summary>
        /// In-Model: Revit can unload cloud links too, so any non-nested, non-imported row qualifies.
        /// Pre-Open / Folder: the host's link table is rewritten, which only works for file-based links.
        /// </summary>
        public bool CanSkipLoad => !IsNested && !IsImport && RefId != null && (InModel || HasLocalFile);

        public void NotifyDerived()
        {
            Raise(nameof(Severity)); Raise(nameof(CheckText)); Raise(nameof(EstText));
            Raise(nameof(Recommendation)); Raise(nameof(IsProblem));
            Raise(nameof(SourceText)); Raise(nameof(SourceColour)); Raise(nameof(SourceTip));
            NotifySync();
        }

        /// <summary>Refreshes every Sync-derived binding after the state changes.</summary>
        public void NotifySync()
        {
            Raise(nameof(Sync)); Raise(nameof(SyncText)); Raise(nameof(SyncGlyph)); Raise(nameof(AccDot));
            Raise(nameof(SyncColour)); Raise(nameof(SyncCellText)); Raise(nameof(SyncTip));
            Raise(nameof(SyncVersionText)); Raise(nameof(SyncCheckedText));
            Raise(nameof(SyncSortOrder)); Raise(nameof(SyncDetectedBy)); Raise(nameof(IsSyncProblem));
        }
    }
}
