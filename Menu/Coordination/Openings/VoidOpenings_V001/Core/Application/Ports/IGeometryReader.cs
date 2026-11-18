using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>An element that was selected for analysis but could not be turned into geometry.</summary>
    public sealed class SkippedElement
    {
        /// <summary>Name of the model the element is in.</summary>
        public string ModelName { get; }

        /// <summary>Revit element id value.</summary>
        public long ElementId { get; }

        /// <summary>Category name, for the list shown to the user.</summary>
        public string Category { get; }

        /// <summary>Why it was not analysed.</summary>
        public string Reason { get; }

        /// <summary>Creates an entry.</summary>
        public SkippedElement(string modelName, long elementId, string category, string reason)
        {
            ModelName = modelName ?? string.Empty;
            ElementId = elementId;
            Category = category ?? string.Empty;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>One batch of elements read from a model, with those that could not be read.</summary>
    public sealed class GeometryBatch<T>
    {
        /// <summary>Snapshots, in host millimetres.</summary>
        public IReadOnlyList<T> Elements { get; }

        /// <summary>Elements of the batch that were not analysed, each with its reason.</summary>
        public IReadOnlyList<SkippedElement> NotAnalysed { get; }

        /// <summary>Creates a batch.</summary>
        public GeometryBatch(IReadOnlyList<T> elements, IReadOnlyList<SkippedElement> notAnalysed)
        {
            Elements = elements ?? new List<T>();
            NotAnalysed = notAnalysed ?? new List<SkippedElement>();
        }
    }

    /// <summary>
    /// Reads element geometry as Revit-free snapshots, in the active model's coordinates and in
    /// millimetres. Implemented on the Revit side; every method must be called in the Revit API
    /// context (through <see cref="IRevitDispatcher"/>). Reading is split into "list the ids" and
    /// "read a batch", so a long read can report progress and be cancelled between batches.
    /// Every method throws <see cref="System.InvalidOperationException"/> with a message for the
    /// user when the model is no longer available (project switched, link unloaded or removed).
    /// </summary>
    public interface IGeometryReader
    {
        /// <summary>Ids of the model's elements in the given MEP categories.</summary>
        IReadOnlyList<long> ListMep(ModelRef model, IReadOnlyList<MepCategory> categories);

        /// <summary>Reads centrelines and cross-sections of the given elements.</summary>
        GeometryBatch<MepElementData> ReadMep(ModelRef model, IReadOnlyList<long> elementIds);

        /// <summary>
        /// Ids of the model's elements in the given structural categories whose bounding box touches
        /// <paramref name="regionMm"/> (host millimetres); every element when the region is null.
        /// </summary>
        IReadOnlyList<long> ListStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories, Aabb? regionMm);

        /// <summary>Reads the solids of the given elements as triangle meshes.</summary>
        GeometryBatch<StructuralElementData> ReadStructural(ModelRef model, IReadOnlyList<long> elementIds);
    }
}
