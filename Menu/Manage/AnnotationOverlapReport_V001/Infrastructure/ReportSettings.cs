using System;
using System.Collections.Generic;
using System.IO;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>
    /// The tool's saved options, in %AppData%\Revit26_Plugin_Revised\AnnotationOverlapReport\settings.json.
    /// </summary>
    public sealed class ReportSettings
    {
        /// <summary>Format version of the settings file.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Annotation type names the user unticked.</summary>
        public List<string> UncheckedTypes { get; set; } = new List<string>();

        /// <summary>"Also check overlaps between different types"; on by default.</summary>
        public bool AcrossTypes { get; set; } = true;

        /// <summary>Folder of the last Excel export.</summary>
        public string LastExportFolder { get; set; }

        private static string FilePath => JsonFileStore.PathFor("AnnotationOverlapReport", "settings.json");

        /// <summary>Loads the settings; defaults when the file is missing or unreadable. Never throws.</summary>
        public static ReportSettings Load()
        {
            try
            {
                ReportSettings settings = JsonFileStore.Load<ReportSettings>(FilePath, out _);
                settings.UncheckedTypes ??= new List<string>();
                return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ReportSettings();
            }
        }

        /// <summary>Saves the settings. Returns false (and the reason) when the file cannot be written.</summary>
        public bool TrySave(out string error)
        {
            error = null;
            try
            {
                Version = 1;
                JsonFileStore.Save(FilePath, this);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
