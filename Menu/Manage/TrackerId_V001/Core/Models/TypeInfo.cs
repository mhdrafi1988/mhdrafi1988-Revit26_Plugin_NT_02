namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>One element type of the chosen category, as listed in step 2.</summary>
    public sealed class TypeInfo
    {
        /// <summary>Creates the row data.</summary>
        public TypeInfo(long id, string familyName, string typeName, string typeMark, int elementCount, int withIdCount)
        {
            Id = id;
            FamilyName = familyName ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            TypeMark = typeMark ?? string.Empty;
            ElementCount = elementCount;
            WithIdCount = withIdCount;
        }

        /// <summary>Revit type id (<c>ElementId.Value</c>).</summary>
        public long Id { get; }

        /// <summary>Family name (or the system family name, e.g. "Basic Wall").</summary>
        public string FamilyName { get; }

        /// <summary>Type name.</summary>
        public string TypeName { get; }

        /// <summary>Type Mark, empty when not set.</summary>
        public string TypeMark { get; }

        /// <summary>Number of placed elements of this type.</summary>
        public int ElementCount { get; }

        /// <summary>Number of those that already have a Tracker_ID.</summary>
        public int WithIdCount { get; }
    }
}
