// ==============================================
// File: WorksetCreatorSettings.cs
// Layer: Core/Models  (no Revit API — CLAUDE.md)
// NEW in Workset Creator V016.
//
// Per-tool persisted settings, serialised by WorksetSettingsService to
//   %AppData%\Revit26_Plugin_Revised\WorksetCreator\settings.json
// A plain System.Text.Json POCO: public get/set, parameterless ctor, no
// attributes, no behaviour. Every member carries the value the tool uses when
// no file exists yet, so a fresh install and a corrupt file land on the same
// defaults.
//
// SchemaVersion exists so a later key addition does not need a file wipe:
// the service accepts any version it recognises and re-writes the file at the
// current version on the next save.
// ==============================================

namespace Revit26_Plugin_Revised.WorksetCreator.V016.Core.Models
{
    public sealed class WorksetCreatorSettings
    {
        /// <summary>Bump when a key changes meaning, not when one is added.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public const int CurrentSchemaVersion = 1;

        /// <summary>Workset name pattern. {name} is the linked file name.</summary>
        public string Pattern { get; set; } = "+Link({name})";

        /// <summary>
        /// How many worksets must share a prefix/suffix residue before the matcher
        /// treats it as a project naming convention and pre-selects Rename.
        /// Below this, the row goes to Needs Review instead.
        /// Deliberately not exposed in the UI — see WorksetMatchService.
        /// </summary>
        public int ConventionMinOccurrences { get; set; } = 3;

        /// <summary>
        /// Shortest normalised link name that may be fuzzy-matched at all. Below
        /// this, only an exact name match counts, because a two-character name is
        /// contained in half the model.
        /// </summary>
        public int MinNormalisedNameLength { get; set; } = 4;

        public bool IsLogExpanded { get; set; }

        /// <summary>
        /// Folder chosen once per session for the log export, remembered between
        /// sessions. Null until the user picks one.
        /// </summary>
        public string LogExportFolder { get; set; }

        /// <summary>
        /// Clamps anything a hand-edited file could have put out of range. Called by
        /// the service after a load, so the rest of the tool never guards these.
        /// </summary>
        public void Sanitise()
        {
            if (string.IsNullOrWhiteSpace(Pattern) || !Pattern.Contains("{name}"))
                Pattern = "+Link({name})";

            if (ConventionMinOccurrences < 1)  ConventionMinOccurrences = 1;
            if (ConventionMinOccurrences > 99) ConventionMinOccurrences = 99;

            if (MinNormalisedNameLength < 1)  MinNormalisedNameLength = 1;
            if (MinNormalisedNameLength > 99) MinNormalisedNameLength = 99;

            if (string.IsNullOrWhiteSpace(LogExportFolder)) LogExportFolder = null;
        }
    }
}
