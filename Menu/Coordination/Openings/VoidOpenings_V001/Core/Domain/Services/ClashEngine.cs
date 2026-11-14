using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
    /// <summary>
    /// Finds where MEP elements pass through structural elements. Works on snapshots only, so it can
    /// run on a background thread. Bounding boxes pick the candidates; the result always comes from
    /// intersecting the MEP centreline (and body) with the structural mesh.
    /// </summary>
    public sealed class ClashEngine
    {
        private const double GridCellMm = 2000;

        // A crossing shorter than this is a graze along an edge, not a penetration.
        private const double MinPenetrationMm = 0.5;

        /// <summary>Detects clashes between the two models.</summary>
        /// <param name="structural">Structural elements with their meshes.</param>
        /// <param name="mep">MEP elements with their centrelines.</param>
        /// <param name="progress">Receives 0..1 as MEP elements are processed; may be null.</param>
        /// <param name="cancellationToken">Stops the run with an <see cref="OperationCanceledException"/>.</param>
        /// <returns>One entry per penetration, in MEP element order and then along each element.</returns>
        public IReadOnlyList<ClashData> Detect(StructuralModel structural, MepModel mep,
            IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (structural == null) throw new ArgumentNullException(nameof(structural));
            if (mep == null) throw new ArgumentNullException(nameof(mep));

            var result = new List<ClashData>();
            var grid = new SpatialGrid(structural.Elements.Select(e => e.Bounds).ToList(), GridCellMm);

            for (int i = 0; i < mep.Elements.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.AddRange(DetectElement(structural, mep.Ref, mep.Elements[i], grid));
                progress?.Report((i + 1.0) / mep.Elements.Count);
            }

            progress?.Report(1.0);
            return result;
        }

        private static IEnumerable<ClashData> DetectElement(StructuralModel structural, ModelRef mepRef,
            MepElementData element, SpatialGrid grid)
        {
            var found = new List<Penetration>();

            for (int s = 0; s < element.Segments.Count; s++)
            {
                MepSegment segment = element.Segments[s];
                if (segment.Axis.Length < MinPenetrationMm)
                    continue;

                double reach = HalfSize(segment.Profile) + element.InsulationMm;
                foreach (int structuralIndex in grid.Query(segment.Axis.Bounds.Expand(reach)))
                {
                    TriangleMesh mesh = structural.Elements[structuralIndex].Mesh;
                    List<Interval> intervals = Intervals(segment.Axis, mesh);
                    if (intervals.Count > 0)
                    {
                        found.AddRange(intervals.Select(iv => new Penetration(structuralIndex, s, iv, false)));
                        continue;
                    }

                    Interval? body = BodyInterval(segment, element.InsulationMm, mesh);
                    if (body.HasValue)
                        found.Add(new Penetration(structuralIndex, s, body.Value, true));
                }
            }

            var clashes = new List<(int Segment, double T, ClashData Clash)>();
            foreach (IGrouping<int, Penetration> group in found.GroupBy(p => p.StructuralIndex))
            {
                StructuralElementData structuralElement = structural.Elements[group.Key];
                int ordinal = 0;
                foreach (Penetration p in MergeAcrossSegments(group.OrderBy(x => x.EntrySegment).ThenBy(x => x.TEnter)))
                {
                    MepSegment entrySegment = element.Segments[p.EntrySegment];
                    Vec3 entry = entrySegment.Axis.PointAt(p.TEnter);
                    Vec3 exit = element.Segments[p.ExitSegment].Axis.PointAt(p.TExit);
                    Vec3 chord = exit - entry;

                    clashes.Add((p.EntrySegment, p.TEnter, new ClashData
                    {
                        Key = new ClashKey(structural.Ref.ModelKey, structuralElement.UniqueId, mepRef.ModelKey, element.UniqueId, ordinal++),
                        StructuralModel = structural.Ref,
                        Structural = structuralElement,
                        MepModel = mepRef,
                        Mep = element,
                        Segment = entrySegment,
                        EntryPoint = entry,
                        ExitPoint = exit,
                        MepDirection = chord.Length > 1e-6 ? chord.Normalized() : entrySegment.Axis.Direction,
                        EntryNormal = p.EntryNormal,
                        IsPartial = p.StartsInside || p.EndsInside,
                        IsBodyOnly = p.BodyOnly,
                    }));
                }
            }

            return clashes.OrderBy(c => c.Segment).ThenBy(c => c.T).Select(c => c.Clash);
        }

        /// <summary>Parts of the segment that lie inside the mesh.</summary>
        private static List<Interval> Intervals(Segment3 segment, TriangleMesh mesh)
        {
            var intervals = new List<Interval>();
            Aabb reach = segment.Bounds.Expand(MinPenetrationMm);
            if (!reach.Intersects(mesh.Bounds))
                return intervals;

            var hits = new List<Hit>();
            foreach (Triangle triangle in mesh.Triangles)
            {
                if (Intersect.SegmentTriangle(segment, triangle, out double t, out bool entering))
                    hits.Add(new Hit(t, entering, triangle.Normal));
            }
            if (hits.Count == 0)
                return intervals;

            hits.Sort((a, b) => a.T.CompareTo(b.T));
            double length = segment.Length;
            Vec3 direction = segment.Direction;

            // A first hit that leaves the solid means the segment started inside it.
            bool inside = !hits[0].Entering;
            bool startedInside = inside;
            double enterT = 0;
            Vec3 enterNormal = -direction;

            // A crossing on an edge shared by two triangles is reported twice; the second report
            // finds the state already changed and is ignored.
            foreach (Hit hit in hits)
            {
                if (hit.Entering && !inside)
                {
                    inside = true;
                    startedInside = false;
                    enterT = hit.T;
                    enterNormal = hit.Normal;
                }
                else if (!hit.Entering && inside)
                {
                    inside = false;
                    if ((hit.T - enterT) * length >= MinPenetrationMm)
                        intervals.Add(new Interval(enterT, hit.T, startedInside, false, enterNormal));
                    startedInside = false;
                }
            }

            if (inside && (1 - enterT) * length >= MinPenetrationMm)
                intervals.Add(new Interval(enterT, 1, startedInside, true, enterNormal));

            return intervals;
        }

        /// <summary>
        /// Where the body of the element is inside the mesh although its centreline is not: rays along
        /// the rim of the cross-section are tested, and the result is mapped back to the centreline.
        /// </summary>
        private static Interval? BodyInterval(MepSegment segment, double insulationMm, TriangleMesh mesh)
        {
            Interval? union = null;
            foreach (Vec3 offset in RimOffsets(segment, insulationMm))
            {
                foreach (Interval iv in Intervals(segment.Axis.Offset(offset), mesh))
                {
                    if (!union.HasValue)
                    {
                        union = iv;
                        continue;
                    }
                    Interval u = union.Value;
                    union = new Interval(
                        Math.Min(u.TEnter, iv.TEnter),
                        Math.Max(u.TExit, iv.TExit),
                        u.TEnter <= iv.TEnter ? u.StartsInside : iv.StartsInside,
                        u.TExit >= iv.TExit ? u.EndsInside : iv.EndsInside,
                        u.TEnter <= iv.TEnter ? u.EntryNormal : iv.EntryNormal);
                }
            }
            return union;
        }

        private static IEnumerable<Vec3> RimOffsets(MepSegment segment, double insulationMm)
        {
            Vec3 x = segment.ProfileX;
            Vec3 y = segment.ProfileY;
            if (segment.Profile.Shape == ProfileShape.Round)
            {
                double radius = segment.Profile.DiameterMm / 2 + insulationMm;
                for (int k = 0; k < 8; k++)
                {
                    double angle = k * Math.PI / 4;
                    yield return x * (radius * Math.Cos(angle)) + y * (radius * Math.Sin(angle));
                }
                yield break;
            }

            double halfWidth = segment.Profile.WidthMm / 2 + insulationMm;
            double halfHeight = segment.Profile.HeightMm / 2 + insulationMm;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (i != 0 || j != 0)
                        yield return x * (i * halfWidth) + y * (j * halfHeight);
        }

        /// <summary>Joins a penetration that runs on from one segment of the element into the next.</summary>
        private static IEnumerable<Penetration> MergeAcrossSegments(IEnumerable<Penetration> ordered)
        {
            Penetration? current = null;
            foreach (Penetration next in ordered)
            {
                if (current.HasValue && current.Value.EndsInside && next.StartsInside
                    && next.EntrySegment == current.Value.ExitSegment + 1)
                {
                    current = current.Value.ExtendedBy(next);
                    continue;
                }
                if (current.HasValue)
                    yield return current.Value;
                current = next;
            }
            if (current.HasValue)
                yield return current.Value;
        }

        private static double HalfSize(MepProfile profile)
            => profile.Shape == ProfileShape.Round
                ? profile.DiameterMm / 2
                : Math.Sqrt(profile.WidthMm * profile.WidthMm + profile.HeightMm * profile.HeightMm) / 2;

        private readonly struct Hit
        {
            public readonly double T;
            public readonly bool Entering;
            public readonly Vec3 Normal;

            public Hit(double t, bool entering, Vec3 normal)
            {
                T = t;
                Entering = entering;
                Normal = normal;
            }
        }

        private readonly struct Interval
        {
            public readonly double TEnter;
            public readonly double TExit;
            public readonly bool StartsInside;
            public readonly bool EndsInside;
            public readonly Vec3 EntryNormal;

            public Interval(double tEnter, double tExit, bool startsInside, bool endsInside, Vec3 entryNormal)
            {
                TEnter = tEnter;
                TExit = tExit;
                StartsInside = startsInside;
                EndsInside = endsInside;
                EntryNormal = entryNormal;
            }
        }

        private readonly struct Penetration
        {
            public readonly int StructuralIndex;
            public readonly int EntrySegment;
            public readonly int ExitSegment;
            public readonly double TEnter;
            public readonly double TExit;
            public readonly bool StartsInside;
            public readonly bool EndsInside;
            public readonly bool BodyOnly;
            public readonly Vec3 EntryNormal;

            public Penetration(int structuralIndex, int segment, Interval interval, bool bodyOnly)
                : this(structuralIndex, segment, segment, interval.TEnter, interval.TExit,
                    interval.StartsInside, interval.EndsInside, bodyOnly, interval.EntryNormal)
            {
            }

            private Penetration(int structuralIndex, int entrySegment, int exitSegment, double tEnter, double tExit,
                bool startsInside, bool endsInside, bool bodyOnly, Vec3 entryNormal)
            {
                StructuralIndex = structuralIndex;
                EntrySegment = entrySegment;
                ExitSegment = exitSegment;
                TEnter = tEnter;
                TExit = tExit;
                StartsInside = startsInside;
                EndsInside = endsInside;
                BodyOnly = bodyOnly;
                EntryNormal = entryNormal;
            }

            public Penetration ExtendedBy(Penetration next) => new Penetration(
                StructuralIndex, EntrySegment, next.ExitSegment, TEnter, next.TExit,
                StartsInside, next.EndsInside, BodyOnly || next.BodyOnly, EntryNormal);
        }
    }
}
