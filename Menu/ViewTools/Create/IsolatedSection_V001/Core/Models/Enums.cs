namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models
{
    /// <summary>Name of one side of an element's plan rectangle: A/B long, C/D short.</summary>
    public enum SideKey
    {
        /// <summary>Long side facing most to the south (−Y).</summary>
        A = 0,

        /// <summary>Long side opposite A.</summary>
        B = 1,

        /// <summary>Short side facing most to the west (−X).</summary>
        C = 2,

        /// <summary>Short side opposite C.</summary>
        D = 3,
    }

    /// <summary>Which side the 3D view looks at.</summary>
    public enum SideChoice
    {
        /// <summary>The largest side of any selected element (its long side A).</summary>
        Auto = 0,

        /// <summary>Long side A of the element with the largest side.</summary>
        LongA = 1,

        /// <summary>Long side B (the back) of the element with the largest side.</summary>
        LongB = 2,

        /// <summary>Short side C of the element with the largest side.</summary>
        ShortC = 3,

        /// <summary>Short side D of the element with the largest side.</summary>
        ShortD = 4,

        /// <summary>A face the user picked in the model.</summary>
        PickedFace = 5,
    }

    /// <summary>How the other elements are hidden in the new 3D view.</summary>
    public enum IsolationMode
    {
        /// <summary>Temporary Hide/Isolate: Revit drops it when the view is closed or "Reset Temporary Hide/Isolate" is used.</summary>
        Temporary = 0,

        /// <summary>Isolate, then convert to permanent "Hide in View".</summary>
        Permanent = 1,

        /// <summary>No hiding; only the section box limits the view.</summary>
        None = 2,
    }

    /// <summary>Where the camera of the new 3D view stands.</summary>
    public enum CameraMode
    {
        /// <summary>Straight in front of the chosen side, level (an elevation-like 3D view).</summary>
        FacingSide = 0,

        /// <summary>In front of the chosen side, 30° to the right and 35° above (a 3D view of that side).</summary>
        Angled = 1,
    }

    /// <summary>Detail level of the new view (ignored when a view template controls it).</summary>
    public enum DetailChoice
    {
        /// <summary>Coarse.</summary>
        Coarse = 0,

        /// <summary>Medium.</summary>
        Medium = 1,

        /// <summary>Fine.</summary>
        Fine = 2,
    }

    /// <summary>Visual style of the new view (ignored when a view template controls it).</summary>
    public enum StyleChoice
    {
        /// <summary>Hidden Line.</summary>
        HiddenLine = 0,

        /// <summary>Shaded.</summary>
        Shaded = 1,

        /// <summary>Consistent Colors.</summary>
        ConsistentColors = 2,

        /// <summary>Realistic.</summary>
        Realistic = 3,
    }
}
