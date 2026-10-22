using System;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models
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
        Unknown
    }

    /// <summary>Whether the copy loaded in the model is the latest one Desktop Connector has.</summary>
    public enum LatestState
    {
        /// <summary>The link is not loaded, so there is nothing to compare.</summary>
        NotLoaded,
        /// <summary>Loaded copy matches the file on this PC, and Desktop Connector is in sync.</summary>
        UpToDate,
        /// <summary>The file on this PC is a newer save than the copy loaded in the model.</summary>
        ReloadNeeded,
        /// <summary>Desktop Connector is still syncing; the answer may change.</summary>
        SyncPending,
        /// <summary>Could not compare (online-only file, unreadable header, not a cloud file).</summary>
        Unknown
    }

    /// <summary>One top-level Revit link that lives under the Desktop Connector folder.</summary>
    public sealed class LinkStatusRow
    {
        /// <summary>The link's RevitLinkType id.</summary>
        public ElementId TypeId { get; init; }

        /// <summary>File name, e.g. ARC_Building-A.rvt.</summary>
        public string Name { get; init; } = "";

        /// <summary>Full path on this PC.</summary>
        public string FullPath { get; init; } = "";

        /// <summary>Folder inside the ACC project ("02_ARC\Models"), or the parent folder when no project root is found.</summary>
        public string AccFolder { get; init; } = "";

        /// <summary>Load state in the open model.</summary>
        public ModelLoadState Model { get; init; }

        /// <summary>Desktop Connector state of the file on this PC.</summary>
        public DcFileState Dc { get; init; }

        /// <summary>Whether the loaded copy is the latest.</summary>
        public LatestState Latest { get; init; }

        /// <summary>Why <see cref="Latest"/> has its value, shown in the detail panel.</summary>
        public string LatestReason { get; init; } = "";

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

        /// <summary>Text for the "On this PC" column.</summary>
        public string DcText => Dc switch
        {
            DcFileState.AlwaysKept => "Always kept",
            DcFileState.AvailableLocally => "Available locally",
            DcFileState.OnlineOnly => "Online only",
            DcFileState.SyncPending => "Sync pending",
            DcFileState.NotCloudFile => "Not a cloud file",
            DcFileState.Missing => "Not on this PC",
            _ => "Unknown"
        };

        /// <summary>Text for the "Latest from ACC?" column.</summary>
        public string LatestText => Latest switch
        {
            LatestState.UpToDate => "Yes",
            LatestState.ReloadNeeded => "Reload needed",
            LatestState.SyncPending => "Sync pending",
            LatestState.NotLoaded => "—",
            _ => "Unknown"
        };

        /// <summary>One line explaining the Desktop Connector state, for the detail panel.</summary>
        public string DcExplain => Dc switch
        {
            DcFileState.AlwaysKept => "Always kept on this device — Desktop Connector keeps the latest version downloaded.",
            DcFileState.AvailableLocally => "Downloaded, but not set to always keep — Windows may turn it back into an online-only file.",
            DcFileState.OnlineOnly => "Only a placeholder is on this PC — Revit downloads the file the next time the link loads.",
            DcFileState.SyncPending => "Desktop Connector has not finished syncing this file.",
            DcFileState.NotCloudFile => "The file is in the Desktop Connector folder but Windows does not report it as a cloud file.",
            DcFileState.Missing => "There is no file at this path on this PC.",
            _ => "Windows did not report a sync state for this file."
        };

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
            _ => 6
        };

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
            Latest is LatestState.ReloadNeeded or LatestState.SyncPending;
    }
}
