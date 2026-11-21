using System;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models
{
    /// <summary>
    /// Identifies one element across the host model and its links: an element id, plus the id of
    /// the <see cref="Autodesk.Revit.DB.RevitLinkInstance"/> it sits in (0 for the host model).
    /// Element ids repeat between documents, so the pair is what makes a row unique.
    /// </summary>
    public readonly struct ElementKey : IEquatable<ElementKey>
    {
        /// <summary>Id of the link instance the element belongs to, or 0 for the host model.</summary>
        public long LinkInstanceId { get; }

        /// <summary>Id of the element in its own document.</summary>
        public long ElementId { get; }

        /// <summary>Creates a key. <paramref name="linkInstanceId"/> is 0 for a host element.</summary>
        public ElementKey(long linkInstanceId, long elementId)
        {
            LinkInstanceId = linkInstanceId;
            ElementId = elementId;
        }

        /// <summary>True when this key names an element of the host model.</summary>
        public bool IsHost => LinkInstanceId == 0;

        /// <inheritdoc/>
        public bool Equals(ElementKey other)
            => LinkInstanceId == other.LinkInstanceId && ElementId == other.ElementId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ElementKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(LinkInstanceId, ElementId);

        /// <inheritdoc/>
        public override string ToString() => IsHost ? $"host:{ElementId}" : $"link{LinkInstanceId}:{ElementId}";
    }

    /// <summary>
    /// What the user ticks: one element type in one source. Selecting by type rather than by
    /// instance is what lets a single tick carry across every section in the run, where the
    /// instances cut by each section differ.
    /// </summary>
    public readonly struct TypeKey : IEquatable<TypeKey>
    {
        /// <summary>Id of the link instance the type was read from, or 0 for the host model.</summary>
        public long LinkInstanceId { get; }

        /// <summary>Id of the element type in its own document.</summary>
        public long TypeId { get; }

        /// <summary>Creates a key. <paramref name="linkInstanceId"/> is 0 for a host type.</summary>
        public TypeKey(long linkInstanceId, long typeId)
        {
            LinkInstanceId = linkInstanceId;
            TypeId = typeId;
        }

        /// <inheritdoc/>
        public bool Equals(TypeKey other)
            => LinkInstanceId == other.LinkInstanceId && TypeId == other.TypeId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TypeKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(LinkInstanceId, TypeId);

        /// <inheritdoc/>
        public override string ToString() => $"{LinkInstanceId}/{TypeId}";
    }
}
