using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>One place where an MEP element passes through a structural element.</summary>
    public sealed class ClashData
    {
        /// <summary>Stable identity of the clash.</summary>
        public ClashKey Key { get; set; }

        /// <summary>Short id shown to the user, derived from <see cref="Key"/>; the same on every run.</summary>
        public string ClashId => Key.ToClashId();

        /// <summary>Model the structural element belongs to.</summary>
        public ModelRef StructuralModel { get; set; }

        /// <summary>The structural element.</summary>
        public StructuralElementData Structural { get; set; }

        /// <summary>Model the MEP element belongs to.</summary>
        public ModelRef MepModel { get; set; }

        /// <summary>The MEP element.</summary>
        public MepElementData Mep { get; set; }

        /// <summary>The MEP segment that enters the structure (its cross-section sizes the opening).</summary>
        public MepSegment Segment { get; set; }

        /// <summary>Where the centreline enters the structure.</summary>
        public Vec3 EntryPoint { get; set; }

        /// <summary>Where the centreline leaves the structure (the MEP end point for a partial clash).</summary>
        public Vec3 ExitPoint { get; set; }

        /// <summary>Unit direction of the MEP element through the structure.</summary>
        public Vec3 MepDirection { get; set; }

        /// <summary>Outward normal of the structural face at the entry point.</summary>
        public Vec3 EntryNormal { get; set; }

        /// <summary>True when the MEP element starts or ends inside the structure.</summary>
        public bool IsPartial { get; set; }

        /// <summary>True when the centreline misses the structure and only the body of the element touches it.</summary>
        public bool IsBodyOnly { get; set; }

        /// <summary>True when a person should check this clash before an opening is generated.</summary>
        public bool NeedsReview => IsPartial || IsBodyOnly;
    }
}
