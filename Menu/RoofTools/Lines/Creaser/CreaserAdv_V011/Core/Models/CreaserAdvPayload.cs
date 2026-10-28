// =======================================================
// File: CreaserAdvPayload.cs
// Location: Core/Models/
// Carries everything CreaserAdvEngine needs to run a single Run() pass
// through the ExternalEvent boundary. Elements are referenced by Id only
// (RoofId, SelectedDetailSymbolId) — a modeless window cannot hold a live
// Element/FamilySymbol reference and expect it to stay valid for a
// button click that fires later, per this suite's standing convention.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.CreaserAdv.V011.Services;
using System;

namespace Revit26_Plugin_Revised.CreaserAdv.V011.Core.Models
{
    /// <summary>One Run request, handed from the window to <c>CreaserAdvHandler</c>.</summary>
    public class CreaserAdvPayload
    {
        /// <summary>
        /// The project the window was opened for. The handler refuses to run when another
        /// project is active, because <see cref="RoofId"/> and <see cref="SelectedDetailSymbolId"/>
        /// only mean something in this one.
        /// </summary>
        public Document Document { get; set; }

        public ElementId RoofId { get; set; }
        public ElementId SelectedDetailSymbolId { get; set; }

        public bool IncludeBoundaryLines { get; set; }
        public bool EnableDrainGrouping { get; set; }
        public bool EnableDijkstraPathFilter { get; set; }
        public bool EnableMinimumSlope { get; set; }
        public double MinimumSlopePercent { get; set; }
        public double DrainGroupingRadiusMm { get; set; }
        public bool EnableMinimumLength { get; set; }
        public double MinimumLengthMm { get; set; }

        /// <summary>Same LoggingService instance the ViewModel's LogEntries is bound to — thread-safe (marshals to the UI thread internally), so the engine can log directly.</summary>
        public LoggingService Log { get; set; }

        public Action<CreaserAdvResult> OnCompleted { get; set; }
    }
}
