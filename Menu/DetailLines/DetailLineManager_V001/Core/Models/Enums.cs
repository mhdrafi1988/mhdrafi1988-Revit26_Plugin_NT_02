namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>Which detail lines a search collects.</summary>
    public enum SearchScope
    {
        /// <summary>Detail lines in the current Revit selection.</summary>
        Selected,

        /// <summary>Detail lines owned by the active view.</summary>
        ActiveView,

        /// <summary>Detail lines owned by sheets or by views placed on sheets.</summary>
        ViewsOnSheets,

        /// <summary>Every detail line in the project.</summary>
        WholeProject
    }

    /// <summary>Which line of a duplicate cluster survives a delete.</summary>
    public enum DuplicateKeepRule
    {
        /// <summary>Keep the line with the lowest element ID.</summary>
        OldestId,

        /// <summary>Keep the line with the highest element ID.</summary>
        NewestId,

        /// <summary>Keep the line with the heaviest effective line weight (lowest ID on a tie).</summary>
        HeaviestWeight
    }

    /// <summary>How the Line thickness tab changes lines.</summary>
    public enum ThicknessMode
    {
        /// <summary>Set <c>CurveElement.LineStyle</c> to the chosen style.</summary>
        AssignStyle,

        /// <summary>Change the projection line weight of the styles the target lines use (project-wide).</summary>
        StyleLineWeight,

        /// <summary>Set or clear a per-element projection line weight override in each line's owner view.</summary>
        ViewOverride
    }

    /// <summary>Geometry kind of a detail line.</summary>
    public enum CurveKind
    {
        /// <summary>Straight line.</summary>
        Line,

        /// <summary>Circular arc or circle.</summary>
        Arc,

        /// <summary>NURBS or Hermite spline.</summary>
        Spline,

        /// <summary>Ellipse or elliptical arc.</summary>
        Ellipse,

        /// <summary>Any other curve type.</summary>
        Other
    }
}
