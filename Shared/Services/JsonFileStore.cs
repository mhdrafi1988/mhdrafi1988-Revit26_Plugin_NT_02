using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Reads and writes a tool's JSON data file under %AppData%\Revit26_Plugin_Revised\&lt;Tool&gt;.
    /// Unlike <see cref="SettingsService{T}"/>, a failed save throws so the tool can tell the user,
    /// and a corrupt file is kept aside instead of being overwritten.
    /// Several Revit sessions can share one file: use <see cref="Update{T}"/> to change it, so each
    /// save starts from what is on disk and never drops what another session saved.
    /// </summary>
    public static class JsonFileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // A file another program has open for a moment (antivirus, a sync client, another Revit
        // session saving) is tried again for about a second before the read or write gives up.
        private const int Attempts = 6;
        private const int RetryDelayMs = 200;

        // How long Update waits for another Revit session to finish changing the same file.
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Full path of <paramref name="fileName"/> in the tool's data folder; the folder is created.</summary>
        public static string PathFor(string toolFolder, string fileName)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", toolFolder);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        /// <summary>
        /// Loads <paramref name="path"/>. A missing file gives a new instance. A file whose content is
        /// not valid JSON is renamed to "*.corrupt-&lt;time&gt;.json" and a new instance is returned. A
        /// file that can't be read (locked, no access) is left as it is and a new instance is returned.
        /// In both cases <paramref name="warning"/> says what happened; otherwise it is null.
        /// Never throws for a bad or unreadable file.
        /// </summary>
        public static T Load<T>(string path, out string warning) where T : class, new()
        {
            try
            {
                return ReadOrKeepAside<T>(path, out warning);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                warning = $"Could not read {path} ({ex.Message}). Saved sets are not shown this time; the file was not changed.";
                return new T();
            }
        }

        /// <summary>
        /// Changes the file safely when several Revit sessions use it: waits for any other session
        /// changing it, reads the latest content from disk, runs <paramref name="change"/> on it and,
        /// when <paramref name="change"/> returns true, saves it. Returns the latest content (as saved).
        /// Throws <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> or
        /// <see cref="NotSupportedException"/> when the file can't be read or written; the file is then
        /// left as it was. A file whose content is not valid JSON is kept aside, as in <see cref="Load{T}"/>.
        /// </summary>
        public static T Update<T>(string path, Func<T, bool> change) where T : class, new()
        {
            if (change == null) throw new ArgumentNullException(nameof(change));

            using FileStream gate = AcquireLock(path);
            T data = ReadOrKeepAside<T>(path, out _);
            if (change(data)) Save(path, data);
            return data;
        }

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/> through a temporary file, so a
        /// failure never leaves a half-written file. Throws on failure. This replaces the whole file:
        /// prefer <see cref="Update{T}"/> for a file that other Revit sessions also write.
        /// </summary>
        public static void Save<T>(string path, T data)
        {
            string temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
                Retry(() => File.Move(temp, path, overwrite: true));
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (IsFileError(ex)) { /* only a stray temp file is left */ }
            }
        }

        // Reads the file (null when missing), retrying while it is busy. Content that is not valid
        // JSON is moved aside and a new instance returned, with a warning. Throws when the file
        // can't be read, so a caller never saves over a file it could not see.
        private static T ReadOrKeepAside<T>(string path, out string warning) where T : class, new()
        {
            warning = null;
            string json = Retry(() => File.Exists(path) ? File.ReadAllText(path) : null);
            if (json == null) return new T();

            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                string kept = Path.ChangeExtension(path, $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try { Retry(() => File.Move(path, kept)); }
                catch (Exception moveEx) when (IsFileError(moveEx))
                {
                    // Can't move it aside: stop rather than save over it.
                    throw new IOException($"{path} is not valid ({ex.Message}) and could not be kept aside: {moveEx.Message}", moveEx);
                }
                warning = $"Could not read the saved sets ({ex.Message}). The file was kept as {kept}; starting with no saved sets.";
                return new T();
            }
        }

        // A lock file next to the data file, held open with no sharing, so only one Revit session
        // (on this or another computer sharing the profile) reads, changes and writes at a time.
        // The empty .lock file stays in the folder; it is harmless.
        private static FileStream AcquireLock(string path)
        {
            string lockPath = path + ".lock";
            var waited = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex)
                {
                    if (waited.Elapsed >= LockTimeout)
                        throw new IOException($"Another Revit session is still saving to {path}. Try again in a moment.", ex);
                    Thread.Sleep(100);
                }
            }
        }

        private static void Retry(Action action) => Retry<object>(() => { action(); return null; });

        private static TResult Retry<TResult>(Func<TResult> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return action();
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < Attempts)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }

        private static bool IsFileError(Exception ex)
            => ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException;
    }
}
