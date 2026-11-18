using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Turns a pipe, duct, cable tray, conduit or one of their fittings into a snapshot: centreline
    /// segments with a cross-section, in host millimetres.
    /// Runs (pipes, ducts, trays, conduits, flex) use their location curve; curved ones are split into
    /// short straight segments. Fittings and accessories have no centreline, so theirs is built from
    /// their connectors: straight from one connector to the other when the two face away from each
    /// other, otherwise through the fitting's insertion point.
    /// </summary>
    public static class MepExtractor
    {
        // Segments shorter than this carry no information and only slow the clash test down.
        private const double MinSegmentMm = 1.0;

        /// <summary>Reads one element; returns null and a <paramref name="reason"/> when it cannot be reduced to a centreline.</summary>
        public static MepElementData TryRead(Element element, MepCategory category, Transform toHost, out string reason)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (toHost == null) throw new ArgumentNullException(nameof(toHost));

            List<MepSegment> segments;
            if (element is MEPCurve curve)
                segments = CurveSegments(curve, toHost, out reason);
            else if (element is FamilyInstance instance)
                segments = FittingSegments(instance, toHost, out reason);
            else
            {
                reason = "It is not a pipe, duct, cable tray, conduit or one of their fittings.";
                return null;
            }

            if (segments == null)
                return null;
            if (segments.Count == 0)
            {
                reason = "Its centreline is too short to analyse.";
                return null;
            }

            reason = null;
            return new MepElementData(element.UniqueId, element.Id.Value, category, segments)
            {
                InsulationMm = InsulationOf(element),
            };
        }

        private static List<MepSegment> CurveSegments(MEPCurve curve, Transform toHost, out string reason)
        {
            Curve centreline = (curve.Location as LocationCurve)?.Curve;
            if (centreline == null)
            {
                reason = "It has no centreline.";
                return null;
            }

            MepProfile profile = ProfileOf(curve, out XYZ widthDirection);
            if (profile == null)
            {
                reason = "Its size could not be read.";
                return null;
            }

            IList<XYZ> points = centreline is Line
                ? new List<XYZ> { centreline.GetEndPoint(0), centreline.GetEndPoint(1) }
                : centreline.Tessellate();

            Vec3? profileX = widthDirection == null ? (Vec3?)null : UnitsMm.Direction(widthDirection, toHost);
            var segments = new List<MepSegment>();
            for (int i = 0; i + 1 < points.Count; i++)
                AddSegment(segments, UnitsMm.Point(points[i], toHost), UnitsMm.Point(points[i + 1], toHost), profile, profileX);
            reason = null;
            return segments;
        }

        private static List<MepSegment> FittingSegments(FamilyInstance instance, Transform toHost, out string reason)
        {
            List<Connector> connectors = PhysicalConnectors(instance.MEPModel?.ConnectorManager);
            if (connectors.Count < 2)
            {
                reason = "It has fewer than two connectors, so it has no centreline.";
                return null;
            }

            var profiles = new List<MepProfile>();
            foreach (Connector connector in connectors)
            {
                MepProfile profile = ProfileOf(connector);
                if (profile == null)
                {
                    reason = "The size of one of its connectors could not be read.";
                    return null;
                }
                profiles.Add(profile);
            }

            var segments = new List<MepSegment>();
            reason = null;

            if (connectors.Count == 2 && FaceAway(connectors[0], connectors[1]))
            {
                // Straight through (coupling, valve, transition): one piece, sized by the larger end.
                int larger = profiles[0].NominalSizeMm >= profiles[1].NominalSizeMm ? 0 : 1;
                AddSegment(segments, UnitsMm.Point(connectors[0].Origin, toHost), UnitsMm.Point(connectors[1].Origin, toHost),
                    profiles[larger], WidthDirection(connectors[larger], toHost));
                return segments;
            }

            // Elbow, tee, cross: every connector runs to the middle of the fitting.
            Vec3 hub = HubOf(instance, connectors, toHost);
            AddSegment(segments, UnitsMm.Point(connectors[0].Origin, toHost), hub, profiles[0], WidthDirection(connectors[0], toHost));
            for (int i = 1; i < connectors.Count; i++)
                AddSegment(segments, hub, UnitsMm.Point(connectors[i].Origin, toHost), profiles[i], WidthDirection(connectors[i], toHost));
            return segments;
        }

        private static void AddSegment(List<MepSegment> segments, Vec3 start, Vec3 end, MepProfile profile, Vec3? profileX)
        {
            if ((end - start).Length >= MinSegmentMm)
                segments.Add(new MepSegment(new Segment3(start, end), profile, profileX));
        }

        private static List<Connector> PhysicalConnectors(ConnectorManager manager)
        {
            var result = new List<Connector>();
            ConnectorSet set = manager?.Connectors;
            if (set == null)
                return result;
            foreach (Connector connector in set)
            {
                if (connector != null && connector.ConnectorType == ConnectorType.End)
                    result.Add(connector);
            }
            return result;
        }

        /// <summary>True when the two connectors point in opposite directions, within about 5 degrees.</summary>
        private static bool FaceAway(Connector a, Connector b)
        {
            try
            {
                return a.CoordinateSystem.BasisZ.DotProduct(b.CoordinateSystem.BasisZ) < -0.996;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Vec3 HubOf(FamilyInstance instance, List<Connector> connectors, Transform toHost)
        {
            XYZ centroid = XYZ.Zero;
            foreach (Connector connector in connectors)
                centroid += connector.Origin;
            centroid /= connectors.Count;

            // The insertion point is the true corner of an elbow; a family inserted away from its
            // body would send the centreline off, so it is only trusted when it lies among the connectors.
            XYZ insertion = (instance.Location as LocationPoint)?.Point;
            if (insertion != null)
            {
                double spread = connectors.Max(c => c.Origin.DistanceTo(centroid));
                if (insertion.DistanceTo(centroid) <= spread + 1e-6)
                    return UnitsMm.Point(insertion, toHost);
            }
            return UnitsMm.Point(centroid, toHost);
        }

        /// <summary>
        /// Cross-section of a run: the outside diameter of a pipe or conduit, otherwise what its
        /// connector says. <paramref name="widthDirection"/> is the direction of a rectangular
        /// section's width in the model's coordinates; null when round or unknown.
        /// </summary>
        private static MepProfile ProfileOf(MEPCurve curve, out XYZ widthDirection)
        {
            widthDirection = null;

            double outside = PositiveLength(curve, BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
            if (outside <= 0) outside = PositiveLength(curve, BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM);
            if (outside > 0)
                return MepProfile.Round(UnitsMm.Length(outside));

            Connector connector = PhysicalConnectors(curve.ConnectorManager).FirstOrDefault();
            if (connector != null)
            {
                MepProfile profile = ProfileOf(connector);
                if (profile != null)
                {
                    if (profile.Shape == ProfileShape.Rectangular)
                    {
                        try { widthDirection = connector.CoordinateSystem.BasisX; }
                        catch (Exception) { widthDirection = null; }
                    }
                    return profile;
                }
            }

            // No usable connector: the element's own size properties. Each throws when it does not apply.
            try { if (curve.Diameter > 0) return MepProfile.Round(UnitsMm.Length(curve.Diameter)); }
            catch (Exception) { /* not round */ }
            try { if (curve.Width > 0 && curve.Height > 0) return MepProfile.Rectangular(UnitsMm.Length(curve.Width), UnitsMm.Length(curve.Height)); }
            catch (Exception) { /* not rectangular */ }
            return null;
        }

        private static MepProfile ProfileOf(Connector connector)
        {
            try
            {
                switch (connector.Shape)
                {
                    case ConnectorProfileType.Round:
                        return connector.Radius > 0 ? MepProfile.Round(UnitsMm.Length(connector.Radius * 2)) : null;
                    case ConnectorProfileType.Rectangular:
                    case ConnectorProfileType.Oval:
                        return connector.Width > 0 && connector.Height > 0
                            ? MepProfile.Rectangular(UnitsMm.Length(connector.Width), UnitsMm.Length(connector.Height))
                            : null;
                    default:
                        return null;
                }
            }
            catch (Exception)
            {
                return null; // the connector does not expose a size (wrong domain or shape)
            }
        }

        private static Vec3? WidthDirection(Connector connector, Transform toHost)
        {
            try
            {
                return connector.Shape == ConnectorProfileType.Round ? (Vec3?)null : UnitsMm.Direction(connector.CoordinateSystem.BasisX, toHost);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double PositiveLength(Element element, BuiltInParameter parameter)
        {
            Parameter p = element.get_Parameter(parameter);
            return p != null && p.HasValue && p.StorageType == StorageType.Double ? Math.Max(0, p.AsDouble()) : 0;
        }

        /// <summary>Thickness of the thickest insulation on the element, in millimetres; 0 when it has none.</summary>
        private static double InsulationOf(Element element)
        {
            try
            {
                double thickest = 0;
                foreach (ElementId id in InsulationLiningBase.GetInsulationIds(element.Document, element.Id))
                {
                    if (element.Document.GetElement(id) is InsulationLiningBase insulation)
                        thickest = Math.Max(thickest, insulation.Thickness);
                }
                return UnitsMm.Length(thickest);
            }
            catch (Exception)
            {
                return 0; // categories that cannot carry insulation (trays, conduits)
            }
        }
    }
}
