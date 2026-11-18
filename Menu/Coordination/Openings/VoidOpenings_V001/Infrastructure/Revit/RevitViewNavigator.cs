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
    /// <summary><see cref="IViewNavigator"/> over the active Revit view. Call only in the Revit API context.</summary>
    public sealed class RevitViewNavigator : IViewNavigator
    {
        private readonly RevitContext _context;

        /// <summary>Creates the navigator.</summary>
        public RevitViewNavigator(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public void ZoomTo(Aabb boxMm)
        {
            _context.RequireProject();
            UIDocument uiDocument = _context.Application.ActiveUIDocument;
            View view = uiDocument.ActiveView;
            if (!(view is View3D || view is ViewPlan || view is ViewSection))
                throw new InvalidOperationException("Open a plan, section or 3D view, then press Zoom to again.");

            UIView uiView = uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (uiView == null)
                throw new InvalidOperationException("The active view cannot be zoomed. Click into a plan, section or 3D view first.");

            uiView.ZoomAndCenterRectangle(UnitsMm.ToXyz(boxMm.Min), UnitsMm.ToXyz(boxMm.Max));
        }

        /// <inheritdoc/>
        public void Select(ModelRef model, IReadOnlyList<long> elementIds)
        {
            Document host = _context.RequireProject();
            UIDocument uiDocument = _context.Application.ActiveUIDocument;
            ModelLocation location = LinkCatalog.Locate(host, model);

            List<Element> elements = (elementIds ?? new List<long>())
                .Select(id => location.Document.GetElement(new ElementId(id)))
                .Where(e => e != null && e.IsValidObject)
                .ToList();
            if (elements.Count == 0)
                throw new InvalidOperationException("The element is no longer in the model. Run Analyze again.");

            if (location.Link == null)
            {
                uiDocument.Selection.SetElementIds(elements.Select(e => e.Id).ToList());
                return;
            }

            // Elements of a link are selected through link references.
            var references = new List<Reference>();
            foreach (Element element in elements)
            {
                Reference reference = new Reference(element).CreateLinkReference(location.Link);
                if (reference != null)
                    references.Add(reference);
            }
            if (references.Count == 0)
                throw new InvalidOperationException($"The element could not be selected in the link {location.Name}.");
            uiDocument.Selection.SetReferences(references);
        }
    }
}
