using Revit26_Plugin_Revised.ViewAutoRenamer.V006.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V006.Services;

public static class ViewAutoRenamerSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Revit26_Plugin_Revised", "ViewAutoRenamer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ViewAutoRenamerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ViewAutoRenamerSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ViewAutoRenamerSettings>(json, JsonOptions)
                   ?? new ViewAutoRenamerSettings();
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to defaults
            // rather than blocking the tool from opening.
            return new ViewAutoRenamerSettings();
        }
    }

    public static void Save(ViewAutoRenamerSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort — a failed settings save should never interrupt
            // the user's actual rename workflow.
        }
    }
}
