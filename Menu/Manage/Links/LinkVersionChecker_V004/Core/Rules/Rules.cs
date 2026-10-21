using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Rules
{
    internal static class R
    {
        public static IEnumerable<Finding> None => Enumerable.Empty<Finding>();
        public static IEnumerable<Finding> One(Finding f) { yield return f; }

        public static int YearsBehind(string year, int current) =>
            int.TryParse(year, out int y) ? Math.Max(1, current - y) : 1;
    }

    // ------------------------------------------------------------------ 1
    /// <summary>A cloud model in the host's own ACC project — informational.</summary>
    public sealed class CloudLinkRule : ILinkRule
    {
        public string Id => "CLOUD";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.AccThisProject ? R.None : R.One(new Finding(Id, Severity.Info,
                "Cloud model (ACC)",
                "Cloud model from this project's ACC hub. Its version can't be read from disk — check it in ACC Docs; an older cloud model must be upgraded by its owner."));
    }

    // ------------------------------------------------------------------ 1b
    /// <summary>A cloud link that lives in a DIFFERENT ACC project — an access risk for the team.</summary>
    public sealed class AccOtherProjectRule : ILinkRule
    {
        public string Id => "ACC_OTHER_PROJECT";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.AccOtherProject ? R.None : R.One(new Finding(Id, Severity.Warning,
                "Cloud link from another project",
                "This link lives in a different ACC project" + (string.IsNullOrEmpty(l.CloudProjectId) ? "" : $" ({l.CloudIdText})") +
                ". Anyone without access to that project gets 'Not Found' when they open the host — confirm the team's permissions."));
    }

    // ------------------------------------------------------------------ 1c
    public sealed class LegacyBim360Rule : ILinkRule
    {
        public string Id => "BIM360_LEGACY";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.Bim360Legacy ? R.None : R.One(new Finding(Id, Severity.Info,
                "Legacy BIM 360 link",
                "Still pointing at a legacy BIM 360 Docs location. Migrate it to the ACC project when the owning team is ready."));
    }

    // ------------------------------------------------------------------ 1d
    public sealed class DesktopConnectorRule : ILinkRule
    {
        public string Id => "DESKTOP_CONNECTOR";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.DesktopConnector ? R.None : R.One(new Finding(Id, Severity.Warning,
                "Desktop Connector path",
                "Looks like a local file but is ACC content synced by Desktop Connector. Open time depends on the sync state, and the path is personal to this PC. Link the cloud model directly instead."));
    }

    // ------------------------------------------------------------------ 1e
    public sealed class CloudSyncFolderRule : ILinkRule
    {
        public string Id => "CLOUD_SYNC";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.CloudSyncFolder ? R.None : R.One(new Finding(Id, Severity.Warning,
                "Cloud-sync folder",
                "Linked from a personal OneDrive / SharePoint sync folder. Nobody else can resolve this path, and a paused sync stalls the open. Move it to the project folder or ACC."));
    }

    // ------------------------------------------------------------------ 1f
    public sealed class RemovableDriveRule : ILinkRule
    {
        public string Id => "REMOVABLE_DRIVE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.RemovableDrive ? R.None : R.One(new Finding(Id, Severity.Warning,
                "Link on removable drive",
                "The file sits on a removable drive — the link breaks the moment it is unplugged. Copy it into the project folder and repath."));
    }

    // ------------------------------------------------------------------ 1g
    public sealed class MappedDriveRule : ILinkRule
    {
        public string Id => "MAPPED_DRIVE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.MappedDrive ? R.None : R.One(new Finding(Id, Severity.Info,
                "Mapped drive",
                $"Linked through a mapped drive letter{(string.IsNullOrEmpty(l.ResolvedUnc) ? "" : $" ({l.PathRoot} → {l.ResolvedUnc})")}. " +
                "Anyone whose drive letter differs loses the link — use the UNC path instead."));
    }

    // ------------------------------------------------------------------ 1h
    public sealed class RevitServerRule : ILinkRule
    {
        public string Id => "REVIT_SERVER";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.RevitServer ? R.None : R.One(new Finding(Id, Severity.Info,
                "Revit Server link",
                "Hosted on Revit Server (RSN://). There is no local file to probe — open time depends on the accelerator."));
    }

    // ------------------------------------------------------------------ 1i
    public sealed class UnresolvedSourceRule : ILinkRule
    {
        public string Id => "UNRESOLVED";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c) =>
            l.Source != LinkSource.Unknown || l.IsImport ? R.None : R.One(new Finding(Id, Severity.Critical,
                "Path unresolved",
                "Revit holds this reference with no usable path, so it can't be found or repaired automatically. Repath it, or delete the link type.",
                (int)LoadCostModel.MissingLinkSeconds));
    }

    // ------------------------------------------------------------------ 2
    public sealed class MissingLinkRule : ILinkRule
    {
        public string Id => "MISSING";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Exists || !l.HasLocalFile || l.IsImport) return R.None;
            return R.One(new Finding(Id, Severity.Critical, "Not found",
                "File not found at its saved path. Revit searches for it on every open. Repath it or remove the link.",
                (int)LoadCostModel.MissingLinkSeconds));
        }
    }

    // ------------------------------------------------------------------ 3
    public sealed class UnreadableHeaderRule : ILinkRule
    {
        public string Id => "UNREADABLE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Kind != LinkKind.RVT || !l.HasLocalFile || !l.Exists || l.HeaderRead) return R.None;
            return R.One(new Finding(Id, Severity.Warning, "Unreadable",
                "Could not read the file header" + (string.IsNullOrEmpty(l.HeaderError) ? "." : ": " + l.HeaderError)));
        }
    }

    // ------------------------------------------------------------------ 4
    public sealed class LaterVersionRule : ILinkRule
    {
        public string Id => "LATER_VERSION";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Kind != LinkKind.RVT || !l.HeaderRead || !l.IsLaterVersion) return R.None;
            return R.One(new Finding(Id, Severity.Critical, "Newer version",
                $"Saved in {l.SavedYear}, newer than Revit {c.CurrentYear}. It cannot load — ask for a {c.CurrentYear} copy."));
        }
    }

    // ------------------------------------------------------------------ 5
    public sealed class TemporaryUpgradeRule : ILinkRule
    {
        public string Id => "TEMP_UPGRADE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Kind != LinkKind.RVT || !l.HeaderRead || l.IsCurrentVersion || l.IsLaterVersion) return R.None;

            int behind = R.YearsBehind(l.SavedYear, c.CurrentYear);
            int est = (int)Math.Round(LoadCostModel.UpgradeBaseSeconds
                                      + l.SizeMb * LoadCostModel.UpgradeSecondsPerMb
                                      + LoadCostModel.UpgradeSecondsPerExtraVersion * (behind - 1));
            string msg = l.IsWorkshared
                ? $"Saved in {l.SavedYear} → upgraded in memory every time the host opens (never saved). Workshared ({l.WorksharedText}) — ask the model owner to upgrade the central to {c.CurrentYear}."
                : $"Saved in {l.SavedYear} → upgraded in memory every time the host opens. Use Upgrade & Save (a backup is kept) so it opens directly in {c.CurrentYear}.";
            return R.One(new Finding(Id, Severity.Critical, "Temporary upgrade", msg, est));
        }
    }

    // ------------------------------------------------------------------ 6
    public sealed class IfcCacheRule : ILinkRule
    {
        public string Id => "IFC_CACHE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Kind != LinkKind.IFC || !l.HasLocalFile || !l.Exists) return R.None;

            int est = (int)Math.Round(LoadCostModel.IfcBaseSeconds + l.SizeMb * LoadCostModel.IfcSecondsPerMb);

            if (!l.CacheExists)
            {
                string msg = l.CacheFolderWritable
                    ? "No .ifc.RVT cache, so the IFC is converted on every open. Use Rebuild IFC cache (In-Model mode reloads it once to create the cache)."
                    : "No .ifc.RVT cache and the IFC folder is read-only, so Revit can't save one. Copy the IFC to a writable folder and repath.";
                return R.One(new Finding(Id, Severity.Warning, "Cache missing", msg, est));
            }
            if (!l.CacheIsCurrent)
                return R.One(new Finding(Id, Severity.Warning, "Cache stale",
                    $"The .ifc.RVT cache was built by Revit {l.CacheYear}, so it is upgraded on every open. Rebuild the cache once in {c.CurrentYear}.", est));
            if (l.CacheIsOlderThanIfc)
                return R.One(new Finding(Id, Severity.Warning, "Cache stale",
                    $"The .ifc.RVT cache ({l.CacheTimeUtc?.ToLocalTime():dd-MMM-yy HH:mm}) is older than the IFC ({l.IfcTimeUtc?.ToLocalTime():dd-MMM-yy HH:mm}), so the IFC is reconverted. Rebuild the cache.", est));
            return R.None;
        }
    }

    // ------------------------------------------------------------------ 7
    public sealed class DuplicateLinkRule : ILinkRule
    {
        public string Id => "DUPLICATE";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.IsNested || l.IsImport || string.IsNullOrEmpty(l.FullPath)) return R.None;
            var same = c.HostLinks.Where(o => !o.IsNested && !o.IsImport &&
                                              string.Equals(o.FullPath, l.FullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Count < 2) return R.None;

            // Only the 2nd, 3rd... copy carries the cost.
            bool isFirst = ReferenceEquals(same[0], l);
            int est = isFirst ? 0 : (int)Math.Round(5 + l.SizeMb * LoadCostModel.DuplicateSecondsPerMb);
            return R.One(new Finding(Id, Severity.Warning, "Duplicate link",
                $"This file is linked {same.Count} times in the host, so it loads {same.Count} times. Keep one link type and copy its instances instead.", est));
        }
    }

    // ------------------------------------------------------------------ 8
    public sealed class ImportedCadRule : ILinkRule
    {
        public string Id => "IMPORTED_CAD";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (!l.IsImport) return R.None;
            return R.One(new Finding(Id, Severity.Warning, "Imported CAD",
                $"Imported, not linked ({Math.Max(l.Instances, 1)} instance(s)). Imports are embedded in the host, bloat the file and slow every open. Delete it, or link the CAD instead."));
        }
    }

    // ------------------------------------------------------------------ 9
    public sealed class LoadedPointCloudRule : ILinkRule
    {
        public string Id => "POINT_CLOUD";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.Kind != LinkKind.PointCloud || l.IsUnloaded || !l.Exists) return R.None;
            bool big = l.SizeMb >= 1024;
            return R.One(new Finding(Id, big ? Severity.Warning : Severity.Info, "Point cloud loaded",
                $"Point cloud is loaded ({l.SizeText}). Unload it when you don't need it — it costs memory and view regeneration time."));
        }
    }

    // ------------------------------------------------------------------ 10
    public sealed class AllWorksetsOpenRule : ILinkRule
    {
        public string Id => "WORKSETS";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (l.WorksetsTotal < 4 || l.WorksetsOpen < l.WorksetsTotal || l.SizeMb < 100) return R.None;
            return R.One(new Finding(Id, Severity.Info, "All worksets open",
                $"All {l.WorksetsTotal} worksets of this workshared link are open. Close the ones you don't need (Manage Links → Manage Worksets) to load less."));
        }
    }

    // ------------------------------------------------------------------ 11
    public sealed class NestedLinkRule : ILinkRule
    {
        public string Id => "NESTED";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (!l.IsNested) return R.None;
            string msg = c.InModel
                ? (l.IsUnloaded
                    ? $"Nested link of {l.ParentName} — currently not loaded."
                    : $"Nested link of {l.ParentName}, loaded because its parent attaches it as {l.Attachment}. If you don't need it, ask the parent's owner to change it to Overlay.")
                : $"Nested inside {l.ParentName}. It only loads if {l.ParentName} attaches it as Attachment (Overlay links don't load) — its estimate is shown in brackets and left out of the total.";
            return R.One(new Finding(Id, Severity.Info, "Nested", msg));
        }
    }

    // ------------------------------------------------------------------ 12
    public sealed class AbsolutePathRule : ILinkRule
    {
        public string Id => "ABS_PATH";
        public IEnumerable<Finding> Evaluate(LinkInfo l, RuleContext c)
        {
            if (!l.HasLocalFile || l.IsImport || l.IsNested || !l.Exists) return R.None;
            if (!string.Equals(l.PathType, "Absolute", StringComparison.OrdinalIgnoreCase)) return R.None;
            return R.One(new Finding(Id, Severity.Info, "Absolute path",
                l.IsUnc
                    ? "Uses an absolute network path. A relative path survives folder moves and project copies."
                    : "Uses an absolute local path — it breaks on any other computer. Use a relative path."));
        }
    }
}
