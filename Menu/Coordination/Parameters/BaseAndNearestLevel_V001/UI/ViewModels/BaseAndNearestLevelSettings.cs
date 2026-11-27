namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// Remembered between sessions in %AppData%\Revit26_Plugin_Revised\Base and Nearest Level.
    /// <see cref="Version"/> lets later releases migrate the file. "Existing values" (Skip / Rewrite), the
    /// per-row choices and the shape-edited override are not remembered on purpose: each always starts off.
    /// </summary>
    public class BaseAndNearestLevelSettings
    {
        /// <summary>Settings format version.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Shared parameter file last used for a new parameter.</summary>
        public string SharedParamFilePath { get; set; } = string.Empty;

        /// <summary>Group inside that file last used for a new parameter.</summary>
        public string FileGroup { get; set; } = "Data";

        /// <summary>Last parameter name for each of the four slots, in <c>TargetSlot</c> order.</summary>
        public string[] SlotNames { get; set; } = { string.Empty, string.Empty, string.Empty, string.Empty };

        /// <summary>Nearest-level rule, as a <c>NearestRule</c> name. Unknown or missing → closest in either direction.</summary>
        public string NearestRule { get; set; } = nameof(Core.NearestRule.ClosestEitherWay);

        /// <summary>Only levels whose "Building Story" parameter is ticked are candidates.</summary>
        public bool BuildingStoreysOnly { get; set; } = true;

        /// <summary>Differences below this many millimetres count as zero.</summary>
        public double ToleranceMm { get; set; } = 1.0;

        /// <summary>The "Target parameters" card is open.</summary>
        public bool TargetExpanded { get; set; } = true;

        /// <summary>The "Nearest level" card is open.</summary>
        public bool NearestExpanded { get; set; } = true;

        /// <summary>The "Pre-checks" card is open.</summary>
        public bool ChecksExpanded { get; set; } = true;

        /// <summary>The activity log is open.</summary>
        public bool LogExpanded { get; set; }
    }
}
