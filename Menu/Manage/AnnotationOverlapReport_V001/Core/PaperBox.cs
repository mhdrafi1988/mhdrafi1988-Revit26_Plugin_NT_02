namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core
{
    /// <summary>
    /// One annotation's box in a view, in millimetres on paper (model distance divided by the view scale).
    /// X runs along the view's right direction, Y along its up direction. Revit-free.
    /// </summary>
    public sealed class PaperBox
    {
        /// <summary>Revit element id value.</summary>
        public long Id { get; set; }

        /// <summary>Annotation type shown in the type list: the Revit category name.</summary>
        public string TypeName { get; set; }

        /// <summary>The annotation's text (tag text, note text, dimension value); may be empty.</summary>
        public string Text { get; set; }

        /// <summary>Left edge.</summary>
        public double X { get; set; }

        /// <summary>Bottom edge.</summary>
        public double Y { get; set; }

        /// <summary>Box width.</summary>
        public double Width { get; set; }

        /// <summary>Box height.</summary>
        public double Height { get; set; }

        /// <summary>Right edge.</summary>
        public double Right => X + Width;

        /// <summary>Top edge.</summary>
        public double Top => Y + Height;
    }
}
