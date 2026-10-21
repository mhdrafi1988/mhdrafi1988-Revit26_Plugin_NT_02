using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry
{
    /// <summary>One side of an element's plan rectangle.</summary>
    public sealed class PlanSide
    {
        internal PlanSide(SideKey key, Vec2 outwardNormal, Vec2 start, Vec2 end, double length)
        {
            Key = key;
            OutwardNormal = outwardNormal;
            Start = start;
            End = end;
            Length = length;
        }

        /// <summary>A/B are the long sides, C/D the short ones.</summary>
        public SideKey Key { get; }

        /// <summary>Unit plan direction the side faces.</summary>
        public Vec2 OutwardNormal { get; }

        /// <summary>One end of the side.</summary>
        public Vec2 Start { get; }

        /// <summary>The other end of the side.</summary>
        public Vec2 End { get; }

        /// <summary>Length of the side (ft).</summary>
        public double Length { get; }

        /// <summary>True for the long sides A and B.</summary>
        public bool IsLong => Key == SideKey.A || Key == SideKey.B;

        /// <summary>8-point compass name of <see cref="OutwardNormal"/> (project north = +Y).</summary>
        public string Compass => PlanSides.CompassName(OutwardNormal);
    }

    /// <summary>
    /// Names the four sides of a plan rectangle the same way every time: A is the long side facing
    /// most to the south (−Y), B the opposite one; C is the short side facing most to the west (−X),
    /// D the opposite one.
    /// </summary>
    public static class PlanSides
    {
        private static readonly string[] Compass8 = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>The four sides of <paramref name="r"/>, in the order A, B, C, D.</summary>
        public static IReadOnlyList<PlanSide> Of(PlanRectangle r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            Vec2 a = r.Along * (r.Length / 2), b = r.Across * (r.Width / 2);

            Vec2 nA = FacingMost(r.Across, new Vec2(0, -1), new Vec2(-1, 0));
            Vec2 nC = FacingMost(r.Along, new Vec2(-1, 0), new Vec2(0, -1));

            // A long side's mid-point is centre ± (half width) along Across.
            Vec2 midA = r.Center + nA * (r.Width / 2), midB = r.Center - nA * (r.Width / 2);
            Vec2 midC = r.Center + nC * (r.Length / 2), midD = r.Center - nC * (r.Length / 2);

            return new[]
            {
                new PlanSide(SideKey.A, nA, midA - a, midA + a, r.Length),
                new PlanSide(SideKey.B, -nA, midB - a, midB + a, r.Length),
                new PlanSide(SideKey.C, nC, midC - b, midC + b, r.Width),
                new PlanSide(SideKey.D, -nC, midD - b, midD + b, r.Width),
            };
        }

        /// <summary>The side named <paramref name="key"/> of <paramref name="r"/>.</summary>
        public static PlanSide Get(PlanRectangle r, SideKey key) => Of(r)[(int)key];

        /// <summary>8-point compass name of a plan direction (project north = +Y).</summary>
        public static string CompassName(Vec2 dir)
        {
            double bearing = Math.Atan2(dir.X, dir.Y) * 180 / Math.PI; // clockwise from north
            if (bearing < 0) bearing += 360;
            return Compass8[(int)Math.Round(bearing / 45) % 8];
        }

        // ±axis, whichever points more along 'prefer' (ties broken by 'tieBreak').
        private static Vec2 FacingMost(Vec2 axis, Vec2 prefer, Vec2 tieBreak)
        {
            double d = axis.Dot(prefer);
            if (Math.Abs(d) > 1e-6) return d > 0 ? axis : -axis;
            return axis.Dot(tieBreak) >= 0 ? axis : -axis;
        }
    }

    /// <summary>How the 3D view is turned: which way it looks, and why.</summary>
    public sealed class Alignment
    {
        internal Alignment(Vec2 forward, int sourceIndex, PlanSide side, string description)
        {
            Forward = forward.Normalized();
            SourceIndex = sourceIndex;
            Side = side;
            Description = description;
        }

        /// <summary>Unit plan direction the camera looks in (towards the chosen side).</summary>
        public Vec2 Forward { get; }

        /// <summary>Unit plan direction to the camera's right: <see cref="Forward"/> × Z.</summary>
        public Vec2 Right => new(Forward.Y, -Forward.X);

        /// <summary>Index of the element whose side was used, or −1 for a picked face.</summary>
        public int SourceIndex { get; }

        /// <summary>The side used, or null for a picked face.</summary>
        public PlanSide Side { get; }

        /// <summary>One-line explanation for the log (which element it belongs to is added by the caller).</summary>
        public string Description { get; }
    }

    /// <summary>Chooses the alignment of the 3D view for the whole selection.</summary>
    public static class AlignmentPlanner
    {
        /// <summary>
        /// Index of the element with the largest side: the largest long-side area (length × height).
        /// When every element is flat (zero height), the longest rectangle wins. Ties keep the first.
        /// </summary>
        public static int LargestSideIndex(IReadOnlyList<ElementFootprint> footprints)
        {
            if (footprints == null || footprints.Count == 0)
                throw new InvalidOperationException("Select at least one element.");

            int best = 0;
            for (int i = 1; i < footprints.Count; i++)
            {
                ElementFootprint f = footprints[i], b = footprints[best];
                bool bigger = b.LongSideArea > 1e-9 || f.LongSideArea > 1e-9
                    ? f.LongSideArea > b.LongSideArea + 1e-9
                    : f.Rectangle.Length > b.Rectangle.Length + 1e-9;
                if (bigger) best = i;
            }
            return best;
        }

        /// <summary>
        /// Picks the view direction. <see cref="SideChoice.Auto"/> looks at side A of the element with
        /// the largest side; A–D look at that side of the same element; <see cref="SideChoice.PickedFace"/>
        /// looks at the face whose outward plan normal is <paramref name="pickedFaceNormal"/>.
        /// </summary>
        public static Alignment Choose(IReadOnlyList<ElementFootprint> footprints, SideChoice choice,
            Vec2? pickedFaceNormal = null)
        {
            if (choice == SideChoice.PickedFace)
            {
                if (pickedFaceNormal == null || pickedFaceNormal.Value.Length < 1e-6)
                    throw new InvalidOperationException("Pick a face first, or choose another side.");
                Vec2 n = pickedFaceNormal.Value.Normalized();
                return new Alignment(-n, -1, null, $"Looking at the picked face (faces {PlanSides.CompassName(n)})");
            }

            int index = LargestSideIndex(footprints);
            SideKey key = choice switch
            {
                SideChoice.LongB => SideKey.B,
                SideChoice.ShortC => SideKey.C,
                SideChoice.ShortD => SideKey.D,
                _ => SideKey.A,
            };
            PlanSide side = PlanSides.Get(footprints[index].Rectangle, key);
            string how = choice == SideChoice.Auto ? "largest side" : "chosen side";
            return new Alignment(-side.OutwardNormal, index, side,
                $"Looking at {how} {key} (faces {side.Compass}, {side.Length * 304.8:N0} mm long)");
        }
    }
}
