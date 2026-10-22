using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>
    /// Notices when elements with a Tracker_ID are deleted: Revit shows a warning (Ctrl+Z restores them)
    /// and the IDs are added to the model's deletion log, which the health check lists as "Deleted".
    /// It knows which ID an element had from a list read when a model opens and refreshed whenever
    /// Tracker ID V002 reads the model. Registered once at Revit start-up; it never throws into Revit.
    /// </summary>
    public sealed class TrackerDeletionWatcher : IUpdater
    {
        private static readonly AddInId AddIn = new(new Guid("a4e8c4c7-9f8b-4a6a-9d31-532f8c980d41"));
        private static readonly UpdaterId Id = new(AddIn, new Guid("3a243ec3-58ee-4b42-9cdd-3ba3fbd5d840"));
        private static readonly FailureDefinitionId WarningId = new(new Guid("60febbde-5c39-4364-8ba7-c1bc1a746fd0"));

        // Per open model: element id → Tracker_ID.
        private static readonly Dictionary<string, Dictionary<long, string>> Known = new();
        private static readonly object Gate = new();

        /// <summary>Folder of the deletion logs: %AppData%\Revit26_Plugin_Revised\TrackerId_V002\Deleted.</summary>
        public static string LogFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                       "Revit26_Plugin_Revised", "TrackerId_V002", "Deleted");

        /// <summary>The deletion log of <paramref name="doc"/>.</summary>
        public static string LogPath(Document doc) => DeletionLog.PathFor(LogFolder, doc?.Title);

        /// <summary>Registers the watcher, its warning and the model-open hook. Call once from OnStartup.</summary>
        public static void Register(UIControlledApplication application)
        {
            try
            {
                FailureDefinition.CreateFailureDefinition(WarningId, FailureSeverity.Warning,
                    "Elements with a Tracker_ID were deleted. Their IDs are listed in Tracker ID V002 › Health check › Deleted. " +
                    "Press Ctrl+Z to restore them.");

                var watcher = new TrackerDeletionWatcher();
                if (!UpdaterRegistry.IsUpdaterRegistered(Id))
                {
                    UpdaterRegistry.RegisterUpdater(watcher, true);
                    UpdaterRegistry.AddTrigger(Id, new ElementIsElementTypeFilter(true), Element.GetChangeTypeElementDeletion());
                }

                application.ControlledApplication.DocumentOpened += OnDocumentOpened;
                application.ControlledApplication.DocumentClosing += OnDocumentClosing;
            }
            catch (Exception ex)
            {
                // The plugin works without the watcher; deletions are then simply not noticed.
                ToolGuard.Report(typeof(TrackerDeletionWatcher), ex);
            }
        }

        /// <summary>Replaces what the watcher knows about <paramref name="doc"/>'s Tracker_IDs.</summary>
        public static void Remember(Document doc, IEnumerable<(long ElementId, string TrackerId)> holders)
        {
            if (doc == null || doc.IsFamilyDocument) return;
            var map = holders.Where(h => !string.IsNullOrEmpty(h.TrackerId)).GroupBy(h => h.ElementId)
                             .ToDictionary(g => g.Key, g => g.First().TrackerId);
            lock (Gate) Known[Key(doc)] = map;
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument || TrackerParameterService.FindBinding(doc, TrackerParameters.TrackerId).Binding == null) return;
                Remember(doc, ModelReader.ReadIdHolders(doc).Select(h => (h.ElementId, h.TrackerId)));
            }
            catch (Exception ex)
            {
                ToolGuard.Report(typeof(TrackerDeletionWatcher), ex);
            }
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            if (e.Document == null) return;
            lock (Gate) Known.Remove(Key(e.Document));
        }

        /// <inheritdoc/>
        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                Dictionary<long, string> map;
                lock (Gate)
                    if (!Known.TryGetValue(Key(doc), out map)) return;

                var gone = new List<DeletedTrackerId>();
                string user = doc.Application.Username ?? string.Empty;
                foreach (var id in data.GetDeletedElementIds())
                {
                    if (!map.TryGetValue(id.Value, out string trackerId)) continue;
                    gone.Add(new DeletedTrackerId { TrackerId = trackerId, ElementId = id.Value, DeletedAt = DateTime.Now, User = user });
                    map.Remove(id.Value);
                }
                if (gone.Count == 0) return;

                try { DeletionLog.Append(LogPath(doc), gone); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { ToolGuard.Report(GetType(), ex); }

                doc.PostFailure(new FailureMessage(WarningId));
            }
            catch (Exception ex)
            {
                ToolGuard.Report(GetType(), ex);
            }
        }

        private static string Key(Document doc) => string.IsNullOrEmpty(doc.PathName) ? "untitled:" + doc.Title : doc.PathName;

        /// <inheritdoc/>
        public UpdaterId GetUpdaterId() => Id;

        /// <inheritdoc/>
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;

        /// <inheritdoc/>
        public string GetUpdaterName() => "Tracker ID deletion watcher";

        /// <inheritdoc/>
        public string GetAdditionalInformation() =>
            "Warns when elements with a Tracker_ID are deleted and lists them in the Tracker ID health check.";
    }
}
