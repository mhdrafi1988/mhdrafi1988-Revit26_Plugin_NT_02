using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkMemory.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V001.Core.Services
{
    /// <summary>A top-level Revit link of the open model, with its current state.</summary>
    public sealed class LiveLink
    {
        /// <summary>The link type.</summary>
        public RevitLinkType Type { get; init; }

        /// <summary>Link type name.</summary>
        public string Name { get; init; }

        /// <summary>UniqueId of the link type.</summary>
        public string UniqueId { get; init; }

        /// <summary>User-visible path, or null.</summary>
        public string Path { get; init; }

        /// <summary>Status reported by Revit.</summary>
        public LinkedFileStatus Status { get; init; }

        /// <summary>True when the link is loaded.</summary>
        public bool IsLoaded => Status == LinkedFileStatus.Loaded;
    }

    /// <summary>Options of a reload.</summary>
    public sealed class LinkReloadOptions
    {
        /// <summary>Unload links the set had unloaded (default on).</summary>
        public bool UnloadUnloaded { get; init; } = true;

        /// <summary>Reload links that are already loaded, to pick up the latest file (default off).</summary>
        public bool ReloadLoaded { get; init; }
    }

    /// <summary>Counts of a reload.</summary>
    public sealed class LinkReloadResult
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
    /// Reads the open model's Revit links, loads / saves the memory file, and applies a saved set.
    /// Link load / unload calls manage their own transactions (Revit refuses them inside one), so
    /// each link is changed on its own; a failure on one link is logged and the rest continue.
    /// </summary>
    public static class LinkMemoryService
    {
        /// <summary>Folder under %AppData%\Revit26_Plugin_Revised.</summary>
        public const string ToolFolder = "LinkMemory";

        /// <summary>Path of the memory file.</summary>
        public static string FilePath => JsonFileStore.PathFor(ToolFolder, "memory.json");

        /// <summary>File name of the window options (kept apart from V002's).</summary>
        public const string SettingsFileName = "settings_v001.json";

        /// <summary>Loads the memory file; <paramref name="warning"/> is set when it could not be read.</summary>
        public static LinkMemoryFile Load(out string warning)
        {
            LinkMemoryFile file = JsonFileStore.Load<LinkMemoryFile>(FilePath, out warning);
            file.Normalize();
            return file;
        }

        /// <summary>
        /// Re-reads the memory file, runs <paramref name="change"/> on the latest content and saves it
        /// when <paramref name="change"/> returns true, so sets another Revit session saved in the
        /// meantime are kept. Returns the file as saved. Throws <see cref="System.IO.IOException"/>,
        /// <see cref="UnauthorizedAccessException"/> or <see cref="NotSupportedException"/> on failure.
        /// </summary>
        public static LinkMemoryFile Update(Func<LinkMemoryFile, bool> change)
            => JsonFileStore.Update<LinkMemoryFile>(FilePath, file =>
            {
                file.Normalize();
                if (!change(file)) return false;
                file.FormatVersion = LinkMemoryFile.CurrentFormat;
                return true;
            });

        /// <summary>Loads the window options (defaults when there are none or they can't be read).</summary>
        public static LinkMemorySettings LoadSettings()
            => SettingsService<LinkMemorySettings>.Load(ToolFolder, SettingsFileName);

        /// <summary>Saves the window options (a failure only means they are not remembered).</summary>
        public static void SaveSettings(LinkMemorySettings settings)
        {
            settings.FormatVersion = LinkMemorySettings.CurrentFormat;
            SettingsService<LinkMemorySettings>.Save(ToolFolder, SettingsFileName, settings);
        }

        /// <summary>Top-level Revit links of <paramref name="doc"/>, sorted by name.</summary>
        public static List<LiveLink> ReadLinks(Document doc)
        {
            var links = new List<LiveLink>();
            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                if (type.IsNestedLink) continue;

                LinkedFileStatus status;
                try { status = type.GetLinkedFileStatus(); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { status = LinkedFileStatus.Invalid; }

                links.Add(new LiveLink
                {
                    Type = type,
                    Name = type.Name,
                    UniqueId = type.UniqueId,
                    Path = PathOf(doc, type),
                    Status = status
                });
            }
            return links.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Snapshot of <paramref name="doc"/>'s link states, named <paramref name="name"/>.</summary>
        public static LinkSet Capture(Document doc, string name) => new()
        {
            Name = name,
            SavedAt = DateTime.Now,
            SavedBy = doc.Application.Username,
            Links = ReadLinks(doc).Select(l => new SavedLink
            {
                UniqueId = l.UniqueId,
                Name = l.Name,
                Path = l.Path,
                IsLoaded = l.IsLoaded
            }).ToList()
        };

        /// <summary>The live link for a saved one: same UniqueId first, then same name (ignoring case).</summary>
        public static LiveLink Match(IEnumerable<LiveLink> live, SavedLink saved)
        {
            var list = live as IList<LiveLink> ?? live.ToList();
            return list.FirstOrDefault(l => l.UniqueId == saved.UniqueId)
                ?? list.FirstOrDefault(l => string.Equals(l.Name, saved.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Loads and unloads <paramref name="doc"/>'s links to match <paramref name="set"/>. Links not
        /// in the set are left alone. Must run in a Revit API context with no transaction open.
        /// </summary>
        public static LinkReloadResult Apply(Document doc, LinkSet set, LinkReloadOptions options, Action<LogEntry> log)
        {
            var result = new LinkReloadResult();
            bool isLocal = IsLocalCopy(doc);

            foreach (SavedLink saved in set.Links)
            {
                // Re-read each time: loading one link can change the others (shared nested links).
                LiveLink live = Match(ReadLinks(doc), saved);
                if (live == null)
                {
                    if (saved.IsLoaded)
                    {
                        result.Skipped++;
                        log(new LogEntry(LogLevel.Warning, $"Skipped {saved.Name}: the link is no longer in the model."));
                    }
                    continue;
                }

                try
                {
                    if (saved.IsLoaded && !live.IsLoaded)
                    {
                        if (Load(doc, live, isLocal, log)) result.Loaded++;
                        else result.Skipped++;
                    }
                    else if (!saved.IsLoaded && live.IsLoaded && options.UnloadUnloaded)
                    {
                        if (Unload(doc, live, isLocal, log)) result.Unloaded++;
                        else result.Skipped++;
                    }
                    else if (saved.IsLoaded && live.IsLoaded && options.ReloadLoaded)
                    {
                        if (Reload(live, log)) result.Reloaded++;
                        else result.Skipped++;
                    }
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException
                                           || ex is InvalidOperationException || ex is ArgumentException)
                {
                    result.Skipped++;
                    log(new LogEntry(LogLevel.Error, $"{live.Name}: {ex.Message}"));
                }
            }

            return result;
        }

        private static bool Load(Document doc, LiveLink live, bool isLocal, Action<LogEntry> log)
        {
            if (live.Status == LinkedFileStatus.InClosedWorkset)
            {
                log(new LogEntry(LogLevel.Warning, $"Skipped {live.Name}: its workset is closed. Open the workset first."));
                return false;
            }

            // Unloaded "for me" only: clearing that restores the link without touching other users.
            if (isLocal && live.Type.LocallyUnloaded)
            {
                LinkedFileStatus after = live.Type.RevertLocalUnloadStatus();
                if (after == LinkedFileStatus.Loaded)
                {
                    log(new LogEntry(LogLevel.Success, $"Loaded {live.Name} (was unloaded for you only)."));
                    return true;
                }
            }

            if (OwnedByOther(doc, live, log)) return false;

            LinkLoadResult load = live.Type.Load();
            if (Succeeded(live, load))
            {
                log(new LogEntry(LogLevel.Success, $"Loaded {live.Name}"));
                return true;
            }
            log(new LogEntry(LogLevel.Warning, $"Could not load {live.Name}: {Describe(load?.LoadResult)}"));
            return false;
        }

        private static bool Unload(Document doc, LiveLink live, bool isLocal, Action<LogEntry> log)
        {
            if (isLocal)
            {
                // Workshared local copy: unload for this user only, as Manage Links' "Unload for me".
                if (live.Type.UnloadLocally(null))
                {
                    log(new LogEntry(LogLevel.Success, $"Unloaded {live.Name} (for you only)"));
                    return true;
                }
                log(new LogEntry(LogLevel.Warning, $"Revit did not unload {live.Name}."));
                return false;
            }

            if (OwnedByOther(doc, live, log)) return false;
            live.Type.Unload(null);
            log(new LogEntry(LogLevel.Success, $"Unloaded {live.Name}"));
            return true;
        }

        private static bool Reload(LiveLink live, Action<LogEntry> log)
        {
            LinkLoadResult load = live.Type.Reload();
            if (Succeeded(live, load))
            {
                log(new LogEntry(LogLevel.Success, $"Reloaded {live.Name}"));
                return true;
            }
            log(new LogEntry(LogLevel.Warning, $"Could not reload {live.Name}: {Describe(load?.LoadResult)}"));
            return false;
        }

        // Trusts the link's status afterwards over the result code (some codes are informational).
        private static bool Succeeded(LiveLink live, LinkLoadResult load)
        {
            if (load?.LoadResult == LinkLoadResultType.LinkLoaded) return true;
            try { return live.Type.GetLinkedFileStatus() == LinkedFileStatus.Loaded; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        // Loading or unloading for all users edits the link type, which another user may own.
        private static bool OwnedByOther(Document doc, LiveLink live, Action<LogEntry> log)
        {
            if (!doc.IsWorkshared) return false;
            CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, live.Type.Id, out string owner);
            if (status != CheckoutStatus.OwnedByOtherUser) return false;
            log(new LogEntry(LogLevel.Warning, $"Skipped {live.Name}: its link type is checked out by {owner}."));
            return true;
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

        private static string PathOf(Document doc, RevitLinkType type)
        {
            try
            {
                ExternalFileReference reference = ExternalFileUtils.GetExternalFileReference(doc, type.Id);
                ModelPath path = reference?.GetAbsolutePath();
                return path == null || path.Empty ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // cloud links have no file path
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
