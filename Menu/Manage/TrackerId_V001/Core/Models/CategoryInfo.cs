namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>One model category, as listed in step 1.</summary>
    public sealed class CategoryInfo
    {
        /// <summary>Creates the row data.</summary>
        public CategoryInfo(long id, string name, string discipline, int typeCount, int elementCount, int withIdCount)
        {
            Id = id;
            Name = name ?? string.Empty;
            Discipline = discipline ?? string.Empty;
            TypeCount = typeCount;
            ElementCount = elementCount;
            WithIdCount = withIdCount;
        }

        /// <summary>Revit category id (<c>ElementId.Value</c>).</summary>
        public long Id { get; }

        /// <summary>Category name as Revit shows it.</summary>
        public string Name { get; }

        /// <summary>Architecture, Structure or MEP.</summary>
        public string Discipline { get; }

        /// <summary>Number of types that have placed elements.</summary>
        public int TypeCount { get; }

        /// <summary>Number of placed elements.</summary>
        public int ElementCount { get; }

        /// <summary>Number of placed elements that already have a Tracker_ID.</summary>
        public int WithIdCount { get; }
    }
}
