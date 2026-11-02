using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Payload the ViewModel sets on the handler before raising the event
    /// (ARCHITECTURE.md §2). It lives in Infrastructure rather than Core/Models
    /// because it names a Revit type.
    /// </summary>
    internal class RenumberRequest
    {
        public ViewSheet Sheet { get; init; }

        public RenumberOptions Options { get; init; }
    }
}
