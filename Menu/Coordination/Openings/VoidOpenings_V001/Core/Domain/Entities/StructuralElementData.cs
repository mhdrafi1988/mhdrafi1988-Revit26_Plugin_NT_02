using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>Snapshot of one structural element: identity plus geometry in host millimetres. Holds no Revit object.</summary>
    public sealed class StructuralElementData
    {
        /// <summary>Revit UniqueId in the element's own model. This is the identity used for tracking.</summary>
        public string UniqueId { get; }

        /// <summary>Revit element id value, for display and selection only.</summary>
        public long ElementId { get; }

        /// <summary>Category of the element.</summary>
        public StructuralCategory Category { get; }

        /// <summary>Surface of the element.</summary>
        public TriangleMesh Mesh { get; }

        /// <summary>Bounding box of the element.</summary>
        public Aabb Bounds => Mesh.Bounds;

        /// <summary>Normal of the main faces of a wall or floor (unit length); null when not known.</summary>
        public Vec3? FaceNormal { get; set; }

        /// <summary>Direction of a beam or column along its length (unit length); null when not known.</summary>
        public Vec3? MemberAxis { get; set; }

        /// <summary>Creates the snapshot.</summary>
        public StructuralElementData(string uniqueId, long elementId, StructuralCategory category, TriangleMesh mesh)
        {
            UniqueId = uniqueId ?? throw new ArgumentNullException(nameof(uniqueId));
            ElementId = elementId;
            Category = category;
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        }
    }

    /// <summary>The structural side of a run: one model and the elements read from it.</summary>
    public sealed class StructuralModel
    {
        /// <summary>The model the elements belong to.</summary>
        public ModelRef Ref { get; }

        /// <summary>The elements to check.</summary>
        public IReadOnlyList<StructuralElementData> Elements { get; }

        /// <summary>Creates the structural side of a run.</summary>
        public StructuralModel(ModelRef modelRef, IReadOnlyList<StructuralElementData> elements)
        {
            Ref = modelRef ?? throw new ArgumentNullException(nameof(modelRef));
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }
    }
}
