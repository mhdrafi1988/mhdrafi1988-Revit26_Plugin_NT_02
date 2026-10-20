using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry
{
    /// <summary>
    /// The section box and camera of the new 3D view, in project coordinates (feet).
    /// The box frame is <see cref="Right"/> (local X), <see cref="Forward"/> (local Y) and project Z (local Z),
    /// so the box is turned in plan to face the chosen side.
    /// </summary>
    public sealed class SectionBoxPlan
    {
        /// <summary>Origin of the box frame (plan centre of the elements, Z = 0).</summary>
        public Point3 Origin { get; init; }

        /// <summary>Box local X: the viewer's right.</summary>
        public Vec2 Right { get; init; }

        /// <summary>Box local Y: the direction the viewer looks in.</summary>
        public Vec2 Forward { get; init; }

        /// <summary>Box minimum corner in the box frame.</summary>
        public Point3 Min { get; init; }

        /// <summary>Box maximum corner in the box frame.</summary>
        public Point3 Max { get; init; }

        /// <summary>Box size along <see cref="Right"/> (ft).</summary>
        public double Width => Max.X - Min.X;

        /// <summary>Box size along <see cref="Forward"/> (ft).</summary>
        public double Depth => Max.Y - Min.Y;

        /// <summary>Box size along Z (ft).</summary>
        public double Height => Max.Z - Min.Z;

        /// <summary>Camera position.</summary>
        public Point3 Eye { get; init; }

        /// <summary>Unit camera view direction.</summary>
        public Point3 ViewDirection { get; init; }

        /// <summary>Unit camera up direction, perpendicular to <see cref="ViewDirection"/>.</summary>
        public Point3 Up { get; init; }
    }

    /// <summary>Fits the section box around all elements and places the camera.</summary>
    public static class SectionBoxPlanner
    {
        /// <summary>Camera angle to the right of straight-on for <see cref="CameraMode.Angled"/>.</summary>
        public const double AngledAzimuthDeg = 30;

        /// <summary>Camera angle above level for <see cref="CameraMode.Angled"/>.</summary>
        public const double AngledElevationDeg = 35;

        /// <summary>
        /// Box aligned to <paramref name="alignment"/> that holds every footprint, grown by the margins (ft):
        /// <paramref name="sideMargin"/> left/right, <paramref name="depthMargin"/> front/back,
        /// <paramref name="topBottomMargin"/> above/below.
        /// </summary>
        public static SectionBoxPlan Plan(IReadOnlyList<ElementFootprint> footprints, Alignment alignment,
            double sideMargin, double depthMargin, double topBottomMargin, CameraMode camera)
        {
            if (footprints == null || footprints.Count == 0)
                throw new InvalidOperationException("Select at least one element.");
            if (alignment == null) throw new ArgumentNullException(nameof(alignment));

            Vec2 right = alignment.Right, forward = alignment.Forward;
            double minR = double.MaxValue, maxR = double.MinValue, minF = double.MaxValue, maxF = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (ElementFootprint f in footprints)
            {
                foreach (Vec2 p in f.Hull)
                {
                    double r = p.Dot(right), d = p.Dot(forward);
                    minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                    minF = Math.Min(minF, d); maxF = Math.Max(maxF, d);
                }
                minZ = Math.Min(minZ, f.MinZ);
                maxZ = Math.Max(maxZ, f.MaxZ);
            }

            // Revit refuses a flat section box: keep every size at least 1/8" before the margins.
            const double minSize = 1.0 / 96;
            Grow(ref minR, ref maxR, minSize);
            Grow(ref minF, ref maxF, minSize);
            Grow(ref minZ, ref maxZ, minSize);

            double cR = (minR + maxR) / 2, cF = (minF + maxF) / 2;
            Vec2 originPlan = right * cR + forward * cF;
            double halfW = (maxR - minR) / 2 + Math.Max(0, sideMargin);
            double halfD = (maxF - minF) / 2 + Math.Max(0, depthMargin);
            double zLo = minZ - Math.Max(0, topBottomMargin), zHi = maxZ + Math.Max(0, topBottomMargin);

            // The camera looks at the box centre from well outside the box.
            Point3 target = new(originPlan.X, originPlan.Y, (zLo + zHi) / 2);
            (Point3 dir, Point3 up) = CameraAxes(forward, camera);
            double distance = halfD + Math.Max(2 * halfW, zHi - zLo) + 10;
            return new SectionBoxPlan
            {
                Origin = new Point3(originPlan.X, originPlan.Y, 0),
                Right = right,
                Forward = forward,
                Min = new Point3(-halfW, -halfD, zLo),
                Max = new Point3(halfW, halfD, zHi),
                Eye =new Point3(target.X - dir.X * distance, target.Y - dir.Y * distance, target.Z - dir.Z * distance),
                ViewDirection = dir,
                Up = up,
            };
        }

        /// <summary>Unit view direction and up vector for a camera looking along plan <paramref name="forward"/>.</summary>
        public static (Point3 ViewDirection, Point3 Up) CameraAxes(Vec2 forward, CameraMode camera)
        {
            Vec2 f = forward.Normalized();
            if (camera == CameraMode.FacingSide)
                return (new Point3(f.X, f.Y, 0), new Point3(0, 0, 1));

            Vec2 right = new(f.Y, -f.X);
            double az = AngledAzimuthDeg * Math.PI / 180, el = AngledElevationDeg * Math.PI / 180;

            // Direction from the target to the eye: in front of the side, turned to the right, raised.
            Vec2 h = (-f) * Math.Cos(az) + right * Math.Sin(az);
            double tx = h.X * Math.Cos(el), ty = h.Y * Math.Cos(el), tz = Math.Sin(el);
            Point3 dir = new(-tx, -ty, -tz);

            // up = (dir × Z) × dir, normalised
            double rx = dir.Y, ry = -dir.X, rz = 0;
            double ux = ry * dir.Z - rz * dir.Y, uy = rz * dir.X - rx * dir.Z, uz = rx * dir.Y - ry * dir.X;
            double len = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            return (dir, new Point3(ux / len, uy / len, uz / len));
        }

        private static void Grow(ref double min, ref double max, double minSize)
        {
            if (max - min >= minSize) return;
            double c = (min + max) / 2;
            min = c - minSize / 2;
            max = c + minSize / 2;
        }
    }
}
