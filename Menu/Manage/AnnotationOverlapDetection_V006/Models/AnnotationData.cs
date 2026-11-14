using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models
{
    /// <summary>
    /// Internal working data for a single annotation element, collected during
    /// Step 2 (Annotation Collection). Coordinates are stored in mm.
    /// Not bound to the UI directly - OverlapResult is the UI-facing model.
    /// </summary>
    public class AnnotationData
    {
        public ElementId ElementId { get; set; }

        /// <summary>Label of <see cref="Category"/>, used for the type list and the grid (V006).</summary>
        public string TypeName { get; set; }

        /// <summary>Revit class name (TextNote, IndependentTag, ...): V005 compared only within one class.</summary>
        public string ClassName { get; set; }

        /// <summary>Priority category of this annotation (V006).</summary>
        public Helpers.AnnotationCategory Category { get; set; }

        // Bounding box in mm, view coordinate system
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public double Right => X + Width;
        public double Bottom => Y + Height;
    }
}
