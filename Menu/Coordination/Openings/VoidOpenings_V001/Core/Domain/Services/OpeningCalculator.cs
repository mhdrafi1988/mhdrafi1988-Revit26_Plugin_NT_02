using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
    /// <summary>
    /// Turns a clash into an opening: which way it points, its shape, its size from the clearance
    /// rules, its depth and its centre.
    /// </summary>
    public sealed class OpeningCalculator
    {
        // cos(80°): past this the MEP element runs almost along the face it is supposed to pass through.
        private const double MinCos = 0.17364817766693041;

        private readonly ClearanceRuleSet _rules;
        private readonly OpeningSettings _settings;

        /// <summary>Creates a calculator for one run.</summary>
        public OpeningCalculator(ClearanceRuleSet rules, OpeningSettings settings)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Calculates the opening for one clash.</summary>
        public OpeningData Calculate(ClashData clash)
        {
            if (clash == null) throw new ArgumentNullException(nameof(clash));

            var notes = new List<string>();
            if (clash.IsPartial) notes.Add("MEP element starts or ends inside the structure.");
            if (clash.IsBodyOnly) notes.Add("Only the body of the MEP element touches the structure; its centreline passes outside.");

            MepProfile profile = clash.Segment.Profile;
            double insulation = _settings.IncludeInsulation ? Math.Max(0, clash.Mep.InsulationMm) : 0;
            ClearanceRule rule = _rules.Resolve(clash.Mep.Category, profile.NominalSizeMm);
            double added = rule.Mode == ClearanceMode.PerSide ? 2 * rule.ClearanceMm : rule.ClearanceMm;

            Vec3 direction = clash.MepDirection;
            Vec3 axis = ResolveAxis(clash, direction, notes);
            double cos = Math.Abs(direction.Dot(axis));
            if (cos < MinCos)
            {
                notes.Add("MEP element runs almost parallel to the face; opening size is approximate.");
                cos = MinCos;
            }
            double angleDeg = Math.Acos(Math.Min(1, cos)) * 180 / Math.PI;

            // In-plane directions of the opening: width along u, height along v.
            Vec3 u = profile.Shape == ProfileShape.Rectangular
                ? FirstUsable(clash.Segment.ProfileX.RejectFrom(axis), clash.Segment.ProfileY.RejectFrom(axis), axis.AnyPerpendicular())
                : FirstUsable(direction.RejectFrom(axis), axis.AnyPerpendicular(), axis.AnyPerpendicular());
            Vec3 v = axis.Cross(u).Normalized();

            // Footprint of the element on the two faces: every rim line is followed to the planes
            // through the entry and exit points, and the extents are measured along u and v.
            Vec3 mid = (clash.EntryPoint + clash.ExitPoint) * 0.5;
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (Vec3 planePoint in new[] { clash.EntryPoint, clash.ExitPoint })
            {
                foreach (Vec3 offset in RimOffsets(clash.Segment, direction, u, insulation))
                {
                    Vec3 rim = planePoint + offset;
                    Vec3 onPlane = rim + direction * ((planePoint - rim).Dot(axis) / cos);
                    Vec3 local = onPlane - mid;
                    minU = Math.Min(minU, local.Dot(u)); maxU = Math.Max(maxU, local.Dot(u));
                    minV = Math.Min(minV, local.Dot(v)); maxV = Math.Max(maxV, local.Dot(v));
                }
            }
            double extentU = maxU - minU;
            double extentV = maxV - minV;
            Vec3 center = mid + u * ((minU + maxU) / 2) + v * ((minV + maxV) / 2);

            var opening = new OpeningData
            {
                Clash = clash,
                Orientation = new Frame3(center, u, v, axis),
                ClearanceMm = rule.ClearanceMm,
                DepthMm = Math.Abs((clash.ExitPoint - clash.EntryPoint).Dot(axis)) + 2 * Math.Max(0, _settings.OvershootMm),
                Tracking = TrackingState.New,
            };

            if (UseCircular(profile, rule.Shape, angleDeg))
            {
                double covering = profile.Shape == ProfileShape.Round
                    ? Math.Max(extentU, extentV)
                    : Math.Sqrt(extentU * extentU + extentV * extentV);
                opening.Shape = OpeningShape.Circular;
                opening.DiameterMm = RoundUp(covering + added);
            }
            else
            {
                opening.Shape = OpeningShape.Rectangular;
                opening.WidthMm = RoundUp(extentU + added);
                opening.HeightMm = RoundUp(extentV + added);
            }

            opening.ReviewNote = string.Join(" ", notes);
            opening.Status = notes.Count > 0 ? OpeningStatus.Review : OpeningStatus.Detected;
            return opening;
        }

        private bool UseCircular(MepProfile profile, OpeningShapePolicy policy, double angleDeg)
        {
            if (policy == OpeningShapePolicy.Circular) return true;
            if (policy == OpeningShapePolicy.Rectangular) return false;
            return profile.Shape == ProfileShape.Round && angleDeg <= _settings.MaxCircularAngleDeg;
        }

        private Vec3 ResolveAxis(ClashData clash, Vec3 direction, List<string> notes)
        {
            if (_settings.AxisMode == OpeningAxisMode.FollowMep)
                return direction;

            if (clash.Structural.FaceNormal.HasValue)
                return Aligned(clash.Structural.FaceNormal.Value.Normalized(), direction);

            if (clash.Structural.MemberAxis.HasValue)
            {
                Vec3 across = direction.RejectFrom(clash.Structural.MemberAxis.Value.Normalized());
                if (across.Length > MinCos)
                    return across.Normalized();
                notes.Add("MEP element runs along the structural member.");
                return direction;
            }

            return clash.EntryNormal.IsNearZero() ? direction : Aligned(clash.EntryNormal.Normalized(), direction);
        }

        /// <summary>The normal, flipped if needed so it points the way the MEP element travels.</summary>
        private static Vec3 Aligned(Vec3 normal, Vec3 direction) => normal.Dot(direction) < 0 ? -normal : normal;

        private static Vec3 FirstUsable(Vec3 a, Vec3 b, Vec3 fallback)
        {
            if (a.Length > 1e-6) return a.Normalized();
            if (b.Length > 1e-6) return b.Normalized();
            return fallback;
        }

        /// <summary>
        /// Four points on the rim of the cross-section, chosen so they are the extremes along the
        /// opening's width and height: the corners of a rectangular section, or the four points of a
        /// round section that lie towards and across <paramref name="u"/>.
        /// </summary>
        private static IEnumerable<Vec3> RimOffsets(MepSegment segment, Vec3 direction, Vec3 u, double insulation)
        {
            if (segment.Profile.Shape == ProfileShape.Round)
            {
                double radius = segment.Profile.DiameterMm / 2 + insulation;
                Vec3 c1 = FirstUsable(u.RejectFrom(direction), direction.AnyPerpendicular(), direction.AnyPerpendicular());
                Vec3 c2 = direction.Cross(c1).Normalized();
                yield return c1 * radius;
                yield return c1 * -radius;
                yield return c2 * radius;
                yield return c2 * -radius;
                yield break;
            }

            Vec3 x = FirstUsable(segment.ProfileX.RejectFrom(direction), direction.AnyPerpendicular(), direction.AnyPerpendicular());
            Vec3 y = direction.Cross(x).Normalized();
            double halfWidth = segment.Profile.WidthMm / 2 + insulation;
            double halfHeight = segment.Profile.HeightMm / 2 + insulation;
            yield return x * halfWidth + y * halfHeight;
            yield return x * halfWidth - y * halfHeight;
            yield return x * -halfWidth + y * halfHeight;
            yield return x * -halfWidth - y * halfHeight;
        }

        private double RoundUp(double value)
        {
            double module = _settings.RoundUpToMm;
            return module > 0 ? Math.Ceiling(value / module - 1e-9) * module : value;
        }
    }
}
