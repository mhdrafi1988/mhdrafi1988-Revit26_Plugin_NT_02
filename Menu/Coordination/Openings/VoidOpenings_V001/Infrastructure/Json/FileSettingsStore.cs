using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Json
{
    /// <summary>
    /// Keeps the clearance setup in <c>%AppData%\Revit26_Plugin_Revised\VoidOpenings\settings.json</c>.
    /// A save goes through a temporary file, so a failure never leaves half a file. A file that is
    /// not valid JSON is copied to <c>settings.corrupt-&lt;time&gt;.json</c> before anything can replace it.
    /// </summary>
    public sealed class FileSettingsStore : ISettingsStore
    {
        private const string FileName = "settings.json";
        private readonly string _folder;

        /// <summary>Creates a store on the tool's folder under %AppData%.</summary>
        public FileSettingsStore() : this(DefaultFolder())
        {
        }

        /// <summary>Creates a store on another folder (used by the unit tests).</summary>
        public FileSettingsStore(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A folder is required.", nameof(folder));
            _folder = folder;
        }

        /// <summary>The tool's settings folder under %AppData%.</summary>
        public static string DefaultFolder() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", "VoidOpenings");

        /// <inheritdoc/>
        public string Location => Path.Combine(_folder, FileName);

        /// <inheritdoc/>
        public SettingsLoadResult Load()
        {
            string path = Location;
            string json;
            try
            {
                if (!File.Exists(path))
                    return new SettingsLoadResult(ClearanceSetup.CreateDefault());
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return new SettingsLoadResult(ClearanceSetup.CreateDefault(),
                    $"Could not read the saved clearance rules ({ex.Message}). The defaults are shown; the file was not changed.");
            }

            try
            {
                ClearanceSetup setup = SettingsJson.Read(json, out string warning);
                return new SettingsLoadResult(setup, warning == null ? null : "Saved clearance rules: " + warning);
            }
            catch (JsonException ex)
            {
                string kept = KeepAside(path);
                return new SettingsLoadResult(ClearanceSetup.CreateDefault(),
                    $"The saved clearance rules could not be read ({ex.Message}). The defaults are shown."
                    + (kept == null ? string.Empty : $" The old file was copied to {kept}."));
            }
        }

        /// <inheritdoc/>
        public void Save(ClearanceSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            string path = Location;
            Directory.CreateDirectory(_folder);
            string temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, SettingsJson.Write(setup));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (IsFileError(ex)) { /* only a stray temp file is left */ }
            }
        }

        private static string KeepAside(string path)
        {
            try
            {
                string kept = Path.ChangeExtension(path, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(path, kept, overwrite: true);
                return kept;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return null;
            }
        }

        private static bool IsFileError(Exception ex)
            => ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException
            || ex is System.Security.SecurityException;
    }
}
