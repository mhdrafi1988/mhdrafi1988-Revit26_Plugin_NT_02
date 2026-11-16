using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>
    /// Shows things in the active Revit view. Implemented on the Revit side; call in the Revit API
    /// context. Both methods throw <see cref="System.InvalidOperationException"/> with a message for
    /// the user when the request cannot be shown (no model view active, element or link gone).
    /// </summary>
    public interface IViewNavigator
    {
        /// <summary>Zooms the active view to a box given in host millimetres.</summary>
        void ZoomTo(Aabb boxMm);

        /// <summary>Selects elements of the host or of a link, so Revit highlights them.</summary>
        void Select(ModelRef model, IReadOnlyList<long> elementIds);
    }
}
