// Merge Roofs V001 — draws the merged profile, gap links and issue circles as detail curves in the current view.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Edge = Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models.Edge;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers
{
    /// <summary>Detail-curve output for the Merge Roofs tool.</summary>
    public static class DetailDrawer
    {
        /// <summary>Radius of an issue-marking circle, in feet (about 150 mm).</summary>
        public const double CircleRadius = 0.5;

        /// <summary>Draws the merged outer and interior loops as detail lines/arcs. <paramref name="check"/> (optional) lets the caller cancel; a cancel rolls the drawing back.</summary>
        public static void DrawProfile(Document doc, View view, MergeResult merged, Action? check = null) =>
            Draw(doc, view, "Merge roofs: profile lines", check, z =>
                merged.Outer.Concat(merged.Holes).SelectMany(l => l.Edges).Select(e => EdgeCurve(e, z)));

        /// <summary>Draws the connecting lines across snapped gaps.</summary>
        public static void DrawGapLinks(Document doc, View view, IReadOnlyList<GapLink> links, Action? check = null) =>
            Draw(doc, view, "Merge roofs: gap links", check, z =>
                links.Select(g => (Curve?)Line.CreateBound(new XYZ(g.A.X, g.A.Y, z), new XYZ(g.B.X, g.B.Y, z))));

        /// <summary>Draws a circle at each issue location (two half arcs: Revit cannot create a single 360-degree arc).</summary>
        public static void DrawCircles(Document doc, View view, IReadOnlyList<Issue> issues, Action? check = null) =>
            Draw(doc, view, "Merge roofs: issue circles", check, z =>
                issues.SelectMany(i =>
                {
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(i.Location.X, i.Location.Y, z));
                    return new Curve?[]
                    {
                        Arc.Create(plane, CircleRadius, 0, Math.PI),
                        Arc.Create(plane, CircleRadius, Math.PI, 2 * Math.PI),
                    };
                }));

        private static void Draw(Document doc, View view, string name, Action? check, Func<double, IEnumerable<Curve?>> curves)
        {
            if (view is not ViewPlan)
                throw new InvalidOperationException("Detail lines can only be placed in a plan view. Open a plan view and try again.");
            double z = view.Origin.Z;
            using var t = new Transaction(doc, name);
            t.Start();
            try
            {
                foreach (Curve? c in curves(z))
                {
                    check?.Invoke();
                    if (c != null && c.Length >= MergeAnalyzer.ShortCurve) doc.Create.NewDetailCurve(view, c);
                }
                t.Commit();
            }
            catch
            {
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                throw;
            }
        }

        private static Curve? EdgeCurve(Edge e, double z)
        {
            XYZ a = new(e.A.X, e.A.Y, z), b = new(e.B.X, e.B.Y, z);
            if (a.DistanceTo(b) < MergeAnalyzer.ShortCurve) return null;
            return e.ArcMid is Pt m ? Arc.Create(a, b, new XYZ(m.X, m.Y, z)) : Line.CreateBound(a, b);
        }
    }
}
