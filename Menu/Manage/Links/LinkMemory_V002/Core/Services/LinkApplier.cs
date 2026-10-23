using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>What a reload does to one link.</summary>
    public enum LinkAction
    {
        /// <summary>No change.</summary>
        None,
        /// <summary>Unloaded now, loaded after reload.</summary>
        Load,
        /// <summary>Loaded now, unloaded after reload.</summary>
        Unload,
        /// <summary>Loaded now and reloaded from file.</summary>
        Reload,
        /// <summary>Not used but can't be unloaded (CAD link: the Revit API has no unload for CAD).</summary>
        CantUnload,
        /// <summary>Should change but can't (not in the model, closed workset, checked out by someone else).</summary>
        Skipped
    }

    /// <summary>Reload options.</summary>
    public sealed class ReloadOptions
    {
        /// <summary>Unload Revit / IFC links the memory doesn't use (default on).</summary>
        public bool UnloadUnused { get; init; } = true;

        /// <summary>Reload used links that are already loaded, to pick up the latest file (default off).</summary>
        public bool ReloadUsed { get; init; }
    }

    /// <summary>The planned change for one link.</summary>
    public sealed class LinkPlan
    {
        /// <summary>What happens.</summary>
        public LinkAction Action { get; init; }

        /// <summary>Why, when the link is skipped.</summary>
        public string Note { get; init; }
    }

    /// <summary>Counts of a reload.</summary>
    public sealed class ReloadResult
    {
        /// <summary>Links loaded.</summary>
        public int Loaded { get; set; }
        /// <summary>Links unloaded.</summary>
        public int Unloaded { get; set; }
        /// <summary>Links reloaded from file.</summary>
        public int Reloaded { get; set; }
        /// <summary>Links skipped or failed.</summary>
        public int Skipped { get; set; }

        /// <inheritdoc/>
        public override string ToString()
            => $"{Loaded} loaded · {Unloaded} unloaded · {Reloaded} reloaded · {Skipped} skipped";
    }

    /// <summary>
    /// Plans and applies a memory: loads the links it uses and (optionally) unloads the Revit / IFC
    /// links it doesn't. Unknown links (unloaded when collected) and images, imports and point clouds
    /// are left as they are. Revit link load / unload calls manage their own transactions; CAD loads
    /// run in a transaction of their own. A failure on one link is logged and the rest continue.
    /// </summary>
    public static class LinkApplier
    {
        /// <summary>What a reload would do to <paramref name="saved"/>; <paramref name="live"/> is null when it's gone.</summary>
        public static LinkPlan Plan(Document doc, SavedLink saved, LiveLink live, ReloadOptions options)
        {
            if (live == null)
                return saved.Use == LinkUse.Used ? new LinkPlan { Action = LinkAction.Skipped, Note = "not in model" } : new LinkPlan();
            if (!live.CanLoad) return new LinkPlan();

            bool isLocal = IsLocalCopy(doc);

            if (saved.Use == LinkUse.Used && !live.IsLoaded)
            {
                if (live.Status == LinkedFileStatus.InClosedWorkset)
                    return new LinkPlan { Action = LinkAction.Skipped, Note = "workset closed" };
                bool forMeOnly = isLocal && live.Type is RevitLinkType rlt && rlt.LocallyUnloaded;
                if (!forMeOnly && OwnerOf(doc, live) is string owner)
                    return new LinkPlan { Action = LinkAction.Skipped, Note = $"checked out by {owner}" };
                return new LinkPlan { Action = LinkAction.Load };
            }

            if (saved.Use == LinkUse.NotUsed && live.IsLoaded && options.UnloadUnused)
            {
                if (!live.CanUnload) return new LinkPlan { Action = LinkAction.CantUnload };
                if (!isLocal && OwnerOf(doc, live) is string owner)
                    return new LinkPlan { Action = LinkAction.Skipped, Note = $"checked out by {owner}" };
                return new LinkPlan { Action = LinkAction.Unload };
            }

            if (saved.Use == LinkUse.Used && live.IsLoaded && options.ReloadUsed)
                return new LinkPlan { Action = LinkAction.Reload };

            return new LinkPlan();
        }

        /// <summary>Applies <paramref name="memory"/>. Must run in a Revit API context with no transaction open.</summary>
        public static ReloadResult Apply(Document doc, LinkMemoryItem memory, ReloadOptions options, Action<LogEntry> log)
        {
            var result = new ReloadResult();
            bool isLocal = IsLocalCopy(doc);

            foreach (SavedLink saved in memory.Links)
            {
                // Re-read each time: loading one link can change the others (shared nested links).
                LiveLink live = LinkCatalog.Match(LinkCatalog.Read(doc), saved);
                LinkPlan plan = Plan(doc, saved, live, options);
                try
                {
                    switch (plan.Action)
                    {
                        case LinkAction.Load:
                            if (Load(doc, live, isLocal, log)) result.Loaded++; else result.Skipped++;
                            break;
                        case LinkAction.Unload:
                            if (Unload(live, isLocal, log)) result.Unloaded++; else result.Skipped++;
                            break;
                        case LinkAction.Reload:
                            if (Reload(doc, live, log)) result.Reloaded++; else result.Skipped++;
                            break;
                        case LinkAction.Skipped:
                            result.Skipped++;
                            log(new LogEntry(LogLevel.Warning, $"Skipped {live?.Name ?? saved.Name}: {plan.Note}."));
                            break;
                    }
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException
                                           || ex is InvalidOperationException || ex is ArgumentException)
                {
                    result.Skipped++;
                    log(new LogEntry(LogLevel.Error, $"{live?.Name ?? saved.Name}: {ex.Message}"));
                }
            }
            return result;
        }

        private static bool Load(Document doc, LiveLink live, bool isLocal, Action<LogEntry> log)
        {
            if (live.Type is CADLinkType cad) return LoadCad(doc, live, cad, "Loaded", log);

            var type = (RevitLinkType)live.Type;
            // Unloaded "for me" only: clearing that restores the link without touching other users.
            if (isLocal && type.LocallyUnloaded && type.RevertLocalUnloadStatus() == LinkedFileStatus.Loaded)
            {
                log(new LogEntry(LogLevel.Success, $"Loaded {live.Name} (was unloaded for you only)."));
                return true;
            }

            LinkLoadResult load = type.Load();
            if (Succeeded(type, load))
            {
                log(new LogEntry(LogLevel.Success, $"Loaded {live.Name}"));
                return true;
            }
            log(new LogEntry(LogLevel.Warning, $"Could not load {live.Name}: {Describe(load?.LoadResult)}"));
            return false;
        }

        private static bool Unload(LiveLink live, bool isLocal, Action<LogEntry> log)
        {
            var type = (RevitLinkType)live.Type;
            if (isLocal)
            {
                // Workshared local copy: unload for this user only, as Manage Links' "Unload for me".
                if (type.UnloadLocally(null))
                {
                    log(new LogEntry(LogLevel.Success, $"Unloaded {live.Name} (for you only)"));
                    return true;
                }
                log(new LogEntry(LogLevel.Warning, $"Revit did not unload {live.Name}."));
                return false;
            }
            type.Unload(null);
            log(new LogEntry(LogLevel.Success, $"Unloaded {live.Name}"));
            return true;
        }

        private static bool Reload(Document doc, LiveLink live, Action<LogEntry> log)
        {
            if (live.Type is CADLinkType cad) return LoadCad(doc, live, cad, "Reloaded", log);

            var type = (RevitLinkType)live.Type;
            LinkLoadResult load = type.Reload();
            if (Succeeded(type, load))
            {
                log(new LogEntry(LogLevel.Success, $"Reloaded {live.Name}"));
                return true;
            }
            log(new LogEntry(LogLevel.Warning, $"Could not reload {live.Name}: {Describe(load?.LoadResult)}"));
            return false;
        }

        // CAD links have only Reload (it also loads an unloaded link). It edits the model, so it
        // runs in its own transaction; a failure rolls back.
        private static bool LoadCad(Document doc, LiveLink live, CADLinkType cad, string verb, Action<LogEntry> log)
        {
            using var t = new Transaction(doc, $"Link Memory: load {live.Name}");
            t.Start();
            LinkLoadResult load = cad.Reload();
            bool ok = load?.LoadResult == LinkLoadResultType.LinkLoaded;
            if (!ok)
            {
                t.RollBack();
                log(new LogEntry(LogLevel.Warning, $"Could not load {live.Name}: {Describe(load?.LoadResult)}"));
                return false;
            }
            t.Commit();
            log(new LogEntry(LogLevel.Success, $"{verb} {live.Name}"));
            return true;
        }

        // Trusts the link's status afterwards over the result code (some codes are informational).
        private static bool Succeeded(RevitLinkType type, LinkLoadResult load)
        {
            if (load?.LoadResult == LinkLoadResultType.LinkLoaded) return true;
            try { return type.GetLinkedFileStatus() == LinkedFileStatus.Loaded; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        // Loading or unloading for all users edits the link type, which another user may own.
        private static string OwnerOf(Document doc, LiveLink live)
        {
            if (!doc.IsWorkshared) return null;
            try
            {
                return WorksharingUtils.GetCheckoutStatus(doc, live.Type.Id, out string owner) == CheckoutStatus.OwnedByOtherUser
                    ? owner
                    : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        /// <summary>True for a local copy of a workshared model (not the central itself).</summary>
        public static bool IsLocalCopy(Document doc)
        {
            if (!doc.IsWorkshared) return false;
            if (doc.IsModelInCloud) return true;
            try
            {
                ModelPath central = doc.GetWorksharingCentralModelPath();
                string centralPath = central == null ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                return !string.Equals(centralPath, doc.PathName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
        }

        private static string Describe(LinkLoadResultType? result) => result switch
        {
            LinkLoadResultType.LinkNotFound => "the file was not found",
            LinkLoadResultType.LinkNotOpenable => "the file could not be opened",
            LinkLoadResultType.LinkOpenAsHost => "the file is open in this session as a model",
            LinkLoadResultType.SameModelAsHost or LinkLoadResultType.SameCentralModelAsHost => "the link is this model",
            LinkLoadResultType.ExternalServerMissing => "its external server is not available",
            LinkLoadResultType.LinkMayBeUpgraded => "it is from an older Revit version and must be upgraded",
            null => "Revit returned no result",
            _ => result.ToString()
        };
    }
}
