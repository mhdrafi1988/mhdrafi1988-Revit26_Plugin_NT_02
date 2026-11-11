using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>Cross-section of an MEP element, in millimetres, without insulation.</summary>
    public sealed class MepProfile
    {
        /// <summary>Round or rectangular.</summary>
        public ProfileShape Shape { get; }

        /// <summary>Outside diameter of a round section; 0 for a rectangular one.</summary>
        public double DiameterMm { get; }

        /// <summary>Width of a rectangular section (along the segment's profile X); 0 for a round one.</summary>
        public double WidthMm { get; }

        /// <summary>Height of a rectangular section; 0 for a round one.</summary>
        public double HeightMm { get; }

        private MepProfile(ProfileShape shape, double diameter, double width, double height)
        {
            Shape = shape;
            DiameterMm = diameter;
            WidthMm = width;
            HeightMm = height;
        }

        /// <summary>The size a clearance rule is chosen by: the diameter, or the larger side.</summary>
        public double NominalSizeMm => Shape == ProfileShape.Round ? DiameterMm : Math.Max(WidthMm, HeightMm);

        /// <summary>A round section.</summary>
        public static MepProfile Round(double diameterMm)
        {
            if (diameterMm <= 0) throw new ArgumentOutOfRangeException(nameof(diameterMm));
            return new MepProfile(ProfileShape.Round, diameterMm, 0, 0);
        }

        /// <summary>A rectangular section.</summary>
        public static MepProfile Rectangular(double widthMm, double heightMm)
        {
            if (widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
            if (heightMm <= 0) throw new ArgumentOutOfRangeException(nameof(heightMm));
            return new MepProfile(ProfileShape.Rectangular, 0, widthMm, heightMm);
        }
    }

    /// <summary>One straight piece of an MEP element: centreline, cross-section and how the section is turned.</summary>
    public sealed class MepSegment
    {
        /// <summary>Centreline of the piece.</summary>
        public Segment3 Axis { get; }

        /// <summary>Cross-section of the piece.</summary>
        public MepProfile Profile { get; }

        /// <summary>Unit direction of the section's width, perpendicular to the centreline.</summary>
        public Vec3 ProfileX { get; }

        /// <summary>Unit direction of the section's height.</summary>
        public Vec3 ProfileY => Axis.Direction.Cross(ProfileX).Normalized();

        /// <summary>Creates a segment.</summary>
        /// <param name="axis">Centreline.</param>
        /// <param name="profile">Cross-section.</param>
        /// <param name="profileX">
        /// Direction of the section's width. Only matters for rectangular sections; when left out a
        /// horizontal direction perpendicular to the centreline is used.
        /// </param>
        public MepSegment(Segment3 axis, MepProfile profile, Vec3? profileX = null)
        {
            Axis = axis;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Vec3 dir = axis.Direction;
            Vec3 x = profileX.HasValue ? profileX.Value.RejectFrom(dir).Normalized() : Vec3.Zero;
            ProfileX = x.IsNearZero() ? dir.AnyPerpendicular() : x;
        }
    }

    /// <summary>
    /// Snapshot of one MEP element. Pipes, conduits, ducts, trays and their fittings all reduce to
    /// centreline segments with a cross-section, so one type covers them. Holds no Revit object.
    /// </summary>
    public sealed class MepElementData
    {
        /// <summary>Revit UniqueId in the element's own model. This is the identity used for tracking.</summary>
        public string UniqueId { get; }

        /// <summary>Revit element id value, for display and selection only.</summary>
        public long ElementId { get; }

        /// <summary>Category of the element.</summary>
        public MepCategory Category { get; }

        /// <summary>Centreline pieces in order along the element.</summary>
        public IReadOnlyList<MepSegment> Segments { get; }

        /// <summary>Insulation thickness on each side, in millimetres; 0 when there is none.</summary>
        public double InsulationMm { get; set; }

        /// <summary>Creates the snapshot.</summary>
        public MepElementData(string uniqueId, long elementId, MepCategory category, IReadOnlyList<MepSegment> segments)
        {
            UniqueId = uniqueId ?? throw new ArgumentNullException(nameof(uniqueId));
            ElementId = elementId;
            Category = category;
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }
    }

    /// <summary>The MEP side of a run: one model and the elements read from it.</summary>
    public sealed class MepModel
    {
        /// <summary>The model the elements belong to.</summary>
        public ModelRef Ref { get; }

        /// <summary>The elements to check.</summary>
        public IReadOnlyList<MepElementData> Elements { get; }

        /// <summary>Creates the MEP side of a run.</summary>
        public MepModel(ModelRef modelRef, IReadOnlyList<MepElementData> elements)
        {
            Ref = modelRef ?? throw new ArgumentNullException(nameof(modelRef));
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }
    }
}
