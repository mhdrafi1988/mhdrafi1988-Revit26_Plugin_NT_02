using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.UI.ViewModels
{
    /// <summary>A combo-box option: a label and the value it stands for. The label can change.</summary>
    public sealed partial class Choice<T> : ObservableObject
    {
        /// <summary>Creates an option.</summary>
        public Choice(T value, string label)
        {
            Value = value;
            _label = label;
        }

        /// <summary>The value this option selects.</summary>
        public T Value { get; }

        /// <summary>Text shown in the combo box.</summary>
        [ObservableProperty] private string _label;

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>One row of the selection list.</summary>
    public sealed class ElementRow
    {
        private const double MmPerFoot = 304.8;

        /// <summary>Creates the row for <paramref name="element"/>.</summary>
        public ElementRow(PickedElement element, bool isLargest)
        {
            Element = element;
            IsLargest = isLargest;
        }

        /// <summary>The element behind the row.</summary>
        public PickedElement Element { get; }

        /// <summary>True for the element whose side the view is aligned to.</summary>
        public bool IsLargest { get; }

        /// <summary>Element id.</summary>
        public long Id => Element.ElementId;

        /// <summary>"Category · Family : Type".</summary>
        public string Description => string.IsNullOrEmpty(Element.Name) ? Element.Category : $"{Element.Category} · {Element.Name}";

        /// <summary>"Host" or the link name.</summary>
        public string Source => Element.IsLinked ? $"Link: {Element.LinkName}" : "Host";

        /// <summary>Plan rectangle length × width × height, mm.</summary>
        public string SizeText
        {
            get
            {
                var f = Element.Footprint;
                return $"{f.Rectangle.Length * MmPerFoot:N0} × {f.Rectangle.Width * MmPerFoot:N0} × {f.Height * MmPerFoot:N0}";
            }
        }
    }
}
