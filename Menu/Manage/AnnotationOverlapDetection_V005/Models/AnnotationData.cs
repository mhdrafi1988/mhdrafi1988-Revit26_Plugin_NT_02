using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Models
{
    /// <summary>
    /// Internal working data for a single annotation element, collected during
    /// Step 2 (Annotation Collection). Coordinates are stored in mm.
    /// Not bound to the UI directly - OverlapResult is the UI-facing model.
    /// </summary>
    public class AnnotationData
    {
        public ElementId ElementId { get; set; }
        public string TypeName { get; set; }

        // Bounding box in mm, view coordinate system
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public double Right => X + Width;
        public double Bottom => Y + Height;
    }
}
