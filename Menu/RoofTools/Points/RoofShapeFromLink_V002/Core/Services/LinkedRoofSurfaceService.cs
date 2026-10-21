using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofShapeFromLink.V002.Core.Models;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V002.Core.Services
{
    /// <summary>
    /// Finds, for a host point, the roof above it in the chosen links: the Z of its top (sky-facing)
    /// surface at the same X,Y, and the Z of its nearest shape-editing point. All results are in the
    /// host model's coordinates and in feet. Must run in a valid Revit API context.
    /// </summary>
    public sealed class LinkedRoofSurfaceService
    {
        /// <summary>How far outside a roof's top face a point may be and still snap to the face edge (0.5 m).</summary>
        private const double EdgeSnapDistanceFt = 1.6404;

        /// <summary>Elevation slack when deciding whether a linked surface is "above" the host point (about 3 mm).</summary>
        private const double AboveToleranceFt = 0.01;

        private readonly List<LinkData> _links = new();
        private readonly bool _boundaryPointsOnly;

        /// <summary>Messages about linked roofs that could not be read.</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>
        /// Reads the roofs of each link in <paramref name="linkInstances"/> once, so every point lookup is cheap.
        /// </summary>
        /// <param name="linkInstances">Loaded link instances chosen by the user.</param>
        /// <param name="boundaryPointsOnly">When true, "nearest point" only considers corner and edge points of the linked roof.</param>
        public LinkedRoofSurfaceService(IEnumerable<RevitLinkInstance> linkInstances, bool boundaryPointsOnly)
        {
            _boundaryPointsOnly = boundaryPointsOnly;
            foreach (RevitLinkInstance instance in linkInstances)
            {
                Document linkDoc = instance.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform toHost = instance.GetTotalTransform();
                var data = new LinkData { Name = instance.Name, ToHost = toHost, ToLink = toHost.Inverse };

                var roofs = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Roofs)
                    .WhereElementIsNotElementType()
                    .OfType<RoofBase>();

                foreach (RoofBase roof in roofs)
                {
                    try
                    {
                        LinkedRoof read = ReadRoof(roof, data.Name);
                        if (read != null) data.Roofs.Add(read);
                    }
                    catch (Exception ex)
                    {
                        Warnings.Add($"{instance.Name}: roof {roof.Id.Value} skipped ({ex.Message})");
                    }
                }
                _links.Add(data);
            }
        }

        /// <summary>Number of readable roofs in the link instance <paramref name="instanceName"/>.</summary>
        public static int CountRoofs(RevitLinkInstance instance)
        {
            Document linkDoc = instance.GetLinkDocument();
            if (linkDoc == null) return 0;
            return new FilteredElementCollector(linkDoc)
                .OfCategory(BuiltInCategory.OST_Roofs)
                .WhereElementIsNotElementType()
                .OfType<RoofBase>()
                .Count();
        }

        /// <summary>
        /// Looks up the elevations above <paramref name="hostPoint"/>. Returns a candidate with no values
        /// (and a note) when no linked roof is there.
        /// </summary>
        public LinkedCandidate Find(XYZ hostPoint)
        {
            Hit best = null;
            double bestSnap = double.MaxValue;
            foreach (LinkData link in _links)
            {
                XYZ p = link.ToLink.OfPoint(hostPoint);
                foreach (LinkedRoof roof in link.Roofs)
                {
                    if (!roof.Contains(p.X, p.Y, EdgeSnapDistanceFt)) continue;
                    foreach (Face face in roof.TopFaces)
                    {
                        Hit hit = Intersect(link, roof, face, p, hostPoint.Z);
                        if (hit == null) continue;
                        if (hit.Snap > 0)
                        {
                            if (best == null || (best.Snap > 0 && hit.Snap < bestSnap))
                            {
                                best = hit;
                                bestSnap = hit.Snap;
                            }
                        }
                        else if (best == null || best.Snap > 0 || IsBetter(hit.ZHost, best.ZHost, hostPoint.Z))
                        {
                            best = hit;
                        }
                    }
                }
            }

            if (best == null)
                return new LinkedCandidate { Note = "No linked roof at this X,Y" };

            var notes = new List<string>();
            if (best.Snap > 0) notes.Add("outside the top face: snapped to its edge");
            if (best.ZHost < hostPoint.Z - AboveToleranceFt) notes.Add("linked roof is below this point");

            double? nearestZ = null;
            double? nearestDist = null;
            XYZ q = best.Link.ToLink.OfPoint(hostPoint);
            foreach (LinkedVertex v in best.Roof.Vertices)
            {
                if (_boundaryPointsOnly && v.Type == SlabShapeVertexType.Interior) continue;
                double d = Math.Sqrt((v.Position.X - q.X) * (v.Position.X - q.X) + (v.Position.Y - q.Y) * (v.Position.Y - q.Y));
                if (nearestDist == null || d < nearestDist)
                {
                    nearestDist = d;
                    nearestZ = best.Link.ToHost.OfPoint(v.Position).Z;
                }
            }
            if (nearestZ == null) notes.Add("linked roof has no shape-editing points");

            return new LinkedCandidate
            {
                LinkedRoof = $"{best.Link.Name} · {best.Roof.Id}",
                SurfaceZ = best.ZHost,
                NearestZ = nearestZ,
                NearestDistanceFt = nearestDist,
                Note = string.Join("; ", notes)
            };
        }

        /// <summary>Prefers the lowest surface at or above the point; if none is above, the closest in elevation.</summary>
        private static bool IsBetter(double candidate, double current, double hostZ)
        {
            bool candAbove = candidate >= hostZ - AboveToleranceFt;
            bool currAbove = current >= hostZ - AboveToleranceFt;
            if (candAbove != currAbove) return candAbove;
            return candAbove ? candidate < current : Math.Abs(candidate - hostZ) < Math.Abs(current - hostZ);
        }

        private static Hit Intersect(LinkData link, LinkedRoof roof, Face face, XYZ p, double hostZ)
        {
            try
            {
                var line = Line.CreateBound(new XYZ(p.X, p.Y, roof.MinZ - 1), new XYZ(p.X, p.Y, roof.MaxZ + 1));
                face.Intersect(line, out IntersectionResultArray result);
                if (result != null && !result.IsEmpty)
                {
                    double z = double.MinValue;
                    foreach (IntersectionResult r in result) z = Math.Max(z, r.XYZPoint.Z);
                    return new Hit(link, roof, link.ToHost.OfPoint(new XYZ(p.X, p.Y, z)).Z, 0);
                }

                // Point just outside the top face (e.g. on the roof outline): snap to the nearest face point.
                var flat = new XYZ(p.X, p.Y, (roof.MinZ + roof.MaxZ) / 2);
                IntersectionResult near = face.Project(flat);
                if (near == null) return null;
                double dx = near.XYZPoint.X - p.X, dy = near.XYZPoint.Y - p.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > EdgeSnapDistanceFt) return null;
                return new Hit(link, roof, link.ToHost.OfPoint(near.XYZPoint).Z, Math.Max(dist, 1e-9));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private static LinkedRoof ReadRoof(RoofBase roof, string linkName)
        {
            BoundingBoxXYZ box = roof.get_BoundingBox(null);
            if (box == null) return null;

            var faces = new List<Face>();
            foreach (Reference reference in HostObjectUtils.GetTopFaces(roof))
            {
                if (roof.GetGeometryObjectFromReference(reference) is not Face face) continue;
                BoundingBoxUV uv = face.GetBoundingBox();
                XYZ normal = face.ComputeNormal(new UV((uv.Min.U + uv.Max.U) / 2, (uv.Min.V + uv.Max.V) / 2));
                if (normal.Z > 1e-6) faces.Add(face);   // sky-facing only
            }
            if (faces.Count == 0) return null;

            var vertices = new List<LinkedVertex>();
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor != null && editor.IsEnabled)
            {
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    if (v?.Position != null) vertices.Add(new LinkedVertex(v.Position, v.VertexType));
            }
            return new LinkedRoof(roof.Id.Value, box, faces, vertices);
        }

        private sealed class LinkData
        {
            public string Name;
            public Transform ToHost;
            public Transform ToLink;
            public List<LinkedRoof> Roofs = new();
        }

        private sealed class LinkedRoof
        {
            private readonly BoundingBoxXYZ _box;
            public long Id { get; }
            public List<Face> TopFaces { get; }
            public List<LinkedVertex> Vertices { get; }
            public double MinZ => _box.Min.Z;
            public double MaxZ => _box.Max.Z;

            public LinkedRoof(long id, BoundingBoxXYZ box, List<Face> faces, List<LinkedVertex> vertices)
            {
                Id = id; _box = box; TopFaces = faces; Vertices = vertices;
            }

            public bool Contains(double x, double y, double margin) =>
                x >= _box.Min.X - margin && x <= _box.Max.X + margin &&
                y >= _box.Min.Y - margin && y <= _box.Max.Y + margin;
        }

        private readonly record struct LinkedVertex(XYZ Position, SlabShapeVertexType Type);

        private sealed class Hit
        {
            public readonly LinkData Link;
            public readonly LinkedRoof Roof;
            public readonly double ZHost;
            public readonly double Snap;   // 0 = on the face at the same X,Y; >0 = horizontal snap distance

            public Hit(LinkData link, LinkedRoof roof, double zHost, double snap)
            {
                Link = link; Roof = roof; ZHost = zHost; Snap = snap;
            }
        }
    }
}
