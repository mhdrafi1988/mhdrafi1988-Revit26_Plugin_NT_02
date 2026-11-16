using System;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection
{
    /// <summary>Use case behind the result table's Zoom to and Highlight buttons.</summary>
    public sealed class NavigationService
    {
        private readonly IViewNavigator _navigator;
        private readonly IRevitDispatcher _dispatcher;

        /// <summary>Creates the service.</summary>
        public NavigationService(IViewNavigator navigator, IRevitDispatcher dispatcher)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>Zooms the active view to the opening.</summary>
        public Task ZoomToAsync(OpeningData opening)
        {
            Aabb box = ReviewedOpening.ZoomBox(opening);
            return _dispatcher.RunAsync(() => { _navigator.ZoomTo(box); return true; });
        }

        /// <summary>Selects the MEP element of the clash in Revit.</summary>
        public Task HighlightMepAsync(OpeningData opening)
        {
            ClashData clash = ClashOf(opening);
            return _dispatcher.RunAsync(() => { _navigator.Select(clash.MepModel, new[] { clash.Mep.ElementId }); return true; });
        }

        /// <summary>Selects the structural element of the clash in Revit.</summary>
        public Task HighlightStructureAsync(OpeningData opening)
        {
            ClashData clash = ClashOf(opening);
            return _dispatcher.RunAsync(() => { _navigator.Select(clash.StructuralModel, new[] { clash.Structural.ElementId }); return true; });
        }

        private static ClashData ClashOf(OpeningData opening)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));
            return opening.Clash ?? throw new ArgumentException("The opening has no clash.", nameof(opening));
        }
    }
}
