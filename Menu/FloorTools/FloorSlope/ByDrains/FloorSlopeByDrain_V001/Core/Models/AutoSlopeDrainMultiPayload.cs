// File: AutoSlopeDrainMultiPayload.cs
// Location: Core/Models/
//
// NEW (V008), per Rafi's confirmed multi-floor decision (2026-09-08).
// AutoSlopeDrainPayload itself stayed per-floor (FloorId + SelectedDrains +
// TotalDetectedCount + the shared slope/threshold/marker/export settings,
// copied identically onto every floor's payload). This wrapper carries the
// full batch of per-floor payloads across the ExternalEvent boundary as one
// unit, so AutoSlopeDrainHandler can run every floor inside a single
// TransactionGroup (one combined Undo entry) and report back once with
// every floor's result.

using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class AutoSlopeDrainMultiPayload
    {
        /// <summary>One payload per floor the user ran — each carries its own selected drains but shares slope/threshold/marker/export settings.</summary>
        public List<AutoSlopeDrainPayload> FloorPayloads { get; set; }

        /// <summary>Shared log callback, used for messages that apply to the whole batch (e.g. the combined summary export) rather than one floor.</summary>
        public Action<LogEntry> Log { get; set; }

        /// <summary>
        /// When true and more than one floor actually ran, the Handler writes one
        /// extra Excel workbook after the batch comparing every floor's result
        /// (longest path, highest elevation, drain count) — in addition to each
        /// floor's own per-floor workbook, which AutoSlopeDrainEngine already
        /// writes per FloorPayloads entry.
        /// </summary>
        public bool WriteCombinedSummary { get; set; }

        public string ExportFolderPath { get; set; }

        public string ProjectTitle { get; set; }

        /// <summary>Called exactly once, after every floor in FloorPayloads has been processed (success or failure) and the combined summary (if any) has been written.</summary>
        public Action<List<AutoSlopeDrainFloorResult>> OnAllCompleted { get; set; }
    }

    /// <summary>One floor's outcome within a multi-floor run.</summary>
    public class AutoSlopeDrainFloorResult
    {
        public string FloorName { get; set; }
        public long FloorElementId { get; set; }
        public AutoSlopeDrainResult Result { get; set; }
    }
}
