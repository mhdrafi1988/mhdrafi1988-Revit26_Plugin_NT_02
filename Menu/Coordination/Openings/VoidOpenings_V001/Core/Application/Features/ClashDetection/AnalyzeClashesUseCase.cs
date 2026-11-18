using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection
{
    /// <summary>
    /// Analyze: reads the MEP elements, reads the structural elements near them, finds where MEP
    /// passes through structure and sizes an opening for each place. Nothing in the model is changed.
    /// Geometry is read in batches in the Revit API context, so the window stays responsive and the
    /// run can be cancelled between batches; detection and sizing run on a background thread.
    /// </summary>
    public sealed class AnalyzeClashesUseCase
    {
        /// <summary>MEP elements read per batch (centrelines are cheap).</summary>
        public const int MepBatchSize = 400;

        /// <summary>Structural elements read per batch (solids are slow to triangulate).</summary>
        public const int StructuralBatchSize = 40;

        // Slack around the MEP elements when picking structural candidates.
        private const double RegionMarginMm = 100;

        private readonly IGeometryReader _reader;
        private readonly IRevitDispatcher _dispatcher;

        /// <summary>Creates the use case.</summary>
        public AnalyzeClashesUseCase(IGeometryReader reader, IRevitDispatcher dispatcher)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>Runs the analysis.</summary>
        /// <param name="request">Models, categories and clearance setup.</param>
        /// <param name="progress">Receives the stage and overall progress; may be null.</param>
        /// <param name="cancellationToken">Stops the run with an <see cref="OperationCanceledException"/>.</param>
        /// <exception cref="InvalidOperationException">The request cannot run, or a model is no longer available.</exception>
        public async Task<AnalysisResult> RunAsync(AnalysisRequest request, IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string problem = request.Problem();
            if (problem != null) throw new InvalidOperationException(problem);

            var watch = Stopwatch.StartNew();
            var notAnalysed = new List<SkippedElement>();

            // 1. MEP centrelines.
            progress?.Report(new AnalysisProgress("Reading MEP elements…", 0));
            IReadOnlyList<long> mepIds = await _dispatcher.RunAsync(() => _reader.ListMep(request.Mep, request.MepCategories));
            var mepElements = new List<MepElementData>();
            await ReadInBatchesAsync(mepIds, MepBatchSize, ids => _reader.ReadMep(request.Mep, ids), mepElements, notAnalysed,
                done => progress?.Report(new AnalysisProgress($"Reading MEP elements… {done:N0} of {mepIds.Count:N0}", 0.30 * done / Math.Max(1, mepIds.Count))),
                cancellationToken);

            if (mepElements.Count == 0)
                return Finish(request, new List<OpeningData>(), 0, 0, notAnalysed, watch, progress);

            // 2. Structural solids, only those near the MEP elements.
            progress?.Report(new AnalysisProgress("Reading structural elements…", 0.30));
            Aabb region = RegionOf(mepElements);
            IReadOnlyList<long> structuralIds = await _dispatcher.RunAsync(
                () => _reader.ListStructural(request.Structural, request.StructuralCategories, region));
            var structuralElements = new List<StructuralElementData>();
            await ReadInBatchesAsync(structuralIds, StructuralBatchSize, ids => _reader.ReadStructural(request.Structural, ids),
                structuralElements, notAnalysed,
                done => progress?.Report(new AnalysisProgress($"Reading structural elements… {done:N0} of {structuralIds.Count:N0}", 0.30 + 0.40 * done / Math.Max(1, structuralIds.Count))),
                cancellationToken);

            if (structuralElements.Count == 0)
                return Finish(request, new List<OpeningData>(), mepElements.Count, 0, notAnalysed, watch, progress);

            // 3. Detection and sizing, off the Revit thread: snapshots only from here on.
            progress?.Report(new AnalysisProgress("Finding clashes…", 0.70));
            var structural = new StructuralModel(request.Structural, structuralElements);
            var mep = new MepModel(request.Mep, mepElements);
            var calculator = new OpeningCalculator(request.Setup.ToRuleSet(), request.Setup.Options);
            IProgress<double> detectProgress = progress == null ? null
                : new InlineProgress(f => progress.Report(new AnalysisProgress("Finding clashes…", 0.70 + 0.28 * f)));

            List<OpeningData> openings = await Task.Run(() =>
            {
                IReadOnlyList<ClashData> clashes = new ClashEngine().Detect(structural, mep, detectProgress, cancellationToken);
                var sized = new List<OpeningData>(clashes.Count);
                foreach (ClashData clash in clashes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sized.Add(calculator.Calculate(clash));
                }
                return sized;
            }, cancellationToken);

            return Finish(request, openings, mepElements.Count, structuralElements.Count, notAnalysed, watch, progress);
        }

        private static AnalysisResult Finish(AnalysisRequest request, List<OpeningData> openings, int mepCount, int structuralCount,
            List<SkippedElement> notAnalysed, Stopwatch watch, IProgress<AnalysisProgress> progress)
        {
            progress?.Report(new AnalysisProgress("Done", 1));
            return new AnalysisResult(request, openings, mepCount, structuralCount, notAnalysed, watch.Elapsed);
        }

        private async Task ReadInBatchesAsync<T>(IReadOnlyList<long> ids, int batchSize,
            Func<IReadOnlyList<long>, GeometryBatch<T>> read, List<T> elements, List<SkippedElement> notAnalysed,
            Action<int> reportDone, CancellationToken cancellationToken)
        {
            for (int start = 0; start < ids.Count; start += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<long> slice = ids.Skip(start).Take(batchSize).ToList();
                GeometryBatch<T> batch = await _dispatcher.RunAsync(() => read(slice));
                if (batch != null)
                {
                    elements.AddRange(batch.Elements);
                    notAnalysed.AddRange(batch.NotAnalysed);
                }
                reportDone(Math.Min(ids.Count, start + batchSize));
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Box around every MEP centreline, grown by the largest half-size (insulation included) so an
        /// element whose body, not centreline, reaches a structural element still finds it.
        /// </summary>
        public static Aabb RegionOf(IReadOnlyList<MepElementData> elements)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            var points = new List<Vec3>();
            double reach = 0;
            foreach (MepElementData element in elements)
            {
                foreach (MepSegment segment in element.Segments)
                {
                    points.Add(segment.Axis.Start);
                    points.Add(segment.Axis.End);
                    MepProfile p = segment.Profile;
                    double half = p.Shape == ProfileShape.Round
                        ? p.DiameterMm / 2
                        : Math.Sqrt(p.WidthMm * p.WidthMm + p.HeightMm * p.HeightMm) / 2;
                    reach = Math.Max(reach, half + Math.Max(0, element.InsulationMm));
                }
            }
            return Aabb.FromPoints(points).Expand(reach + RegionMarginMm);
        }

        // Progress<T> posts to a synchronization context; detection reports from a worker thread and
        // the outer progress object does the marshalling, so this one just forwards.
        private sealed class InlineProgress : IProgress<double>
        {
            private readonly Action<double> _report;

            public InlineProgress(Action<double> report) => _report = report;

            public void Report(double value) => _report(value);
        }
    }
}
