using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Reads and writes a tool's JSON data file under %AppData%\Revit26_Plugin_Revised\&lt;Tool&gt;.
    /// Unlike <see cref="SettingsService{T}"/>, a failed save throws so the tool can tell the user,
    /// and a corrupt file is kept aside instead of being overwritten.
    /// </summary>
    public static class JsonFileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Full path of <paramref name="fileName"/> in the tool's data folder; the folder is created.</summary>
        public static string PathFor(string toolFolder, string fileName)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", toolFolder);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        /// <summary>
        /// Loads <paramref name="path"/>. A missing file gives a new instance. A file that can't be
        /// read is renamed to "*.corrupt-&lt;time&gt;.json", a new instance is returned and
        /// <paramref name="warning"/> says what happened; otherwise <paramref name="warning"/> is null.
        /// </summary>
        public static T Load<T>(string path, out string warning) where T : class, new()
        {
            warning = null;
            if (!File.Exists(path)) return new T();

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException)
            {
                string kept = Path.ChangeExtension(path, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try { File.Move(path, kept); }
                catch (Exception moveEx) when (moveEx is IOException || moveEx is UnauthorizedAccessException)
                {
                    kept = null;
                }
                warning = kept == null
                    ? $"Could not read {path} ({ex.Message}). Starting with no saved sets."
                    : $"Could not read the saved sets ({ex.Message}). The file was kept as {kept}; starting with no saved sets.";
                return new T();
            }
        }

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/> through a temporary file, so a
        /// failure never leaves a half-written file. Throws on failure.
        /// </summary>
        public static void Save<T>(string path, T data)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
    }
}
