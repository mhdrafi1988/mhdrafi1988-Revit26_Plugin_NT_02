using System.Linq;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class SampleSpacingTests
    {
        [Theory]
        [InlineData(0.5)]
        [InlineData(2.4)]
        [InlineData(2.75)]
        [InlineData(13.37)]
        [InlineData(328.084)]
        public void DefaultSpacing_GivesTheV033Count(double lengthFt)
        {
            int v033 = System.Math.Max(10, (int)(lengthFt * 4));

            Assert.Equal(v033, SampleSpacing.SampleCount(lengthFt, AutoSlopeConstants.DefaultSampleSpacingMm));
        }

        [Fact]
        public void FortyMm_SamplesEvery40Mm()
        {
            double tenMetresFt = 10000 / 304.8;

            Assert.InRange(SampleSpacing.SampleCount(tenMetresFt, 40), 249, 250);
        }

        [Fact]
        public void ShortLink_KeepsTheMinimumCount()
        {
            Assert.Equal(AutoSlopeConstants.MinEdgeSamples, SampleSpacing.SampleCount(0.2, 100));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(33)]
        [InlineData(double.NaN)]
        public void UnknownValue_FallsBackToTheDefault(double mm)
        {
            Assert.Equal(AutoSlopeConstants.DefaultSampleSpacingMm, SampleSpacing.Find(mm).Mm);
            Assert.Equal(0.25, SampleSpacing.ToFeet(mm));
        }

        [Fact]
        public void Options_AreTheFourChoices_WithOneDefault()
        {
            Assert.Equal(new double[] { 40, 50, 75, 100 }, SampleSpacing.Options.Select(o => o.Mm).ToArray());
            Assert.Equal("75 mm (default)", SampleSpacing.DefaultOption.Label);
            Assert.Equal("40 mm", SampleSpacing.Options[0].Label);
        }
    }
}
