using System;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Xunit;
using static MergeRoofs.Tests.TestShapes;

namespace MergeRoofs.Tests
{
    public class CancellationTests
    {
        private const double Tol = 5.0 / 304.8;

        [Fact]
        public void Analyze_CallsCheck_InsideItsLoops()
        {
            int calls = 0;
            MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 5, 0, 15, 10), Rect("3", 20, 0, 30, 10) }, Tol, () => calls++);
            Assert.True(calls >= 3, $"check was called {calls} times");
        }

        [Fact]
        public void Analyze_PropagatesCancel_WhenCheckThrows()
        {
            Assert.Throws<OperationCanceledException>(() =>
                MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 5, 0, 15, 10) }, Tol,
                    () => throw new OperationCanceledException()));
        }

        [Fact]
        public void Analyze_WithoutCheck_StillWorks()
        {
            var r = MergeAnalyzer.Analyze(new[] { Rect("1", 0, 0, 10, 10), Rect("2", 10, 0, 20, 10) }, Tol);
            Assert.True(r.CanCreate);
        }
    }
}
