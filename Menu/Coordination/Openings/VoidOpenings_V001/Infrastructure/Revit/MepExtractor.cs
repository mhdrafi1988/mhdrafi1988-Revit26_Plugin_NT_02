using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Turns MEP elements into centreline snapshots in host millimetres. Pipes, ducts, conduits and
    /// cable trays use their location curve; fittings and accessories use the lines from their
    /// connectors to their insertion point.
    /// </summary>
    public static class MepExtractor
    {
        /// <summary>Reads the elements of the given categories from <paramref name="document"/>.</summary>
        /// <param name="document">The model to read (host or linked document).</param>
        /// <param name="toHost">Transform of that model into the host; identity for the host.</param>
        /// <param name="categories">Categories to read.</param>
        /// <param name="notes">Receives one line per category for elements that could not be analysed.</param>
        public static List<MepElementData> Read(Document document, Transform toHost,
            IEnumerable<MepCategory> categories, ICollection<string> notes)
        {
            var result = new List<MepElementData>();
            foreach (MepCategory category in categories)
            {
                int skipped = 0;
                var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>(CategoryMap.Of(category)));
                foreach (Element element in new FilteredElementCollector(document).WherePasses(filter).WhereElementIsNotElementType())
                {
                    List<MepSegment> segments = null;
                    try
                    {
                        segments = element is MEPCurve curve ? CurveSegments(curve, toHost)
                            : element is FamilyInstance instance ? FittingSegments(instance, toHost)
                            : null;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // An element whose connectors or curve cannot be read is reported, not fatal.
                    }

                    if (segments == null || segments.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    result.Add(new MepElementData(element.UniqueId, element.Id.Value, category, segments)
                    {
                        InsulationMm = InsulationMm(document, element),
                    });
                }

                if (skipped > 0)
                    notes?.Add($"{skipped} element(s) in {CategoryCatalog.NameOf(category)} could not be analysed (no usable connectors or centreline).");
            }
            return result;
        }

        private static List<MepSegment> CurveSegments(MEPCurve curve, Transform toHost)
        {
            Curve centreline = (curve.Location as LocationCurve)?.Curve;
            Connector connector = Connectors(curve.ConnectorManager).FirstOrDefault();
            if (centreline == null || connector == null)
                return null;

            MepProfile profile = ProfileOf(connector);
            if (profile == null)
                return null;
            Vec3 profileX = UnitsMm.Direction(connector.CoordinateSystem.BasisX, toHost);

            IList<XYZ> points = centreline is Line
                ? new List<XYZ> { centreline.GetEndPoint(0), centreline.GetEndPoint(1) }
                : centreline.Tessellate();

            var segments = new List<MepSegment>();
            for (int i = 0; i + 1 < points.Count; i++)
                segments.Add(new MepSegment(
                    new Segment3(UnitsMm.Point(points[i], toHost), UnitsMm.Point(points[i + 1], toHost)), profile, profileX));
            return segments;
        }

        private static List<MepSegment> FittingSegments(FamilyInstance instance, Transform toHost)
        {
            List<Connector> connectors = Connectors(instance.MEPModel?.ConnectorManager).ToList();
            if (connectors.Count < 2)
                return null;

            XYZ centre = (instance.Location as LocationPoint)?.Point
                ?? connectors.Aggregate(XYZ.Zero, (sum, c) => sum + c.Origin) / connectors.Count;
            Vec3 centreMm = UnitsMm.Point(centre, toHost);

            var segments = new List<MepSegment>();
            for (int i = 0; i < connectors.Count; i++)
            {
                MepProfile profile = ProfileOf(connectors[i]);
                if (profile == null)
                    continue;
                Vec3 origin = UnitsMm.Point(connectors[i].Origin, toHost);
                Vec3 profileX = UnitsMm.Direction(connectors[i].CoordinateSystem.BasisX, toHost);

                // The first leg runs into the fitting and the others out of it, so the legs form one path.
                Segment3 axis = i == 0 ? new Segment3(origin, centreMm) : new Segment3(centreMm, origin);
                if (axis.Length > 0.5)
                    segments.Add(new MepSegment(axis, profile, profileX));
            }
            return segments;
        }

        private static IEnumerable<Connector> Connectors(ConnectorManager manager)
        {
            if (manager == null)
                yield break;
            foreach (Connector connector in manager.Connectors)
                if (connector.ConnectorType == ConnectorType.End)
                    yield return connector;
        }

        private static MepProfile ProfileOf(Connector connector)
        {
            switch (connector.Shape)
            {
                case ConnectorProfileType.Round:
                    return connector.Radius > 0 ? MepProfile.Round(UnitsMm.ToMm(connector.Radius * 2)) : null;
                case ConnectorProfileType.Rectangular:
                case ConnectorProfileType.Oval:
                    return connector.Width > 0 && connector.Height > 0
                        ? MepProfile.Rectangular(UnitsMm.ToMm(connector.Width), UnitsMm.ToMm(connector.Height))
                        : null;
                default:
                    return null;
            }
        }

        private static double InsulationMm(Document document, Element element)
        {
            try
            {
                double thickness = 0;
                foreach (ElementId id in InsulationLiningBase.GetInsulationIds(document, element.Id))
                    if (document.GetElement(id) is InsulationLiningBase insulation)
                        thickness = Math.Max(thickness, insulation.Thickness);
                return UnitsMm.ToMm(thickness);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0; // categories that cannot carry insulation
            }
        }
    }
}
