namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Models
{
    /// <summary>How the rows under each workset are grouped in the browser tree.</summary>
    public enum GroupByMode
    {
        /// <summary>Workset &gt; Category &gt; Type (the original layout and the default).</summary>
        CategoryThenType,

        /// <summary>Workset &gt; Type &gt; Category.</summary>
        TypeThenCategory,

        /// <summary>Workset &gt; Category (one row per category, types merged).</summary>
        CategoryOnly,

        /// <summary>Workset &gt; Type (one row per type, categories merged).</summary>
        TypeOnly
    }

    /// <summary>A Group-by choice with its display label, for the toolbar combo box.</summary>
    public sealed class GroupByOption
    {
        /// <summary>The mode this option selects.</summary>
        public GroupByMode Mode { get; }

        /// <summary>Text shown in the combo box.</summary>
        public string Label { get; }

        /// <summary>Creates an option.</summary>
        public GroupByOption(GroupByMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }
    }

    /// <summary>Result of a "save selection set" request.</summary>
    public enum SelectionSetOutcome
    {
        /// <summary>No request has run yet.</summary>
        None,

        /// <summary>A new selection set was created.</summary>
        Created,

        /// <summary>An existing selection set of that name was overwritten.</summary>
        Updated,

        /// <summary>A selection set with that name exists and overwrite was not allowed; nothing changed.</summary>
        NameExists
    }
}
