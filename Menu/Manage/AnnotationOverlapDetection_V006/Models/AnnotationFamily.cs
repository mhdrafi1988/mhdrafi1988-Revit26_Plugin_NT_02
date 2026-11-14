using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models
{
    /// <summary>
    /// Represents one annotation type group in the "Select annotation types to check" list.
    /// Bound to a checkbox row: [checkbox] TypeName (Count items)
    /// </summary>
    public partial class AnnotationFamily : ObservableObject
    {
        [ObservableProperty]
        private string typeName;

        [ObservableProperty]
        private int count;

        [ObservableProperty]
        private bool isSelected = true; // default: all checkboxes ticked
    }
}
