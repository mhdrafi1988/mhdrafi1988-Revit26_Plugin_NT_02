namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models
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

    /// <summary>What Delete unused does with overlapping straight lines of the same style.</summary>
    public enum OverlapMode
    {
        /// <summary>Delete lines that a longer line of the same style fully covers.</summary>
        DeleteCovered,

        /// <summary>Join each set of overlapping lines into one line (the longest is kept and extended).</summary>
        Join
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

    /// <summary>Which line styles the Rename styles tab lists.</summary>
    public enum RenameScope
    {
        /// <summary>Styles used by the lines the grid shows.</summary>
        FoundLines,

        /// <summary>Styles ticked in the Line styles list of the filter rail.</summary>
        TickedStyles,

        /// <summary>Every line style that is not built in.</summary>
        AllCustom
    }

    /// <summary>Letter case applied by the Rename styles tab.</summary>
    public enum NameCase
    {
        /// <summary>Keep the case.</summary>
        Unchanged,

        /// <summary>UPPER CASE.</summary>
        Upper,

        /// <summary>lower case.</summary>
        Lower,

        /// <summary>Title Case.</summary>
        Title
    }

    /// <summary>What the Rename styles tab does when a new name is already taken.</summary>
    public enum DuplicateNameRule
    {
        /// <summary>Do not rename the style; log it.</summary>
        Skip,

        /// <summary>Add " (2)", " (3)", … until the name is free.</summary>
        AddNumber
    }

    /// <summary>Result of checking one planned rename.</summary>
    public enum RenameStatus
    {
        /// <summary>Will be renamed.</summary>
        WillRename,

        /// <summary>The rules give the same name.</summary>
        Unchanged,

        /// <summary>Another style already has, or would get, the new name.</summary>
        Duplicate,

        /// <summary>The new name is empty or has characters Revit refuses.</summary>
        Invalid,

        /// <summary>A built-in style, which is never renamed.</summary>
        BuiltIn
    }
}
