using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>A slab found under a room.</summary>
    /// <param name="FloorId">ElementId of the floor in its own model.</param>
    /// <param name="TopFt">Top of the slab at the room, host Z in feet.</param>
    /// <param name="Owner">User who has a host slab checked out; null when free or linked.</param>
    public sealed record SlabHit(long FloorId, double TopFt, string Owner);

    /// <summary>
    /// Every floor of the host and of the loaded links, indexed once so the slab under each room
    /// is found without a model query per room.
    /// </summary>
    public sealed class SlabIndex
    {
        /// <summary>How far above or below the room's level a slab top may be, feet (500 mm).</summary>
        public const double SearchBandFt = 500 / 304.8;

        private sealed class Entry
        {
            public Floor Floor;
            public Transform ToHost;
            public string SourceKey;
            public double MinX, MinY, MaxX, MaxY, MinZ, MaxZ; // host coordinates, feet
            public double LocalMinZ, LocalMaxZ;               // the floor's own coordinates, feet
            public List<Face> TopFaces;                       // read once, on first use
        }

        private readonly Document _host;
        private readonly List<Entry> _entries = new();

        /// <summary>Indexes the floors of <paramref name="host"/> and of <paramref name="links"/>.</summary>
        public SlabIndex(Document host, IEnumerable<RevitLinkInstance> links)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Add(host, Transform.Identity, RoomCollector.HostKey);
            foreach (RevitLinkInstance link in links ?? Enumerable.Empty<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc != null) Add(linkDoc, link.GetTotalTransform(), link.UniqueId);
            }
        }

        private void Add(Document doc, Transform toHost, string sourceKey)
        {
            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null) continue;
                var entry = new Entry
                {
                    Floor = floor, ToHost = toHost, SourceKey = sourceKey,
                    LocalMinZ = box.Min.Z, LocalMaxZ = box.Max.Z,
                    MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue,
                    MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue
                };
                foreach (double x in new[] { box.Min.X, box.Max.X })
                    foreach (double y in new[] { box.Min.Y, box.Max.Y })
                        foreach (double z in new[] { box.Min.Z, box.Max.Z })
                        {
                            XYZ p = toHost.OfPoint(new XYZ(x, y, z));
                            entry.MinX = Math.Min(entry.MinX, p.X); entry.MaxX = Math.Max(entry.MaxX, p.X);
                            entry.MinY = Math.Min(entry.MinY, p.Y); entry.MaxY = Math.Max(entry.MaxY, p.Y);
                            entry.MinZ = Math.Min(entry.MinZ, p.Z); entry.MaxZ = Math.Max(entry.MaxZ, p.Z);
                        }
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// The slab under <paramref name="hostPoint"/> whose top is within 500 mm of the room's
        /// level. The host model is searched first, then the room's own link, then the other links;
        /// the highest slab of the first model that has one wins. Null when there is none.
        /// </summary>
        /// <param name="hostPoint">A point inside the room, host coordinates.</param>
        /// <param name="levelZFt">The room's level, host Z in feet.</param>
        /// <param name="roomSourceKey">Where the room lives: <see cref="RoomCollector.HostKey"/> or a link's UniqueId.</param>
        public SlabHit Find(XYZ hostPoint, double levelZFt, string roomSourceKey)
        {
            var near = _entries.Where(e =>
                hostPoint.X >= e.MinX && hostPoint.X <= e.MaxX && hostPoint.Y >= e.MinY && hostPoint.Y <= e.MaxY
                && e.MaxZ >= levelZFt - SearchBandFt && e.MinZ <= levelZFt + SearchBandFt).ToList();

            var groups = new[]
            {
                near.Where(e => e.SourceKey == RoomCollector.HostKey),
                near.Where(e => e.SourceKey == roomSourceKey && e.SourceKey != RoomCollector.HostKey),
                near.Where(e => e.SourceKey != roomSourceKey && e.SourceKey != RoomCollector.HostKey)
            };
            foreach (IEnumerable<Entry> group in groups)
            {
                Entry bestEntry = null;
                double bestTop = double.MinValue;
                foreach (Entry entry in group)
                {
                    double? top = TopAt(entry, hostPoint);
                    if (!top.HasValue || Math.Abs(top.Value - levelZFt) > SearchBandFt || top.Value <= bestTop) continue;
                    bestTop = top.Value;
                    bestEntry = entry;
                }
                if (bestEntry == null) continue;

                string owner = null;
                if (bestEntry.SourceKey == RoomCollector.HostKey) owner = RoomCollector.OwnerOf(_host, bestEntry.Floor.Id);
                return new SlabHit(bestEntry.Floor.Id.Value, bestTop, owner);
            }
            return null;
        }

        /// <summary>Host Z of the slab's top face straight above or below the point; null when the point is not over the slab.</summary>
        private static double? TopAt(Entry entry, XYZ hostPoint)
        {
            try
            {
                XYZ local = entry.ToHost.Inverse.OfPoint(hostPoint);
                Line probe = Line.CreateBound(
                    new XYZ(local.X, local.Y, entry.LocalMinZ - 1), new XYZ(local.X, local.Y, entry.LocalMaxZ + 1));

                if (entry.TopFaces == null)
                {
                    entry.TopFaces = new List<Face>();
                    foreach (Reference reference in HostObjectUtils.GetTopFaces(entry.Floor))
                        if (entry.Floor.GetGeometryObjectFromReference(reference) is Face topFace) entry.TopFaces.Add(topFace);
                }

                double? top = null;
                foreach (Face face in entry.TopFaces)
                {
                    if (face.Intersect(probe, out IntersectionResultArray hits) != SetComparisonResult.Overlap || hits == null) continue;
                    foreach (IntersectionResult hit in hits)
                    {
                        double z = entry.ToHost.OfPoint(hit.XYZPoint).Z;
                        if (!top.HasValue || z > top.Value) top = z;
                    }
                }
                return top;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                entry.TopFaces ??= new List<Face>();
                return null; // a floor Revit cannot give faces for is not used
            }
        }
    }
}
