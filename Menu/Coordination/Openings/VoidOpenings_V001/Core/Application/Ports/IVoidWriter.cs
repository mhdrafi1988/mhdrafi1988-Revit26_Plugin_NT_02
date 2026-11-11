using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>
    /// Places void instances in the active model, cuts the structure, assigns Job IDs and stores the
    /// records. Implemented on the Revit side; call in the Revit API context.
    /// </summary>
    public interface IVoidWriter
    {
        /// <summary>
        /// Places every opening (geometry in the active model's coordinates). One outcome per placement,
        /// in the same order; a failure of one never stops the others.
        /// </summary>
        IReadOnlyList<PlacementOutcome> Place(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings, ICollection<string> notes);
    }

    /// <summary>Shows things in the Revit view. Implemented on the Revit side; call in the Revit API context.</summary>
    public interface IViewNavigator
    {
        /// <summary>Zooms the active view to a box of <paramref name="sizeMm"/> around a point in host millimetres.</summary>
        void ZoomTo(Vec3 hostPointMm, double sizeMm);

        /// <summary>Selects (highlights) one element of the host or of a link. Returns false when it cannot be found.</summary>
        bool Select(ModelRef model, string uniqueId);
    }
}
