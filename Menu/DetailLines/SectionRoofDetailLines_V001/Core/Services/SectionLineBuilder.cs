using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>One element's cut profile in a section, as curves lying in the view's plane.</summary>
    public sealed class CutProfile
    {
        /// <summary>Id of the element the profile came from, in its own document.</summary>
        public long ElementId { get; set; }

        /// <summary>A label for the log: category, type and id.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The profile's curves, in model coordinates, in the view's plane.</summary>
        public List<Curve> Curves { get; set; } = new();

        /// <summary>The curves' points, tessellated once, for the distance tests.</summary>
        public List<XYZ> Points { get; set; } = new();
    }

    /// <summary>
    /// Turns cut profiles into the lines the tool draws: a roof's whole cut profile, and one
    /// vertical line per face of a wall that stands next to a roof.
    /// <para>
    /// Everything here is pure geometry on curves already in the view's plane; it creates nothing
    /// in the model. <see cref="SectionDetailLineWriter"/> does that.
    /// </para>
    /// </summary>
    public sealed class SectionLineBuilder
    {
        private const double Feet = 304.8;

        /// <summary>How parallel a cut edge must be to the view's up direction to be read as a
        /// wall face, as the absolute dot product of the two unit vectors (about 0.6°).</summary>
        private const double VerticalTolerance = 0.9999;

        /// <summary>Two vertical edges within this distance across the view, in feet, are the same
        /// face — about 1 mm.</summary>
        private const double SameFaceFeet = 1.0 / Feet;

        /// <summary>
        /// <paramref name="profiles"/> as the curves to draw for roofs: the whole cut profile,
        /// with the straight parts trimmed to the view's crop so nothing runs off the view.
        /// </summary>
        public List<Curve> BuildRoofCurves(SectionFrame frame, IEnumerable<CutProfile> profiles, double shortCurveFeet)
        {
            var curves = new List<Curve>();
            if (frame == null || profiles == null) return curves;

            foreach (CutProfile profile in profiles)
                foreach (Curve curve in profile.Curves)
                {
                    // Straight curves are trimmed to the crop; an arc or spline is kept whole,
                    // because trimming one exactly is not worth the approximation — Revit simply
                    // does not draw the part of it outside the crop.
                    Curve kept = curve is Line ? frame.ClipToBox(curve, 0.0) : curve;
                    if (kept != null && kept.Length > shortCurveFeet) curves.Add(kept);
                }

            return curves;
        }

        /// <summary>
        /// One vertical line per wall face that stands next to a roof: the face's own position
        /// across the view, run over the wall's whole visible height in the section.
        /// <para>
        /// A wall counts as adjacent when its cut profile comes within
        /// <paramref name="toleranceFeet"/> of a roof's. With
        /// <paramref name="bothFaces"/> false, only the face nearer the roof gets a line.
        /// </para>
        /// </summary>
        public List<Curve> BuildWallCurves(
            SectionFrame frame,
            IEnumerable<CutProfile> wallProfiles,
            IReadOnlyCollection<CutProfile> roofProfiles,
            double toleranceFeet,
            bool bothFaces,
            double shortCurveFeet,
            Action<string> onSkipped,
            out int wallsDrawn)
        {
            wallsDrawn = 0;
            var curves = new List<Curve>();
            if (frame == null || wallProfiles == null) return curves;

            List<XYZ> roofPoints = roofProfiles == null
                ? new List<XYZ>()
                : roofProfiles.SelectMany(p => p.Points).ToList();

            if (roofPoints.Count == 0)
            {
                onSkipped?.Invoke("No roof was cut in this section, so no wall line has a roof to sit next to.");
                return curves;
            }

            foreach (CutProfile wall in wallProfiles)
            {
                double distance = MinDistance(wall.Points, roofPoints, toleranceFeet);
                if (distance > toleranceFeet)
                {
                    onSkipped?.Invoke(
                        $"{wall.Label}: nearest roof edge is {distance * Feet:F0} mm away — further than the "
                        + $"{toleranceFeet * Feet:F0} mm adjacency tolerance, so no line.");
                    continue;
                }

                List<Curve> lines = BuildWallFaceLines(frame, wall, roofPoints, bothFaces, shortCurveFeet);
                if (lines.Count == 0)
                {
                    onSkipped?.Invoke(
                        $"{wall.Label}: its cut profile has no vertical face in this section (the section runs "
                        + "along the wall rather than across it), so no line.");
                    continue;
                }

                curves.AddRange(lines);
                wallsDrawn++;
            }

            return curves;
        }

        private List<Curve> BuildWallFaceLines(
            SectionFrame frame, CutProfile wall, List<XYZ> roofPoints, bool bothFaces, double shortCurveFeet)
        {
            XYZ up = frame.Up.Normalize();

            // The wall's visible height in this section: the up-extent of its own cut profile,
            // held inside the crop box so a line never runs past the view.
            double profileLow = double.MaxValue, profileHigh = double.MinValue;
            foreach (XYZ point in wall.Points)
            {
                double y = frame.ToBox(point).Y;
                profileLow = Math.Min(profileLow, y);
                profileHigh = Math.Max(profileHigh, y);
            }
            double low = Math.Max(profileLow, frame.BoxMin.Y);
            double high = Math.Min(profileHigh, frame.BoxMax.Y);
            if (high - low <= shortCurveFeet) return new List<Curve>();

            // Vertical cut edges, gathered into one group per face: two edges at the same position
            // across the view are the same face seen twice (a wall cut above and below an opening).
            var faces = new List<WallFace>();
            foreach (Curve curve in wall.Curves)
            {
                if (curve is not Line line) continue;
                XYZ direction = line.Direction;
                if (direction == null || direction.IsZeroLength()) continue;
                if (Math.Abs(direction.Normalize().DotProduct(up)) < VerticalTolerance) continue;

                XYZ start = line.GetEndPoint(0);
                double across = frame.ToBox(start).X;

                WallFace face = faces.FirstOrDefault(f => Math.Abs(f.Across - across) <= SameFaceFeet);
                if (face == null)
                {
                    face = new WallFace { Across = across, Seed = start };
                    faces.Add(face);
                }
                face.Distance = Math.Min(face.Distance, MinDistance(line.Tessellate(), roofPoints, double.MaxValue));
            }

            if (faces.Count == 0) return new List<Curve>();

            IEnumerable<WallFace> chosen = bothFaces
                ? faces
                : new[] { faces.OrderBy(f => f.Distance).First() };

            var lines = new List<Curve>();
            foreach (WallFace face in chosen)
            {
                // The seed point is already in the view's plane, and the up direction lies in that
                // plane too, so sliding along it keeps the line where a detail curve can go.
                double seedUp = frame.ToBox(face.Seed).Y;
                XYZ bottom = face.Seed + up * (low - seedUp);
                XYZ top = face.Seed + up * (high - seedUp);
                if (bottom.DistanceTo(top) <= shortCurveFeet) continue;
                lines.Add(Line.CreateBound(bottom, top));
            }

            return lines;
        }

        /// <summary>
        /// Shortest distance between two sets of points, stopping as soon as it is at or under
        /// <paramref name="stopAt"/> — the answer is only ever compared against the adjacency
        /// tolerance, so there is nothing to gain from refining it below that.
        /// </summary>
        private static double MinDistance(IEnumerable<XYZ> a, IReadOnlyCollection<XYZ> b, double stopAt)
        {
            double best = double.MaxValue;
            foreach (XYZ pa in a)
            {
                foreach (XYZ pb in b)
                {
                    double d = pa.DistanceTo(pb);
                    if (d < best) best = d;
                    if (best <= stopAt) return best;
                }
            }
            return best;
        }

        private sealed class WallFace
        {
            public double Across;
            public XYZ Seed;
            public double Distance = double.MaxValue;
        }
    }
}
