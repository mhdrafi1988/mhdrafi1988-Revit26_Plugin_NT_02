using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.IO;
using Xunit;

namespace WetAreaWaterproofing.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void DefaultsMatchSpec()
        {
            var s = new WetAreaSettings();
            Assert.Equal(1, s.Version);
            Assert.Equal(new[] { "toilet", "wc", "bath", "shower", "kitchen", "pantry", "laundry", "wash", "balcony" }, s.Keywords);
            Assert.Equal(5, s.SnapMm);
            Assert.Equal(50, s.BridgeMm);
            Assert.Equal(2, s.AreaTolerancePct);
            Assert.False(s.AllowApproximate);
            Assert.Equal(0, s.OffsetMm);
            Assert.True(s.IncludeHost);
            Assert.Empty(s.IncludedLinks);
            Assert.True(s.AutoSaveReport);
            Assert.True(s.WriteComments);
            Assert.Equal(ReRunMode.UpdateInPlace, s.ReRun);
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Revit26_Plugin_Revised", "WetAreaWaterproofing", "Reports"),
                s.Normalize().ReportFolder);
        }

        [Fact]
        public void NormalizeClampsAndFillsNulls()
        {
            var s = new WetAreaSettings { SnapMm = -1, BridgeMm = 2, AreaTolerancePct = 0, Keywords = null, IncludedLinks = null, ReportFolder = " " }.Normalize();
            Assert.Equal(5, s.SnapMm);
            Assert.Equal(5, s.BridgeMm); // never below the snap distance
            Assert.Equal(2, s.AreaTolerancePct);
            Assert.Equal(9, s.Keywords.Count);
            Assert.NotNull(s.IncludedLinks);
            Assert.EndsWith("Reports", s.ReportFolder);
        }

        [Fact]
        public void NormalizeTrimsAndDeduplicatesKeywords()
        {
            var s = new WetAreaSettings { Keywords = new() { " Toilet ", "toilet", "", "WC" } }.Normalize();
            Assert.Equal(new[] { "toilet", "wc" }, s.Keywords);
        }
    }
}
