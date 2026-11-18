using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>One editable clearance rule row. It holds what was typed; the card checks it.</summary>
    public sealed class ClearanceRuleRowViewModel : ObservableObject
    {
        private readonly Action _changed;
        private MepCategory _category;
        private string _fromText;
        private string _toText;
        private string _clearanceText;
        private ClearanceMode _mode;
        private OpeningShapePolicy _shape;
        private int _number;
        private string _example = string.Empty;
        private string _check = string.Empty;
        private bool _hasError;
        private bool _hasWarning;

        /// <summary>Creates a row.</summary>
        /// <param name="input">Initial content.</param>
        /// <param name="changed">Called after any edit.</param>
        /// <param name="delete">Removes this row from the card.</param>
        public ClearanceRuleRowViewModel(ClearanceRuleInput input, Action changed, Action<ClearanceRuleRowViewModel> delete)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (delete == null) throw new ArgumentNullException(nameof(delete));
            _category = input.Category;
            _fromText = input.FromText ?? string.Empty;
            _toText = input.ToText ?? string.Empty;
            _clearanceText = input.ClearanceText ?? string.Empty;
            _mode = input.Mode;
            _shape = input.Shape;
            _changed = changed;
            DeleteCommand = new RelayCommand(() => delete(this));
        }

        /// <summary>Choices of the MEP category list.</summary>
        public IReadOnlyList<ChoiceItem<MepCategory>> CategoryOptions => ClearanceOptions.Categories;

        /// <summary>Choices of the Applies list.</summary>
        public IReadOnlyList<ChoiceItem<ClearanceMode>> ModeOptions => ClearanceOptions.Modes;

        /// <summary>Choices of the Opening shape list.</summary>
        public IReadOnlyList<ChoiceItem<OpeningShapePolicy>> ShapeOptions => ClearanceOptions.Shapes;

        /// <summary>Removes this row.</summary>
        public IRelayCommand DeleteCommand { get; }

        /// <summary>Row number, starting at 1; the first row that fits an element is used.</summary>
        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }

        /// <summary>MEP category the rule is for.</summary>
        public MepCategory Category
        {
            get => _category;
            set { if (SetProperty(ref _category, value)) _changed?.Invoke(); }
        }

        /// <summary>Smallest size covered, as typed; empty means 0.</summary>
        public string FromText
        {
            get => _fromText;
            set { if (SetProperty(ref _fromText, value ?? string.Empty)) _changed?.Invoke(); }
        }

        /// <summary>Largest size covered, as typed; empty means no upper limit.</summary>
        public string ToText
        {
            get => _toText;
            set { if (SetProperty(ref _toText, value ?? string.Empty)) _changed?.Invoke(); }
        }

        /// <summary>Offset, as typed.</summary>
        public string ClearanceText
        {
            get => _clearanceText;
            set { if (SetProperty(ref _clearanceText, value ?? string.Empty)) _changed?.Invoke(); }
        }

        /// <summary>Each side or total.</summary>
        public ClearanceMode Mode
        {
            get => _mode;
            set { if (SetProperty(ref _mode, value)) _changed?.Invoke(); }
        }

        /// <summary>Opening shape the rule asks for.</summary>
        public OpeningShapePolicy Shape
        {
            get => _shape;
            set { if (SetProperty(ref _shape, value)) _changed?.Invoke(); }
        }

        /// <summary>Worked example of the rule, for example "50 + 2 × 30 = 110 mm"; empty while the row has an error.</summary>
        public string Example
        {
            get => _example;
            private set => SetProperty(ref _example, value);
        }

        /// <summary>"OK", or what is wrong with the row.</summary>
        public string Check
        {
            get => _check;
            private set => SetProperty(ref _check, value);
        }

        /// <summary>True when the row has an error that blocks saving.</summary>
        public bool HasError
        {
            get => _hasError;
            private set => SetProperty(ref _hasError, value);
        }

        /// <summary>True when the row has a warning and no error.</summary>
        public bool HasWarning
        {
            get => _hasWarning;
            private set => SetProperty(ref _hasWarning, value);
        }

        /// <summary>What was typed in the row.</summary>
        public ClearanceRuleInput ToInput() => new ClearanceRuleInput
        {
            Category = _category,
            FromText = _fromText,
            ToText = _toText,
            ClearanceText = _clearanceText,
            Mode = _mode,
            Shape = _shape,
        };

        /// <summary>Shows the result of the card's check for this row.</summary>
        public void ShowCheck(string example, string check, bool hasError, bool hasWarning)
        {
            Example = example ?? string.Empty;
            Check = check ?? string.Empty;
            HasError = hasError;
            HasWarning = hasWarning && !hasError;
        }
    }
}
