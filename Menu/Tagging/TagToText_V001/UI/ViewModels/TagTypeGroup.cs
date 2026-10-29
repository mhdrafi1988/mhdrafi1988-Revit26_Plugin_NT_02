using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels
{
    /// <summary>
    /// The rows of one tag type (what the table's Type column shows, e.g. "Spot Elevation" or "Roof Tags").
    /// Feeds the Type list above the table and the tick box on each group heading.
    /// </summary>
    public sealed class TagTypeGroup : ObservableObject
    {
        private readonly List<TagRow> _rows;

        /// <summary>Creates the group <paramref name="name"/> holding <paramref name="rows"/>.</summary>
        public TagTypeGroup(string name, TagKind kind, IEnumerable<TagRow> rows)
        {
            Name = name ?? string.Empty;
            Kind = kind;
            _rows = rows?.ToList() ?? new List<TagRow>();
        }

        /// <summary>Type name, as in the Type column.</summary>
        public string Name { get; }

        /// <summary>The kind of tag; the Source tick box for this kind shows or hides the whole type.</summary>
        public TagKind Kind { get; }

        /// <summary>The rows of this type.</summary>
        public IReadOnlyList<TagRow> Rows => _rows;

        /// <summary>Number of rows.</summary>
        public int Count => _rows.Count;

        /// <summary>Number of rows ticked.</summary>
        public int TickedCount => _rows.Count(r => r.IsSelected);

        /// <summary>True when at least one row can be copied (and so can be ticked).</summary>
        public bool HasCopyable => _rows.Any(r => r.IsCopyable);

        /// <summary>
        /// True when every copyable row is ticked, false when none is, null when only some are
        /// (the heading's tick box then shows a partial tick).
        /// </summary>
        public bool? AllTicked
        {
            get
            {
                int ticked = TickedCount;
                if (ticked == 0) return false;
                return ticked >= _rows.Count(r => r.IsCopyable) ? true : null;
            }
        }

        /// <summary>"Roof Tags (5)", shown in the Type list.</summary>
        public string Label => $"{Name} ({Count})";

        /// <summary>"3 of 5 ticked", shown on the group heading.</summary>
        public string TickedText => $"{TickedCount} of {Count} ticked";

        /// <summary>Tells the window the ticks (or which rows can be copied) changed.</summary>
        public void NotifyTicksChanged()
        {
            OnPropertyChanged(nameof(TickedCount));
            OnPropertyChanged(nameof(HasCopyable));
            OnPropertyChanged(nameof(AllTicked));
            OnPropertyChanged(nameof(TickedText));
        }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
