using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection
{
    /// <summary>What to analyse.</summary>
    public sealed class AnalysisRequest
    {
        /// <summary>Structural model.</summary>
        public ModelRef Structural { get; set; }

        /// <summary>Structural categories to check.</summary>
        public IReadOnlyList<StructuralCategory> StructuralCategories { get; set; }

        /// <summary>MEP model.</summary>
        public ModelRef Mep { get; set; }

        /// <summary>MEP categories to check.</summary>
        public IReadOnlyList<MepCategory> MepCategories { get; set; }

        /// <summary>Clearance rules and options.</summary>
        public VoidOpeningsSettings Settings { get; set; }
    }

    /// <summary>Progress of an analysis.</summary>
    public readonly struct AnalysisProgress
    {
        /// <summary>What is happening, for the status line.</summary>
        public string Stage { get; }

        /// <summary>0..1 within the detection stage; -1 while the amount of work is not known.</summary>
        public double Fraction { get; }

        /// <summary>Creates a progress report.</summary>
        public AnalysisProgress(string stage, double fraction)
        {
            Stage = stage;
            Fraction = fraction;
        }
    }

    /// <summary>What an analysis found.</summary>
    public sealed class AnalysisResult
    {
        /// <summary>The request the result answers.</summary>
        public AnalysisRequest Request { get; set; }

        /// <summary>Openings of this run, in host coordinates, already compared with stored records.</summary>
        public IReadOnlyList<OpeningData> Openings { get; set; }

        /// <summary>Stored openings whose clash was not found again, each with its state (cut and still used, moved, or gone).</summary>
        public IReadOnlyList<StoredOpeningCheck> StoredOnly { get; set; }

        /// <summary>Things the user should know: skipped elements, unreadable geometry.</summary>
        public IReadOnlyList<string> Notes { get; set; }

        /// <summary>Transform of the structural model into the host.</summary>
        public Transform3 StructuralToHost { get; set; }

        /// <summary>Number of MEP elements analysed.</summary>
        public int MepElementCount { get; set; }

        /// <summary>Number of structural elements analysed.</summary>
        public int StructuralElementCount { get; set; }
    }

    /// <summary>
    /// The Analyze button: read both models in the Revit context, detect and size the openings on a
    /// background thread, and compare them with the voids that already exist.
    /// </summary>
    public sealed class AnalyzeClashesUseCase
    {
        // Structure further than this from every MEP element cannot clash and is not read.
        private const double SearchMarginMm = 1000;

        private readonly IModelReader _reader;
        private readonly IRevitDispatcher _dispatcher;

        /// <summary>Creates the use case.</summary>
        public AnalyzeClashesUseCase(IModelReader reader, IRevitDispatcher dispatcher)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>Runs the analysis. Throws <see cref="InvalidOperationException"/> with a message for the user when it cannot start.</summary>
        public async Task<AnalysisResult> RunAsync(AnalysisRequest request, IProgress<AnalysisProgress> progress, CancellationToken cancellationToken)
        {
            Validate(request);
            var notes = new List<string>();

            progress?.Report(new AnalysisProgress("Reading MEP elements…", -1));
            MepModel mep = await _dispatcher.RunAsync(() => _reader.ReadMep(request.Mep, request.MepCategories, notes));
            cancellationToken.ThrowIfCancellationRequested();

            var result = new AnalysisResult
            {
                Request = request, Notes = notes, Openings = new List<OpeningData>(), StoredOnly = new List<StoredOpeningCheck>(),
                StructuralToHost = Transform3.Identity, MepElementCount = mep.Elements.Count,
            };

            StructuralModel structural = null;
            Aabb? bounds = BoundsOf(mep);
            if (bounds.HasValue)
            {
                progress?.Report(new AnalysisProgress("Reading structural elements…", -1));
                structural = await _dispatcher.RunAsync(() =>
                    _reader.ReadStructural(request.Structural, request.StructuralCategories, bounds.Value.Expand(SearchMarginMm), notes));
                cancellationToken.ThrowIfCancellationRequested();
                result.StructuralToHost = structural.ToHost;
                result.StructuralElementCount = structural.Elements.Count;
            }

            IReadOnlyList<StoredOpening> stored = await _dispatcher.RunAsync(() => _reader.ReadStoredOpenings(request.Structural));

            // From here on only snapshots are used, so the work can leave the Revit thread.
            List<OpeningData> openings = structural == null
                ? new List<OpeningData>()
                : await Task.Run(() => DetectAndSize(structural, mep, request.Settings, progress, cancellationToken), cancellationToken);

            result.StoredOnly = OpeningReconciler.Reconcile(openings, stored, result.StructuralToHost, request.Mep.ModelKey,
                request.StructuralCategories, request.MepCategories, mep);
            result.Openings = openings;
            return result;
        }

        private static List<OpeningData> DetectAndSize(StructuralModel structural, MepModel mep, VoidOpeningsSettings settings,
            IProgress<AnalysisProgress> progress, CancellationToken cancellationToken)
        {
            IProgress<double> detection = progress == null
                ? null
                : new Relay(fraction => progress.Report(new AnalysisProgress("Detecting clashes…", fraction)));
            IReadOnlyList<ClashData> clashes = new ClashEngine().Detect(structural, mep, detection, cancellationToken);

            var calculator = new OpeningCalculator(settings.ToRuleSet(), settings.ToOpeningSettings());
            var openings = new List<OpeningData>(clashes.Count);
            foreach (ClashData clash in clashes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                openings.Add(calculator.Calculate(clash));
            }
            return openings;
        }

        private static Aabb? BoundsOf(MepModel mep)
        {
            var points = new List<Vec3>();
            foreach (MepElementData element in mep.Elements)
                foreach (MepSegment segment in element.Segments)
                {
                    points.Add(segment.Axis.Start);
                    points.Add(segment.Axis.End);
                }
            return points.Count == 0 ? (Aabb?)null : Aabb.FromPoints(points);
        }

        private static void Validate(AnalysisRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Structural == null) throw new InvalidOperationException("Choose the structural model first.");
            if (request.Mep == null) throw new InvalidOperationException("Choose the MEP model first.");
            if (!request.Structural.IsLoaded) throw new InvalidOperationException("The structural link is not loaded. Load it in Manage Links, then press Refresh.");
            if (!request.Mep.IsLoaded) throw new InvalidOperationException("The MEP link is not loaded. Load it in Manage Links, then press Refresh.");
            if (request.StructuralCategories == null || request.StructuralCategories.Count == 0)
                throw new InvalidOperationException("Tick at least one structural category.");
            if (request.MepCategories == null || request.MepCategories.Count == 0)
                throw new InvalidOperationException("Tick at least one MEP category.");
            if (request.Settings == null) throw new ArgumentException("Settings are required.", nameof(request));
        }

        private sealed class Relay : IProgress<double>
        {
            private readonly Action<double> _report;

            public Relay(Action<double> report) => _report = report;

            public void Report(double value) => _report(value);
        }
    }
}
