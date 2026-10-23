using System;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models
{
    /// <summary>Whether a Revit link is loaded in the open model.</summary>
    public enum ModelLoadState
    {
        /// <summary>Loaded and visible in the model.</summary>
        Loaded,
        /// <summary>Unloaded for everyone.</summary>
        Unloaded,
        /// <summary>Unloaded for the current user only (workshared model).</summary>
        UnloadedForMe,
        /// <summary>The link's workset is closed, so Revit has not loaded it.</summary>
        InClosedWorkset,
        /// <summary>Revit could not find the file at its saved path.</summary>
        NotFound,
        /// <summary>Any other status Revit reports.</summary>
        Other
    }

    /// <summary>Where a link's saved path points.  [V007]</summary>
    public enum LinkSourceKind
    {
        /// <summary>A file under a Desktop Connector "ACCDocs" folder.</summary>
        DesktopConnector,
        /// <summary>A cloud model reference: <c>Autodesk Docs://…</c> or <c>BIM 360://…</c>.</summary>
        Cloud,
        /// <summary>Anything else: network share, mapped or local drive, Revit Server.</summary>
        Other
    }

    /// <summary>
    /// What Windows / Desktop Connector reports for the link file on this PC.
    /// Read from the Windows Cloud Files placeholder state, the same state behind
    /// Explorer's status icons.
    /// </summary>
    public enum DcFileState
    {
        /// <summary>Full green: "Always keep on this device".</summary>
        AlwaysKept,
        /// <summary>Green outline: downloaded, but Windows may free it up.</summary>
        AvailableLocally,
        /// <summary>Cloud icon: only a placeholder is on this PC.</summary>
        OnlineOnly,
        /// <summary>Blue arrows: Desktop Connector has not finished syncing this file.</summary>
        SyncPending,
        /// <summary>The file is under the Desktop Connector folder but is not a cloud file.</summary>
        NotCloudFile,
        /// <summary>No file at the link's path.</summary>
        Missing,
        /// <summary>Windows would not say (old Windows, access denied).</summary>
        Unknown,
        /// <summary>A cloud link: Revit opens it from ACC, there is no file to sync.  [V007]</summary>
        CloudModel,
        /// <summary>A link that is not in ACC at all.  [V007]</summary>
        NotAcc
    }

    /// <summary>Whether the copy loaded in the model is the latest one.</summary>
    public enum LatestState
    {
        /// <summary>The link is not loaded, so there is nothing to compare.</summary>
        NotLoaded,
        /// <summary>Loaded copy is the latest (Desktop Connector file, or ACC says Current).</summary>
        UpToDate,
        /// <summary>The file on this PC is a newer save than the copy loaded in the model.</summary>
        ReloadNeeded,
        /// <summary>Desktop Connector is still syncing; the answer may change.</summary>
        SyncPending,
        /// <summary>Could not compare (online-only file, unreadable header, ACC did not answer).</summary>
        Unknown,
        /// <summary>Cloud link: a newer version is published in ACC.  [V007]</summary>
        NewerInAcc,
        /// <summary>Cloud link: not asked yet; press Check cloud versions.  [V007]</summary>
        NotChecked,
        /// <summary>Not an ACC link, so there is no ACC version.  [V007]</summary>
        NotApplicable
    }

    /// <summary>
    /// One top-level Revit link: from Desktop Connector, a cloud link, or (shown on request)
    /// any other path.
    /// </summary>
    public sealed class LinkStatusRow : ObservableObject
    {
        private string _folderShown = "";
        private LatestState _latest;
        private string _latestReason = "";

        /// <summary>The link's RevitLinkType id.</summary>
        public ElementId TypeId { get; init; }

        /// <summary>File name, e.g. ARC_Building-A.rvt.</summary>
        public string Name { get; init; } = "";

        /// <summary>Full path as Revit shows it: a path on this PC, or <c>Autodesk Docs://…</c> for a cloud link.</summary>
        public string FullPath { get; init; } = "";

        /// <summary>Where the link comes from.  [V007]</summary>
        public LinkSourceKind Source { get; init; }

        /// <summary>ACC account (hub) name, when the path names one.  [V007]</summary>
        public string AccAccount { get; init; } = "";

        /// <summary>ACC project name, or empty for links that are not in ACC.  [V007]</summary>
        public string AccProject { get; init; } = "";

        /// <summary>True / false when the link is / is not in the open model's ACC project; null when that cannot be told.  [V007]</summary>
        public bool? SameProject { get; init; }

        /// <summary>Cloud project GUID of a cloud link, when Revit reports it.  [V007]</summary>
        public string CloudProjectId { get; init; } = "";

        /// <summary>Cloud model GUID of a cloud link, when Revit reports it.  [V007]</summary>
        public string CloudModelId { get; init; } = "";

        /// <summary>Folder inside the ACC project ("02_ARC\Models"), or the parent folder when no project root is found.</summary>
        public string AccFolder { get; init; } = "";

        /// <summary><see cref="AccFolder"/> shortened by the window's Folder setting; the grid shows this.</summary>
        public string FolderShown
        {
            get => _folderShown;
            set => SetProperty(ref _folderShown, value ?? "");
        }

        /// <summary>Load state in the open model.</summary>
        public ModelLoadState Model { get; init; }

        /// <summary>Desktop Connector state of the file on this PC.</summary>
        public DcFileState Dc { get; init; }

        /// <summary>Whether the loaded copy is the latest. Cloud links change it after Check cloud versions.</summary>
        public LatestState Latest
        {
            get => _latest;
            set
            {
                if (!SetProperty(ref _latest, value)) return;
                OnPropertyChanged(nameof(LatestText));
                OnPropertyChanged(nameof(IsProblem));
            }
        }

        /// <summary>Why <see cref="Latest"/> has its value, shown in the detail panel.</summary>
        public string LatestReason
        {
            get => _latestReason;
            set => SetProperty(ref _latestReason, value ?? "");
        }

        /// <summary>Local file's last-modified time, or null when there is no file.</summary>
        public DateTime? FileDate { get; init; }

        /// <summary>File size in MB (logical size; an online-only file still reports its full size).</summary>
        public double? SizeMb { get; init; }

        /// <summary>Number of placed instances of this link in the model.</summary>
        public int Instances { get; init; }

        /// <summary>Set when the link type is owned by another user (worksharing).</summary>
        public string OwnedBy { get; init; } = "";

        // ---- display ------------------------------------------------------------------------

        /// <summary>Text for the "In model" column.</summary>
        public string ModelText => Model switch
        {
            ModelLoadState.Loaded => "Loaded",
            ModelLoadState.Unloaded => "Unloaded",
            ModelLoadState.UnloadedForMe => "Unloaded (for me)",
            ModelLoadState.InClosedWorkset => "Closed workset",
            ModelLoadState.NotFound => "Not found",
            _ => "Other"
        };

        /// <summary>Text for the "Source" column.  [V007]</summary>
        public string SourceText => Source switch
        {
            LinkSourceKind.DesktopConnector => "Desktop Connector",
            LinkSourceKind.Cloud => "Cloud link",
            _ => "Other path"
        };

        /// <summary>Text for the "On this PC" column.</summary>
        public string DcText => Dc switch
        {
            DcFileState.AlwaysKept => "Always kept",
            DcFileState.AvailableLocally => "Available locally",
            DcFileState.OnlineOnly => "Online only",
            DcFileState.SyncPending => "Sync pending",
            DcFileState.NotCloudFile => "Not a cloud file",
            DcFileState.Missing => "Not on this PC",
            DcFileState.CloudModel => "Cloud model",
            DcFileState.NotAcc => "Not ACC",
            _ => "Unknown"
        };

        /// <summary>Text for the "Latest from ACC?" column.</summary>
        public string LatestText => Latest switch
        {
            LatestState.UpToDate => "Yes",
            LatestState.ReloadNeeded => "Reload needed",
            LatestState.SyncPending => "Sync pending",
            LatestState.NewerInAcc => "Newer in ACC",
            LatestState.NotChecked => "Not checked",
            LatestState.NotLoaded or LatestState.NotApplicable => "—",
            _ => "Unknown"
        };

        /// <summary>Text for the "ACC project" column: the name, flagged when it is another project.  [V007]</summary>
        public string ProjectText =>
            AccProject.Length == 0 ? "—" : SameProject == false ? AccProject + " · other project" : AccProject;

        /// <summary>"Yes", "No" or "" for the export's "Same ACC project" column.  [V007]</summary>
        public string SameProjectText => SameProject switch { true => "Yes", false => "No", _ => "" };

        /// <summary>One line explaining the Desktop Connector state, for the detail panel.</summary>
        public string DcExplain => Dc switch
        {
            DcFileState.AlwaysKept => "Always kept on this device — Desktop Connector keeps the latest version downloaded.",
            DcFileState.AvailableLocally => "Downloaded, but not set to always keep — Windows may turn it back into an online-only file.",
            DcFileState.OnlineOnly => "Only a placeholder is on this PC — Revit downloads the file the next time the link loads.",
            DcFileState.SyncPending => "Desktop Connector has not finished syncing this file.",
            DcFileState.NotCloudFile => "The file is in the Desktop Connector folder but Windows does not report it as a cloud file.",
            DcFileState.Missing => "There is no file at this path on this PC.",
            DcFileState.CloudModel => "A cloud link — Revit opens it straight from ACC, so there is no Desktop Connector file to sync.",
            DcFileState.NotAcc => "Not an ACC link — the file is on a network share or this PC.",
            _ => "Windows did not report a sync state for this file."
        };

        /// <summary>Account, project and ids of a cloud link for the detail panel; empty for other links.  [V007]</summary>
        public string CloudDetail
        {
            get
            {
                if (Source != LinkSourceKind.Cloud) return "";
                string where = SameProject switch
                {
                    true => " (same ACC project as this model)",
                    false => " (a different ACC project from this model)",
                    _ => ""
                };
                string text = (AccAccount.Length > 0 ? $"Account: {AccAccount} · " : "") +
                              $"ACC project: {(AccProject.Length > 0 ? AccProject : "(not in the path)")}{where}";
                if (CloudProjectId.Length > 0 || CloudModelId.Length > 0)
                    text += $" · Project id {(CloudProjectId.Length > 0 ? CloudProjectId : "—")} / model id {(CloudModelId.Length > 0 ? CloudModelId : "—")}";
                return text;
            }
        }

        /// <summary>Local file date for the grid.</summary>
        public string FileDateText => FileDate?.ToString("dd-MM-yyyy HH:mm") ?? "—";

        /// <summary>Size for the grid.</summary>
        public string SizeText => SizeMb.HasValue ? $"{SizeMb.Value:0} MB" : "—";

        /// <summary>Sort key for "On this PC": problems first.</summary>
        public int DcSort => Dc switch
        {
            DcFileState.Missing => 0,
            DcFileState.SyncPending => 1,
            DcFileState.OnlineOnly => 2,
            DcFileState.NotCloudFile => 3,
            DcFileState.Unknown => 4,
            DcFileState.AvailableLocally => 5,
            DcFileState.AlwaysKept => 6,
            DcFileState.CloudModel => 7,
            _ => 8
        };

        /// <summary>True for a link from Desktop Connector (has a file to pin).  [V007]</summary>
        public bool IsDesktopConnector => Source == LinkSourceKind.DesktopConnector;

        /// <summary>True for a cloud link (no file or folder on this PC).  [V007]</summary>
        public bool IsCloud => Source == LinkSourceKind.Cloud;

        /// <summary>True for loaded links in a normal state.</summary>
        public bool IsLoaded => Model == ModelLoadState.Loaded;

        /// <summary>True for every not-loaded state except "not found".</summary>
        public bool IsUnloaded => Model is ModelLoadState.Unloaded or ModelLoadState.UnloadedForMe
                                         or ModelLoadState.InClosedWorkset or ModelLoadState.Other;

        /// <summary>True when Revit cannot find the file.</summary>
        public bool IsNotFound => Model == ModelLoadState.NotFound;

        /// <summary>True when something needs attention: not loaded, not kept on this PC, or not the latest.</summary>
        public bool IsProblem =>
            !IsLoaded ||
            Dc is DcFileState.OnlineOnly or DcFileState.SyncPending or DcFileState.Missing ||
            Latest is LatestState.ReloadNeeded or LatestState.SyncPending or LatestState.NewerInAcc;
    }
}
