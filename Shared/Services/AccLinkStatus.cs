using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// ACC Docs status of one Revit link, as the shared "ACC" radio-dot column shows it.
    /// <see cref="NotAcc"/> is 0, so an unset value means "not an ACC link — nothing to report".
    /// </summary>
    public enum AccLinkState
    {
        /// <summary>Not an ACC / Autodesk Docs link (local, network, Revit Server…).</summary>
        NotAcc = 0,

        /// <summary>ACC Docs link, loaded in this session.</summary>
        Loaded,

        /// <summary>ACC Docs link that is not loaded (unloaded, not found, closed workset).</summary>
        NotLoaded,

        /// <summary>ACC confirmed this is the newest published version.</summary>
        Current,

        /// <summary>ACC reports a newer published version than the one in use.</summary>
        OutOfDate,

        /// <summary>ACC link whose version has not been, or could not be, confirmed.</summary>
        Unknown,

        /// <summary>The reference is invalid or reading it failed.</summary>
        Failed
    }

    /// <summary>
    /// Detects whether a Revit link comes from ACC / Autodesk Docs (or BIM 360), and describes the
    /// result for the shared <see cref="Controls.AccStatusDot"/> column. Every read here is local
    /// (no network call) and never throws, so it is safe while a tool fills its link list.
    /// Asking ACC for the version status is a network call and stays in Link Version Checker.
    /// </summary>
    public static class AccLinkStatus
    {
        // Desktop Connector mirrors ACC Docs into these folders, so a file link under them is ACC content.
        private static readonly string[] DesktopConnectorFolders = { @"\ACCDocs\", @"\Autodesk Docs\", @"\BIM 360\" };

        /// <summary>ACC status of the link type behind <paramref name="instance"/>.</summary>
        public static AccLinkState Read(RevitLinkInstance instance)
        {
            if (instance?.Document == null) return AccLinkState.NotAcc;
            try { return Read(instance.Document, instance.GetTypeId()); }
            catch { return AccLinkState.NotAcc; }
        }

        /// <summary>
        /// ACC status of the <see cref="RevitLinkType"/> with id <paramref name="linkTypeId"/> in
        /// <paramref name="host"/>: <see cref="AccLinkState.Loaded"/> or <see cref="AccLinkState.NotLoaded"/>
        /// for an ACC link, <see cref="AccLinkState.NotAcc"/> for anything else. Never throws.
        /// </summary>
        public static AccLinkState Read(Document host, ElementId linkTypeId)
        {
            try
            {
                if (host == null || linkTypeId == null || linkTypeId == ElementId.InvalidElementId) return AccLinkState.NotAcc;
                if (!(host.GetElement(linkTypeId) is RevitLinkType type)) return AccLinkState.NotAcc;
                if (!IsAcc(type)) return AccLinkState.NotAcc;

                LinkedFileStatus status;
                try { status = type.GetLinkedFileStatus(); }
                catch { return AccLinkState.Unknown; }

                return status == LinkedFileStatus.Loaded || status == LinkedFileStatus.CanBeUpgraded
                    ? AccLinkState.Loaded
                    : AccLinkState.NotLoaded;
            }
            catch
            {
                return AccLinkState.Failed;
            }
        }

        /// <summary>
        /// True when <paramref name="type"/> points at ACC / Autodesk Docs / BIM 360: a cloud resource
        /// reference, or a file under a Desktop Connector folder. Never throws.
        /// </summary>
        public static bool IsAcc(RevitLinkType type)
        {
            if (type == null) return false;

            bool isFileRef = false;
            try { isFileRef = type.IsExternalFileReference(); } catch { /* treat as resource link */ }

            if (isFileRef)
            {
                try
                {
                    ModelPath mp = type.GetExternalFileReference()?.GetAbsolutePath();
                    if (mp == null) return false;
                    if (mp.CloudPath) return true;
                    string path = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp) ?? "";
                    foreach (string folder in DesktopConnectorFolders)
                        if (path.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                catch { /* unreadable path: not ACC */ }
                return false;
            }

            // A cloud RevitLinkType is not an ExternalFileReference; it carries a resource reference instead.
            IDictionary<ExternalResourceType, ExternalResourceReference> refs = null;
            try { refs = type.GetExternalResourceReferences(); } catch { /* none */ }
            if (refs == null) return false;

            foreach (ExternalResourceReference err in refs.Values)
                if (IsAccReference(err)) return true;
            return false;
        }

        private static bool IsAccReference(ExternalResourceReference err)
        {
            if (err == null) return false;

            string path = "";
            try { path = err.InSessionPath ?? ""; } catch { /* ignore */ }
            if (path.StartsWith("Autodesk Docs", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("ACC", StringComparison.OrdinalIgnoreCase))
                return true;

            // ACC references carry a project id in their (undocumented) reference information.
            try
            {
                IDictionary<string, string> info = err.GetReferenceInformation();
                if (info != null)
                    foreach (var kv in info)
                        if (kv.Key != null && kv.Key.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !string.IsNullOrEmpty(kv.Value))
                            return true;
            }
            catch { /* ignore */ }
            return false;
        }

        /// <summary>Short cell / export text for <paramref name="state"/> ("" for <see cref="AccLinkState.NotAcc"/>).</summary>
        public static string Text(AccLinkState state)
        {
            switch (state)
            {
                case AccLinkState.Loaded: return "ACC - loaded";
                case AccLinkState.NotLoaded: return "ACC - not loaded";
                case AccLinkState.Current: return "ACC - current";
                case AccLinkState.OutOfDate: return "ACC - out of date";
                case AccLinkState.Unknown: return "ACC - unknown";
                case AccLinkState.Failed: return "ACC - failed";
                default: return "";
            }
        }

        /// <summary>Tooltip explaining what the dot for <paramref name="state"/> means.</summary>
        public static string Tip(AccLinkState state)
        {
            switch (state)
            {
                case AccLinkState.Loaded: return "ACC Docs link, loaded (green dot).";
                case AccLinkState.NotLoaded: return "ACC Docs link, not loaded in this session (hollow green ring).";
                case AccLinkState.Current: return "ACC Docs link — ACC confirms this is the newest version (green dot).";
                case AccLinkState.OutOfDate: return "ACC Docs link — a newer version is published in ACC (amber dot). Reload the link.";
                case AccLinkState.Unknown: return "ACC Docs link — version not confirmed (grey ring).";
                case AccLinkState.Failed: return "ACC Docs link — the reference could not be read (red dot).";
                default: return "Not an ACC Docs link.";
            }
        }

        /// <summary>Sort key that puts ACC problems first, then healthy ACC links, then non-ACC links.</summary>
        public static int SortOrder(AccLinkState state)
        {
            switch (state)
            {
                case AccLinkState.Failed: return 0;
                case AccLinkState.OutOfDate: return 1;
                case AccLinkState.NotLoaded: return 2;
                case AccLinkState.Unknown: return 3;
                case AccLinkState.Loaded: return 4;
                case AccLinkState.Current: return 5;
                default: return 6;
            }
        }
    }
}
