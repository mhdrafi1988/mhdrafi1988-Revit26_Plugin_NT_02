using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    /// <summary>
    /// V034: the run orchestrator (Application/AutoSlopeRunner) with fake Revit steps.
    /// Checks the order of steps, rollback and the expected-failure messages without Revit.
    /// </summary>
    public class RunnerTests
    {
        // ── Fakes ──────────────────────────────────────────────────────────

        /// <summary>A straight roof of 4 shape points, 1 ft apart, drain at point 0.</summary>
        private sealed class FakeRoof : IRoofModel
        {
            public string NotReady;
            public List<int> Drains = new List<int> { 0 };
            public Dictionary<int, int> GroupOfDrain = new Dictionary<int, int> { [0] = 0 };
            public Action OnBuildGraph;
            public readonly List<string> Calls = new List<string>();
            public readonly List<Point3> Positions = Enumerable.Range(0, 4).Select(i => new Point3(i, 0, 0)).ToList();

            public string CheckReady(AutoSlopeRequest request) { Calls.Add("Check"); return NotReady; }
            public RoofInfo Describe() => new RoofInfo(42, "Test roof", "Level 1", 0);

            public PreparedRoofData Prepare(AutoSlopeRequest request, Action<RunProgressInfo> progress,
                Action<LogLevel, string> log, CancellationToken cancel)
            {
                Calls.Add("Prepare");
                return new PreparedRoofData { Positions = Positions };
            }

            public DrainResolution ResolveDrains(AutoSlopeRequest request, Action<LogLevel, string> log)
            {
                Calls.Add("Drains");
                var r = new DrainResolution { PickedCount = request.PickedPoints.Count, GroupCount = GroupOfDrain.Values.Distinct().Count() };
                r.DrainVertices.AddRange(Drains);
                foreach (var kv in GroupOfDrain) r.GroupOfDrain[kv.Key] = kv.Value;
                return r;
            }

            public PathGraph BuildGraph(AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel)
            {
                Calls.Add("Graph");
                OnBuildGraph?.Invoke();
                var g = new PathGraph(Positions.Count);
                for (int i = 0; i + 1 < Positions.Count; i++) g.AddEdge(i, i + 1, 1.0);
                return g;
            }

            public IReadOnlyList<Point3> GetOuterBoundaryCorners(double minAngleDeg) => new List<Point3>();

            public ApplyOutcome ApplyElevations(ShortestPathResult paths, IReadOnlyList<int> drainVertices,
                AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel)
            {
                Calls.Add("Apply");
                var o = new ApplyOutcome();
                for (int i = 0; i < Positions.Count; i++)
                {
                    o.Vertices.Add(new VertexData { VertexIndex = i, Position = Positions[i], Outcome = VertexOutcome.Processed });
                    o.Processed++;
                    o.LongestPathFt = Math.Max(o.LongestPathFt, paths.Distance[i]);
                }
                return o;
            }

            public ReadbackOutcome ReadBack(IReadOnlyList<VertexData> rows, IReadOnlyList<int> drainVertices)
            {
                Calls.Add("ReadBack");
                return new ReadbackOutcome();
            }

            public ParameterWriteOutcome WriteParameters(AutoSlopeRequest request, double highestElevationMm,
                double longestPathFt, int processed, int skipped, int durationSec, int finalDrainCount,
                string runDate, string version)
            {
                Calls.Add("Parameters");
                return new ParameterWriteOutcome { Written = 1 };
            }
        }

        private sealed class FakeAnnotations : IAnnotationPlacer
        {
            public int MarkerCalls, RidgeCalls;
            public bool ThrowOnMarkers;

            public MarkerCounts PlaceMarkers(IReadOnlyList<Point3> drainPoints, IReadOnlyList<VertexData> rows,
                AutoSlopeRequest request, Action<LogLevel, string> log)
            {
                MarkerCalls++;
                if (ThrowOnMarkers) throw new AutoSlopeAbortException("Marker step failed.");
                return new MarkerCounts();
            }

            public RidgeLineOutcome PlaceRidgeLines(IReadOnlyList<Point3> positions, RidgeResult ridges,
                AutoSlopeRequest request, Action<LogLevel, string> log)
            {
                RidgeCalls++;
                return new RidgeLineOutcome();
            }
        }

        private sealed class FakeChanges : IChangeScopeFactory
        {
            public int Begun, Committed, Disposed;

            private sealed class Scope : IChangeScope
            {
                private readonly FakeChanges _owner;
                public Scope(FakeChanges owner) => _owner = owner;
                public void Commit() => _owner.Committed++;
                public void Dispose() => _owner.Disposed++;
            }

            public IChangeScope Begin(string name) { Begun++; return new Scope(this); }
        }

        private sealed class FakeExporter : IResultExporter
        {
            public int Calls;
            public string ExportAuto(AutoSlopeResult result, Action<LogLevel, string> log) { Calls++; return "out.xlsx"; }
        }

        private sealed class FeetUnits : ILengthUnits
        {
            public double MToFt(double m) => m / 0.3048;
            public double MmToFt(double mm) => mm / 304.8;
            public double FtToM(double ft) => ft * 0.3048;
        }

        private readonly FakeRoof _roof = new FakeRoof();
        private readonly FakeAnnotations _annotations = new FakeAnnotations();
        private readonly FakeChanges _changes = new FakeChanges();
        private readonly FakeExporter _exporter = new FakeExporter();
        private readonly List<(LogLevel Level, string Text)> _log = new List<(LogLevel, string)>();

        private AutoSlopeResult Run(AutoSlopeRequest request, CancellationToken cancel = default) =>
            new AutoSlopeRunner(_roof, _annotations, _changes, _exporter, new FeetUnits(), "34.0.0")
                .Run(request, _ => { }, (l, t) => _log.Add((l, t)), cancel);

        private static AutoSlopeRequest Request(bool export = false, bool ridges = false) => new AutoSlopeRequest
        {
            RoofIdValue = 42,
            PickedPoints = new List<Point3> { new Point3(0, 0, 0) },
            SlopePercent = 2,
            MaxDrainDistanceM = 100,
            ExportToExcel = export,
            DrawRidgeLines = ridges
        };

        // ── Tests ──────────────────────────────────────────────────────────

        [Fact]
        public void Success_RunsStepsInOrder_AndCommitsOneChange()
        {
            AutoSlopeResult result = Run(Request());

            Assert.True(result.Success);
            Assert.Equal(new[] { "Check", "Prepare", "Drains", "Graph", "Apply", "ReadBack", "Parameters" }, _roof.Calls);
            Assert.Equal(1, _changes.Begun);
            Assert.Equal(1, _changes.Committed);
            Assert.Equal(1, _annotations.MarkerCalls);
            Assert.Equal(4, result.VerticesProcessed);
            Assert.Equal("34.0.0", result.ToolVersion);
            Assert.Equal(Math.Round(3 * 0.3048, 2), result.LongestPathM);
        }

        [Fact]
        public void NotReady_FailsWithTheReason_AndChangesNothing()
        {
            _roof.NotReady = "The roof has no shape points.";

            AutoSlopeResult result = Run(Request());

            Assert.False(result.Success);
            Assert.Equal("The roof has no shape points.", result.ErrorMessage);
            Assert.Equal(new[] { "Check" }, _roof.Calls);
            Assert.Equal(0, _changes.Begun);
            Assert.Contains(_log, e => e.Level == LogLevel.Error && e.Text.Contains("Nothing was changed"));
        }

        [Fact]
        public void NoDrainFound_FailsBeforeTheGraph()
        {
            _roof.Drains.Clear();

            AutoSlopeResult result = Run(Request());

            Assert.False(result.Success);
            Assert.Equal("No roof shape point could be used as a drain.", result.ErrorMessage);
            Assert.DoesNotContain("Graph", _roof.Calls);
            Assert.Equal(0, _changes.Begun);
        }

        [Fact]
        public void Cancel_ReturnsCancelled_AndNeverOpensTheChange()
        {
            using var cts = new CancellationTokenSource();
            _roof.OnBuildGraph = cts.Cancel;

            AutoSlopeResult result = Run(Request(), cts.Token);

            Assert.True(result.WasCancelled);
            Assert.False(result.Success);
            Assert.Equal(0, _changes.Begun);
        }

        [Fact]
        public void FailureInsideTheChange_RollsItBack()
        {
            _annotations.ThrowOnMarkers = true;

            AutoSlopeResult result = Run(Request());

            Assert.False(result.Success);
            Assert.Equal(1, _changes.Begun);
            Assert.Equal(0, _changes.Committed);   // disposed without Commit = rolled back
            Assert.Equal(1, _changes.Disposed);
            Assert.DoesNotContain("ReadBack", _roof.Calls);
        }

        [Fact]
        public void Export_RunsOnlyWhenSwitchedOn()
        {
            Run(Request(export: false));
            Assert.Equal(0, _exporter.Calls);

            AutoSlopeResult result = Run(Request(export: true));
            Assert.Equal(1, _exporter.Calls);
            Assert.Equal("out.xlsx", result.ExportedFilePath);
        }

        [Fact]
        public void RidgeLines_SkippedWithOneDrainGroup()
        {
            AutoSlopeResult result = Run(Request(ridges: true));

            Assert.True(result.Success);
            Assert.Equal(0, _annotations.RidgeCalls);
            Assert.Contains(_log, e => e.Level == LogLevel.Warning && e.Text.StartsWith("Ridge lines skipped: they need 2 or more drain groups"));
        }
    }
}
