using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models
{
    /// <summary>One row of the Priority order list: rank, category, reason and "Never move".</summary>
    public partial class PriorityRow : ObservableObject
    {
        /// <summary>Annotation category of this row.</summary>
        public AnnotationCategory Category { get; init; }

        /// <summary>Label shown in the list.</summary>
        public string Label => AnnotationPriority.Label(Category);

        /// <summary>Why the recommended order puts it here.</summary>
        public string Reason => AnnotationPriority.Reason(Category);

        /// <summary>1-based position in the list (1 = stays, moves last).</summary>
        [ObservableProperty]
        private int rank;

        /// <summary>True: annotations of this category never move.</summary>
        [ObservableProperty]
        private bool neverMove;
    }
}
