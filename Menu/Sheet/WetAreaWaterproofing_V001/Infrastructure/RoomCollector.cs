using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>What the tool read from the model in one pass.</summary>
    public sealed class ModelSnapshot
    {
        /// <summary>Every placed room of the host and the chosen links.</summary>
        public IReadOnlyList<RoomCandidate> Rooms { get; init; } = Array.Empty<RoomCandidate>();
        /// <summary>Roof type names in the host.</summary>
        public IReadOnlyList<string> RoofTypes { get; init; } = Array.Empty<string>();
        /// <summary>User workset names; empty when the model is not workshared.</summary>
        public IReadOnlyList<string> Worksets { get; init; } = Array.Empty<string>();
        /// <summary>Name of the active workset; empty when the model is not workshared.</summary>
        public string ActiveWorkset { get; init; } = string.Empty;
        /// <summary>Names of the loaded links.</summary>
        public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
        /// <summary>Model title.</summary>
        public string ModelTitle { get; init; } = string.Empty;
        /// <summary>Things worth telling the user about the read (rooms left out, links not loaded).</summary>
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    }

    /// <summary>Reads rooms, their outlines, the slab under each and earlier roofs from the model.</summary>
    public static class RoomCollector
    {
        /// <summary>Source key of host elements.</summary>
        public const string HostKey = "Host";

        private sealed record LevelInfo(long Id, string Name, double ZFt);

        /// <summary>
        /// Reads the model. Must run inside Revit's API context. Every placed room is listed, but the
        /// costly part (outline, slab, ownership) is only done for wet rooms, or, when
        /// <paramref name="checkOnly"/> is given, for exactly the rooms with those keys. The others
        /// come back with status <see cref="RoomStatus.NotChecked"/>.
        /// </summary>
        public static ModelSnapshot Read(Document doc, WetAreaSettings s, ISet<string> checkOnly = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            s ??= new WetAreaSettings().Normalize();
            var notes = new List<string>();

            var hostLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => new LevelInfo(l.Id.Value, l.Name, l.ProjectElevation))
                .OrderBy(l => l.ZFt).ToList();

            var allLinks = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            var loadedLinks = allLinks.Where(l => l.GetLinkDocument() != null).ToList();
            int notLoaded = allLinks.Count - loadedLinks.Count;
            if (notLoaded > 0) notes.Add($"{notLoaded} link(s) are not loaded and were not read.");
            var linkNames = loadedLinks.Select(LinkName).Distinct().OrderBy(n => n).ToList();

            var slabs = new SlabIndex(doc, loadedLinks);
            var existing = ExistingRoofs(doc);
            var rooms = new List<RoomCandidate>();

            if (s.IncludeHost)
                ReadRooms(doc, doc, Transform.Identity, HostKey, HostKey, hostLevels, slabs, existing, s, checkOnly, rooms, notes);

            if (s.IncludeLinks)
                foreach (RevitLinkInstance link in loadedLinks)
                {
                    string name = LinkName(link);
                    if (s.IncludedLinks.Count > 0 && !s.IncludedLinks.Contains(name)) continue;
                    ReadRooms(doc, link.GetLinkDocument(), link.GetTotalTransform(), link.UniqueId, name,
                        hostLevels, slabs, existing, s, checkOnly, rooms, notes);
                }

            var worksets = new List<string>();
            string activeWorkset = string.Empty;
            if (doc.IsWorkshared)
            {
                worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                    .Select(w => w.Name).OrderBy(n => n).ToList();
                WorksetTable table = doc.GetWorksetTable();
                activeWorkset = table.GetWorkset(table.GetActiveWorksetId())?.Name ?? string.Empty;
            }

            return new ModelSnapshot
            {
                Rooms = rooms,
                RoofTypes = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                    .Select(t => t.Name).Distinct().OrderBy(n => n).ToList(),
                Worksets = worksets,
                ActiveWorkset = activeWorkset,
                Links = linkNames,
                ModelTitle = doc.Title ?? string.Empty,
                Notes = notes
            };
        }

        /// <summary>Name a link is listed under: its file title.</summary>
        public static string LinkName(RevitLinkInstance link) => link.GetLinkDocument()?.Title ?? link.Name;

        /// <summary>User who has the element checked out, or null when it is free or the model is not workshared.</summary>
        public static string OwnerOf(Document doc, ElementId id)
        {
            if (doc == null || !doc.IsWorkshared || id == null || id == ElementId.InvalidElementId) return null;
            return WorksharingUtils.GetCheckoutStatus(doc, id, out string owner) == CheckoutStatus.OwnedByOtherUser ? owner : null;
        }

        /// <summary>Roofs earlier runs made, by room key: their ids and the newest run ID.</summary>
        public static Dictionary<string, (List<long> Ids, string RunId)> ExistingRoofs(Document doc)
        {
            var result = new Dictionary<string, (List<long> Ids, string RunId)>();
            foreach (Element roof in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType())
            {
                string comments = roof.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                if (!RunTag.TryParse(comments, out string runId, out string roomKey)) continue;
                if (!result.TryGetValue(roomKey, out var entry)) entry = (new List<long>(), runId);
                entry.Ids.Add(roof.Id.Value);
                if (string.CompareOrdinal(runId, entry.RunId) > 0) entry.RunId = runId;
                result[roomKey] = entry;
            }
            return result;
        }

        private static void ReadRooms(
            Document host, Document source, Transform toHost, string sourceKey, string sourceName,
            List<LevelInfo> hostLevels, SlabIndex slabs, Dictionary<string, (List<long> Ids, string RunId)> existing,
            WetAreaSettings s, ISet<string> checkOnly, List<RoomCandidate> rooms, List<string> notes)
        {
            var all = new FilteredElementCollector(source).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>().ToList();
            int unplaced = 0, done = 0;
            bool isHost = sourceKey == HostKey;
            WorksetTable worksets = source.IsWorkshared ? source.GetWorksetTable() : null;

            foreach (Room room in all)
            {
                if (++done % 20 == 0)
                {
                    ToolCancellation.CheckCurrent();
                    ToolCancellation.ReportCurrent(done, all.Count, $"Reading rooms in {sourceName}… {done} of {all.Count}");
                }
                if (room.Location is not LocationPoint location) { unplaced++; continue; }

                Level level = room.Level;
                double levelZ = toHost.OfPoint(new XYZ(0, 0, level?.ProjectElevation ?? location.Point.Z)).Z;
                LevelInfo twin = hostLevels.FirstOrDefault(l => Math.Abs(l.ZFt - levelZ) < 1 / BoundaryReader.FtToMm);
                LevelInfo hostLevel = twin
                    ?? hostLevels.LastOrDefault(l => l.ZFt <= levelZ + 1e-6)
                    ?? hostLevels.FirstOrDefault();

                var row = new RoomRow
                {
                    RoomKey = sourceKey + ":" + room.UniqueId,
                    RoomId = room.Id.Value,
                    Source = sourceName,
                    Level = twin?.Name ?? level?.Name ?? string.Empty,
                    LevelElevationMm = levelZ * BoundaryReader.FtToMm,
                    Number = room.Number ?? string.Empty,
                    Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? string.Empty,
                    AreaM2 = room.Area * BoundaryReader.Ft2ToM2,
                    Phase = room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsValueString() ?? string.Empty,
                    Workset = worksets?.GetWorkset(room.WorksetId)?.Name ?? string.Empty
                };
                row.Matched = KeywordMatcher.Match(row.Name, s.Keywords);

                var candidate = new RoomCandidate
                {
                    Row = row,
                    HostLevelId = hostLevel?.Id ?? 0,
                    HostLevelElevationMm = (hostLevel?.ZFt ?? 0) * BoundaryReader.FtToMm
                };
                if (existing.TryGetValue(row.RoomKey, out var old))
                {
                    candidate.ExistingRoofIds = old.Ids;
                    candidate.ExistingRunId = old.RunId;
                }

                bool check = checkOnly != null ? checkOnly.Contains(row.RoomKey) : !string.IsNullOrEmpty(row.Matched);
                if (!check)
                {
                    RoomStatusRules.Apply(candidate); // listed, not checked: no outline, no slab search
                    rooms.Add(candidate);
                    continue;
                }

                try
                {
                    candidate.Outline = room.Area > 1e-9
                        ? BoundaryReader.Read(room, toHost, s)
                        : new RoomOutline(new List<Pt>(), new List<IReadOnlyList<Pt>>(), 0, 0, 0, RoomStatus.NotEnclosed, new List<RepairStep>());

                    SlabHit slab = slabs.Find(toHost.OfPoint(location.Point), levelZ, sourceKey);
                    if (slab != null)
                    {
                        row.SlabId = slab.FloorId;
                        row.SlabTopMm = slab.TopFt * BoundaryReader.FtToMm;
                    }

                    row.Owner = (isHost ? OwnerOf(host, room.Id) : null) ?? slab?.Owner
                        ?? candidate.ExistingRoofIds.Select(id => OwnerOf(host, new ElementId(id))).FirstOrDefault(o => o != null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // One bad room must not stop the read: it is listed as failed with the reason.
                    candidate.Outline = new RoomOutline(new List<Pt>(), new List<IReadOnlyList<Pt>>(), 0, row.AreaM2, 0,
                        RoomStatus.Failed, new List<RepairStep> { new(0, "Read", false, ex.Message, null) });
                }

                RoomStatusRules.Apply(candidate);
                rooms.Add(candidate);
            }
            if (unplaced > 0) notes.Add($"{unplaced} unplaced room(s) in {sourceName} were left out.");
        }
    }
}
