using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary><see cref="IViewNavigator"/> for the active Revit view. Call only in the Revit API context.</summary>
    public sealed class ViewNavigator : IViewNavigator
    {
        private readonly RevitContext _context;

        /// <summary>Creates the navigator.</summary>
        public ViewNavigator(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public void ZoomTo(Vec3 hostPointMm, double sizeMm)
        {
            _context.RequireProject();
            UIDocument uiDocument = _context.Application.ActiveUIDocument;
            UIView view = uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uiDocument.ActiveView.Id)
                ?? throw new InvalidOperationException("The active view cannot be zoomed. Open a plan, section or 3D view.");

            XYZ centre = UnitsMm.ToXyz(hostPointMm);
            double half = UnitsMm.ToFeet(Math.Max(sizeMm, 500)) / 2;
            var offset = new XYZ(half, half, half);
            view.ZoomAndCenterRectangle(centre - offset, centre + offset);
        }

        /// <inheritdoc/>
        public bool Select(ModelRef model, string uniqueId)
        {
            Document host = _context.RequireProject();
            UIDocument uiDocument = _context.Application.ActiveUIDocument;
            if (model == null || string.IsNullOrEmpty(uniqueId))
                return false;

            if (model.IsHost)
            {
                Element element = host.GetElement(uniqueId);
                if (element == null)
                    return false;
                uiDocument.Selection.SetElementIds(new List<ElementId> { element.Id });
                return true;
            }

            // An element inside a link is selected through a link reference.
            var link = host.GetElement(model.LinkInstanceUniqueId ?? string.Empty) as RevitLinkInstance;
            Element linked = link?.GetLinkDocument()?.GetElement(uniqueId);
            if (linked == null)
                return false;
            Reference reference = new Reference(linked).CreateLinkReference(link);
            uiDocument.Selection.SetReferences(new List<Reference> { reference });
            return true;
        }
    }
}
