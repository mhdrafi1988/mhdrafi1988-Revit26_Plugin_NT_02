using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    /// <summary>A new parameter staged on the Merge tab, edited in a grid.</summary>
    public sealed partial class NewParamRow : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _dataType = "TEXT";
        [ObservableProperty] private string _group = "Other";
        [ObservableProperty] private string _description = string.Empty;

        /// <summary>The parameter as the merger takes it (blank GUID: a fresh one is generated).</summary>
        public SpParam ToSpParam() => new SpParam
        {
            Name = Name?.Trim() ?? string.Empty,
            DataType = DataType,
            GroupName = string.IsNullOrWhiteSpace(Group) ? "Other" : Group.Trim(),
            Description = Description ?? string.Empty
        };
    }

    /// <summary>One line of the duplicate report grid.</summary>
    public sealed class DuplicateRow
    {
        /// <summary>"Same GUID" or "Same name".</summary>
        public string Kind { get; set; }

        /// <summary>Parameter name.</summary>
        public string Name { get; set; }

        /// <summary>Parameter GUID.</summary>
        public string Guid { get; set; }

        /// <summary>Data type token.</summary>
        public string DataType { get; set; }

        /// <summary>File group.</summary>
        public string Group { get; set; }

        /// <summary>Line in the file.</summary>
        public int Line { get; set; }

        /// <summary>"Keep" for the first of a set, "Remove" for the rest.</summary>
        public string Action { get; set; }

        /// <summary>Set when the members of the set have different data types.</summary>
        public string Note { get; set; }
    }

    /// <summary>A conflict-policy choice with its label.</summary>
    public sealed class PolicyOption
    {
        /// <summary>Label shown in the drop-down.</summary>
        public string Label { get; set; }

        /// <summary>The policy.</summary>
        public ConflictPolicy Value { get; set; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
