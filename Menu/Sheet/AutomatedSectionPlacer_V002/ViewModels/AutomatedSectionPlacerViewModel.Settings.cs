using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.AutomatedSectionPlacer.V002.Models;

namespace Revit26_Plugin_Revised.AutomatedSectionPlacer.V002.ViewModels
{
    /// <summary>Settings persistence: %AppData%\Revit26_Plugin_Revised\AutomatedSectionPlacer\settings.json,
    /// via System.Text.Json. Loaded once in the ctor; saved from NextToStage1→2
    /// transition and from Window.Closing (V213 — new, see OnWindowClosing).</summary>
    public partial class AutomatedSectionPlacerViewModel
    {
        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Revit26_Plugin_Revised", ToolName, "settings.json");

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<AutomatedSectionPlacerSettings>(json) ?? new();
                    MarginTopMm = _settings.LastMarginTopMm;
                    MarginBottomMm = _settings.LastMarginBottomMm;
                    MarginLeftMm = _settings.LastMarginLeftMm;
                    MarginRightMm = _settings.LastMarginRightMm;
                    GlobalHorizontalGapMm = _settings.LastHorizontalGapMm;
                    GlobalVerticalGapMm = _settings.LastVerticalGapMm;

                    if (Enum.TryParse<Models.ReadingDirection>(_settings.LastReadingDirection, out var rd))
                        ReadingDirection = rd;
                    if (Enum.TryParse<Models.RowTiebreak>(_settings.LastRowTiebreak, out var tb))
                        RowTiebreak = tb;
                    if (Enum.TryParse<Models.RowFillStrategy>(_settings.LastFillStrategy, out var fs))
                        DefaultFillStrategy = fs;
                    YToleranceMm = _settings.LastYToleranceMm;
                    IsActivityLogExpanded = _settings.IsActivityLogExpanded;
                }
            }
            catch
            {
                _settings = new AutomatedSectionPlacerSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.LastTitleblockName = SelectedTitleblock?.Name;
                _settings.LastMarginTopMm = MarginTopMm;
                _settings.LastMarginBottomMm = MarginBottomMm;
                _settings.LastMarginLeftMm = MarginLeftMm;
                _settings.LastMarginRightMm = MarginRightMm;
                _settings.LastHorizontalGapMm = GlobalHorizontalGapMm;
                _settings.LastVerticalGapMm = GlobalVerticalGapMm;

                _settings.LastReadingDirection = ReadingDirection.ToString();
                _settings.LastRowTiebreak = RowTiebreak.ToString();
                _settings.LastFillStrategy = DefaultFillStrategy.ToString();
                _settings.LastYToleranceMm = YToleranceMm;
                _settings.IsActivityLogExpanded = IsActivityLogExpanded;

                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry(LogLevel.Warning, $"Could not save settings: {ex.Message}"));
            }
        }
    }
}
