namespace Revit26_Plugin_Revised.PlanFromScopeBox.V005.Infrastructure.Settings
{
    /// <summary>
    /// Plain POCO of user-tunable defaults persisted between sessions (H2). Serialized to
    /// %AppData%\Revit26_Plugin_Revised\PlanFromScopeBox\settings.json via System.Text.Json.
    /// Personal defaults only — nothing project-specific (that would use ExtensibleStorage).
    /// </summary>
    public sealed class PlanFromScopeBoxSettings
    {
        public int ScaleDenominator { get; set; } = 100;
        public double MarginMm { get; set; } = 10;
        public double GutterMm { get; set; } = 8;
        public double TbClearRightMm { get; set; } = 0;
        public double TbClearBottomMm { get; set; } = 0;

        public bool OpenSheetsAfter { get; set; } = true;

        public string SheetNumberPattern { get; set; } = "AP-{Increment}";
        public string SheetNamePattern { get; set; } = "{ScopeBoxName}";
        public string ViewNumberPattern { get; set; } = "{Increment}";
        public string ViewNamePattern { get; set; } = "{ScopeBoxName} - Plan";

        /// <summary>Last-used title block display string, matched back on load if still present.</summary>
        public string LastTitleBlockDisplay { get; set; } = string.Empty;
    }
}
