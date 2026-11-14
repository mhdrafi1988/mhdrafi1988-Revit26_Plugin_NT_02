// =======================================================
// File: SettingsStore.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Infrastructure
// Remembers the last-used options in
// %AppData%\Revit26_Plugin_Revised\RoofSlopeBuilder\settings.json.
// =======================================================
using System;
using System.IO;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Infrastructure
{
    /// <summary>Loads and saves <see cref="RoofSlopeSettings"/>. Never throws.</summary>
    public static class SettingsStore
    {
        private const string Folder = "RoofSlopeBuilder";
        private const string FileName = "settings.json";

        /// <summary>
        /// Last-used settings, or the defaults when there is no file. A file that cannot be read
        /// is kept aside and <paramref name="warning"/> says so; otherwise it is null.
        /// </summary>
        public static RoofSlopeSettings Load(out string warning)
        {
            warning = null;
            try
            {
                RoofSlopeSettings s = JsonFileStore.Load<RoofSlopeSettings>(JsonFileStore.PathFor(Folder, FileName), out warning);
                // Version 0 = written before versioning; every field still means the same, so just stamp it.
                if (s.SettingsVersion < RoofSlopeSettings.CurrentVersion) s.SettingsVersion = RoofSlopeSettings.CurrentVersion;
                return Sanitise(s);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                warning = "Could not open the saved settings (" + ex.Message + "). Using the defaults.";
                return new RoofSlopeSettings();
            }
        }

        /// <summary>Saves <paramref name="s"/>. Returns a warning to show when it failed, otherwise null.</summary>
        public static string Save(RoofSlopeSettings s)
        {
            try
            {
                s.SettingsVersion = RoofSlopeSettings.CurrentVersion;
                JsonFileStore.Save(JsonFileStore.PathFor(Folder, FileName), s);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return "Your options could not be saved for next time: " + ex.Message;
            }
        }

        /// <summary>Puts out-of-range values from a hand-edited file back to their defaults.</summary>
        private static RoofSlopeSettings Sanitise(RoofSlopeSettings s)
        {
            RoofSlopeSettings d = new RoofSlopeSettings();
            if (!(s.Slope > 0 && s.Slope <= 0.5)) s.Slope = d.Slope;
            if (!(s.MergeTolerance > 0)) s.MergeTolerance = d.MergeTolerance;
            if (!(s.RidgeSpacing > 0)) s.RidgeSpacing = d.RidgeSpacing;
            if (!(s.SaddleWidthRatio > 0 && s.SaddleWidthRatio <= 1)) s.SaddleWidthRatio = d.SaddleWidthRatio;
            if (s.SlopeUnitIndex < 0 || s.SlopeUnitIndex > 2) s.SlopeUnitIndex = 0;
            if (!Enum.IsDefined(typeof(GapMode), s.GapMode)) s.GapMode = d.GapMode;
            if (!Enum.IsDefined(typeof(RidgeMode), s.RidgeMode)) s.RidgeMode = d.RidgeMode;
            return s;
        }
    }
}
