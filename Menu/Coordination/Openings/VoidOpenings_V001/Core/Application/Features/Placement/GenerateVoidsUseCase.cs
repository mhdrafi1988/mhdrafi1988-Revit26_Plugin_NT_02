using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement
{
    /// <summary>Counts of a Generate or Apply run.</summary>
    public sealed class GenerateSummary
    {
        /// <summary>Voids placed and the cut verified.</summary>
        public int Cut { get; set; }

        /// <summary>Voids placed but the structure not cut.</summary>
        public int Generated { get; set; }

        /// <summary>Openings that already had an unchanged void.</summary>
        public int Existing { get; set; }

        /// <summary>Openings that could not be placed.</summary>
        public int Failed { get; set; }

        /// <summary>Notes from the writer (family built, skipped elements).</summary>
        public IReadOnlyList<string> Notes { get; set; } = new List<string>();

        /// <summary>The outcomes, one per placement.</summary>
        public IReadOnlyList<PlacementOutcome> Outcomes { get; set; } = new List<PlacementOutcome>();

        /// <summary>One sentence for the notification strip.</summary>
        public string ToSentence()
            => $"{Cut} cut, {Generated} placed without a cut, {Existing} already there, {Failed} failed.";
    }

    /// <summary>
    /// The Generate Void button and the Apply package button: hand placements to the Revit writer and
    /// bring the outcome back onto the openings.
    /// </summary>
    public sealed class GenerateVoidsUseCase
    {
        private readonly IVoidWriter _writer;
        private readonly IRevitDispatcher _dispatcher;

        /// <summary>Creates the use case.</summary>
        public GenerateVoidsUseCase(IVoidWriter writer, IRevitDispatcher dispatcher)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// Generates voids for openings of the active model's structure and updates each opening's
        /// Job ID, status, tracking and note.
        /// </summary>
        public async Task<GenerateSummary> RunAsync(IReadOnlyList<OpeningData> openings, Transform3 structuralToHost, VoidOpeningsSettings settings)
        {
            if (openings == null) throw new ArgumentNullException(nameof(openings));
            List<VoidPlacement> placements = openings.Select(o => OpeningPackageBuilder.ToPlacement(o, structuralToHost)).ToList();
            GenerateSummary summary = await PlaceAsync(placements, settings);

            for (int i = 0; i < openings.Count && i < summary.Outcomes.Count; i++)
                Apply(openings[i], summary.Outcomes[i]);
            return summary;
        }

        /// <summary>Places the openings of a package in the active (structural) model.</summary>
        public Task<GenerateSummary> ApplyPackageAsync(OpeningPackage package, VoidOpeningsSettings settings)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            return PlaceAsync(package.Openings, settings);
        }

        private async Task<GenerateSummary> PlaceAsync(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings)
        {
            var notes = new List<string>();
            IReadOnlyList<PlacementOutcome> outcomes = placements.Count == 0
                ? new List<PlacementOutcome>()
                : await _dispatcher.RunAsync(() => _writer.Place(placements, settings, notes));

            return new GenerateSummary
            {
                Outcomes = outcomes,
                Notes = notes,
                Cut = outcomes.Count(o => o.Status == "Cut"),
                Generated = outcomes.Count(o => o.Status == "Generated"),
                Existing = outcomes.Count(o => o.Status == "Existing"),
                Failed = outcomes.Count(o => o.Status == "Failed"),
            };
        }

        private static void Apply(OpeningData opening, PlacementOutcome outcome)
        {
            opening.ResultNote = outcome.Message ?? string.Empty;
            if (outcome.Status == "Failed")
            {
                opening.Status = OpeningStatus.Failed;
                return;
            }

            opening.JobId = outcome.JobId;
            opening.InstanceUniqueId = outcome.InstanceUniqueId;
            opening.Tracking = TrackingState.Existing;
            if (outcome.Status == "Cut") opening.Status = OpeningStatus.Cut;
            else if (outcome.Status == "Generated") opening.Status = OpeningStatus.Generated;
            // "Existing": an unchanged void was already there; the opening keeps the status it had.
        }
    }
}
