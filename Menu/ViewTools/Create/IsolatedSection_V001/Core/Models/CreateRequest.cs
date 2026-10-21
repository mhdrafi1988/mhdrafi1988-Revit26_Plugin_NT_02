using System.Collections.Generic;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models
{
    /// <summary>Everything needed to create one isolated 3D view. Ids only, no Revit objects.</summary>
    public sealed class CreateRequest
    {
        /// <summary>The selected elements (re-read from the model before use).</summary>
        public IReadOnlyList<PickedElement> Elements { get; init; } = new List<PickedElement>();

        /// <summary>Side to look at.</summary>
        public SideChoice Side { get; init; }

        /// <summary>Outward plan normal of the picked face, when <see cref="Side"/> is <see cref="SideChoice.PickedFace"/>.</summary>
        public Vec2? PickedFaceNormal { get; init; }

        /// <summary>How other elements are hidden.</summary>
        public IsolationMode Isolation { get; init; }

        /// <summary>Camera placement.</summary>
        public CameraMode Camera { get; init; }

        /// <summary>Detail level (skipped when a template is applied).</summary>
        public DetailChoice Detail { get; init; }

        /// <summary>Visual style (skipped when a template is applied).</summary>
        public StyleChoice Style { get; init; }

        /// <summary>Margin left/right of the elements (mm).</summary>
        public double SideMarginMm { get; init; }

        /// <summary>Margin in front of/behind the elements (mm).</summary>
        public double DepthMarginMm { get; init; }

        /// <summary>Margin above/below the elements (mm).</summary>
        public double TopBottomMarginMm { get; init; }

        /// <summary>View name pattern.</summary>
        public string NamePattern { get; init; }

        /// <summary>3D ViewFamilyType id.</summary>
        public long ViewTypeId { get; init; }

        /// <summary>3D view template id, or −1 for none.</summary>
        public long TemplateId { get; init; } = -1;

        /// <summary>Open the new view and zoom to fit.</summary>
        public bool OpenView { get; init; }
    }

    /// <summary>What <see cref="CreateRequest"/> produced.</summary>
    public sealed class CreateResult
    {
        /// <summary>Id of the new 3D view.</summary>
        public long ViewId { get; set; }

        /// <summary>Name of the new 3D view.</summary>
        public string ViewName { get; set; }

        /// <summary>How the view was aligned.</summary>
        public string AlignmentText { get; set; }

        /// <summary>Section box width × height × depth in mm.</summary>
        public string BoxText { get; set; }

        /// <summary>Elements used (after re-reading).</summary>
        public int ElementCount { get; set; }

        /// <summary>Non-fatal problems, one per line.</summary>
        public List<string> Warnings { get; } = new();
    }
}
