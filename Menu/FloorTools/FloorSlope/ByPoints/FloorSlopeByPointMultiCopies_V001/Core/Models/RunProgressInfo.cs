// File: RunProgressInfo.cs
// Location: Core/Models/
//
// NEW, per Rafi's confirmed progress-bar/Cancel decision (ported from V010).
// A plain, UI-agnostic progress snapshot for ONE floor's run, reported via
// AutoSlopeDrainPayload.Progress at phase boundaries and, throttled, during
// DijkstraPathEngine.BuildGraph (the actual O(n^2) bottleneck on a large
// floor). Core/Engine and Core/Services never reference WPF — the ViewModel
// is what turns this into an on-screen percentage + phase label and pumps
// the UI so it actually repaints mid-run (see UiPumpHelper).

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models
{
    public class RunProgressInfo
    {
        /// <summary>Short label for what's happening right now, e.g. "Building path graph".</summary>
        public string PhaseLabel { get; set; }

        /// <summary>
        /// 0-100 within the CURRENT phase, or null when this phase's progress isn't
        /// cheap/meaningful to measure (e.g. writing parameters, exporting Excel) —
        /// the UI shows an indeterminate/paused bar for those instead of a fake %.
        /// </summary>
        public double? PercentWithinPhase { get; set; }

        public RunProgressInfo(string phaseLabel, double? percentWithinPhase = null)
        {
            PhaseLabel = phaseLabel;
            PercentWithinPhase = percentWithinPhase;
        }
    }
}
