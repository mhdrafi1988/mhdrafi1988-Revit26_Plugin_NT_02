#nullable enable
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin.RoofNameBuilder.V002.Core.Models;
using Revit26_Plugin.RoofNameBuilder.V002.Core.Services;

namespace Revit26_Plugin.RoofNameBuilder.V002.UI.ViewModels
{
    /// <summary>
    /// One grid row. Recomputes NewName live as BaseName or any code's tick/value changes.
    /// V002 adds the row's validation state (illegal characters, too long, conflicts) and a
    /// one-word StatusText for the grid's Status column.
    /// </summary>
    public partial class RoofNameRowViewModel : ObservableObject
    {
        public long ElementId { get; }
        public string CurrentName { get; }
        public string TypeName { get; }
        public int InstanceCount { get; }

        [ObservableProperty] private string baseName;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private string newName = string.Empty;
        [ObservableProperty] private bool isDuplicate;
        [ObservableProperty] private bool isTooLong;
        [ObservableProperty] private string illegalCharacters = string.Empty;

        /// <summary>code -> editable tick/value, keyed the same as RoofNameRow.Codes. Bound as Codes[INS].Ticked.</summary>
        public Dictionary<string, RoofCodeValueViewModel> Codes { get; } = new();

        private readonly IReadOnlyList<NameCodeDefinition> _definitions;
        private readonly string _separator;
        private readonly int _lengthWarningThreshold;
        private readonly bool _validateTypeNameCharacters;

        public RoofNameRowViewModel(
            RoofNameRow row, IReadOnlyList<NameCodeDefinition> definitions,
            string separator, int lengthWarningThreshold, bool validateTypeNameCharacters)
        {
            ElementId = row.ElementId;
            CurrentName = row.CurrentName;
            TypeName = row.TypeName;
            InstanceCount = row.InstanceCount;
            baseName = row.BaseName;

            _definitions = definitions;
            _separator = separator;
            _lengthWarningThreshold = lengthWarningThreshold;
            _validateTypeNameCharacters = validateTypeNameCharacters;

            foreach (var def in definitions)
            {
                if (string.IsNullOrWhiteSpace(def.Code) || Codes.ContainsKey(def.Code)) continue;
                var source = row.Codes.TryGetValue(def.Code, out var cv) ? cv : new RoofCodeValue();
                var codeVm = new RoofCodeValueViewModel(def.Code, source.Ticked, source.Value);
                codeVm.PropertyChanged += (_, _) => Recompute();
                Codes[def.Code] = codeVm;
            }

            Recompute();
        }

        partial void OnBaseNameChanged(string value) => Recompute();
        partial void OnIsDuplicateChanged(bool value) => RaiseStatus();

        /// <summary>True once the built name differs from what's currently in the model.</summary>
        public bool IsPending => NewName != CurrentName;

        /// <summary>Already carries at least one code and is up to date — hidden by the "Hide compliant" filter.</summary>
        public bool IsCompliant => !IsPending && Codes.Values.Any(c => c.Ticked);

        public bool HasIllegalCharacters => IllegalCharacters.Length > 0;

        /// <summary>Blocks Run while this row is pending.</summary>
        public bool HasBlockingIssue => IsPending && (IsDuplicate || HasIllegalCharacters);

        public string StatusText =>
            IsPending && IsDuplicate ? "Name clash"
            : IsPending && HasIllegalCharacters ? $"Illegal: {IllegalCharacters}"
            : IsPending && IsTooLong ? "Rename (long)"
            : IsPending ? "Rename"
            : IsCompliant ? "Up to date"
            : "—";

        public void Recompute()
        {
            var row = new RoofNameRow
            {
                ElementId = ElementId,
                CurrentName = CurrentName,
                BaseName = BaseName,
                Codes = Codes.ToDictionary(
                    kv => kv.Key,
                    kv => new RoofCodeValue { Ticked = kv.Value.Ticked, Value = kv.Value.Value })
            };

            NewName = NameBuilderService.BuildName(row, _definitions, _separator);
            IsTooLong = NameBuilderService.IsNameTooLong(NewName, _lengthWarningThreshold);
            IllegalCharacters = _validateTypeNameCharacters
                ? string.Join(" ", NameBuilderService.FindIllegalCharacters(NewName))
                : string.Empty;

            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsCompliant));
            OnPropertyChanged(nameof(HasIllegalCharacters));
            RaiseStatus();
        }

        private void RaiseStatus()
        {
            OnPropertyChanged(nameof(HasBlockingIssue));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public partial class RoofCodeValueViewModel : ObservableObject
    {
        public string Code { get; }

        [ObservableProperty] private bool ticked;
        [ObservableProperty] private string value;

        public RoofCodeValueViewModel(string code, bool ticked, string value)
        {
            Code = code;
            this.ticked = ticked;
            this.value = value ?? string.Empty;
        }
    }
}
