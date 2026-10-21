using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models
{
    /// <summary>
    /// One element in the tool's selection, in the host model or in a linked model.
    /// Holds ids only (no Revit objects), so the window can keep it between Revit calls.
    /// </summary>
    public sealed class PickedElement
    {
        /// <summary>Creates an entry; <paramref name="linkInstanceId"/> is −1 for a host-model element.</summary>
        public PickedElement(long elementId, long linkInstanceId, string linkName, string category, string name,
            ElementFootprint footprint)
        {
            ElementId = elementId;
            LinkInstanceId = linkInstanceId;
            LinkName = linkName ?? string.Empty;
            Category = category ?? string.Empty;
            Name = name ?? string.Empty;
            Footprint = footprint;
        }

        /// <summary>Element id, in the host model or (for a linked element) in the link's document.</summary>
        public long ElementId { get; }

        /// <summary>Id of the RevitLinkInstance holding the element, or −1 for a host-model element.</summary>
        public long LinkInstanceId { get; }

        /// <summary>True when the element is in a linked model.</summary>
        public bool IsLinked => LinkInstanceId >= 0;

        /// <summary>Link instance name, or empty for a host-model element.</summary>
        public string LinkName { get; }

        /// <summary>Category name.</summary>
        public string Category { get; }

        /// <summary>Family/type or element name.</summary>
        public string Name { get; }

        /// <summary>Shape in host project coordinates (feet), read when the element was picked.</summary>
        public ElementFootprint Footprint { get; }

        /// <summary>Same element (and same link) as <paramref name="other"/>.</summary>
        public bool SameAs(PickedElement other)
            => other != null && other.ElementId == ElementId && other.LinkInstanceId == LinkInstanceId;
    }

    /// <summary>A Revit element type the user can choose (3D view type or view template).</summary>
    public sealed class NamedId
    {
        /// <summary>Creates an entry.</summary>
        public NamedId(long id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>Element id; −1 means "none".</summary>
        public long Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }
}
