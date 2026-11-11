namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos
{
    /// <summary>One row of a category list: a category of the chosen model and how many elements it has.</summary>
    public sealed class CategoryCountDto
    {
        /// <summary>Name of the category's enum value; identifies the category when the selection is read back.</summary>
        public string Key { get; }

        /// <summary>Name shown to the user.</summary>
        public string Name { get; }

        /// <summary>Number of elements of the category in the model.</summary>
        public int Count { get; }

        /// <summary>Something the user should know before choosing the category; empty when there is nothing.</summary>
        public string Note { get; }

        /// <summary>Creates a row.</summary>
        public CategoryCountDto(string key, string name, int count, string note)
        {
            Key = key;
            Name = name;
            Count = count;
            Note = note ?? string.Empty;
        }
    }
}
