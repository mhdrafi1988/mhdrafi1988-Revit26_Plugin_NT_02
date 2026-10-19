using System;
using System.Collections.Generic;
using System.IO;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    /// <summary>
    /// Copies a file into "_LinkCheckerBackup" beside it before any change.
    /// Each file is backed up once per window session, so batch actions don't pile up copies.
    /// </summary>
    public sealed class BackupService
    {
        public const string FolderName = "_LinkCheckerBackup";
        private readonly Dictionary<string, string> _done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Backups => _done;

        public string Backup(string path, string tag)
        {
            if (_done.TryGetValue(path, out var existing)) return existing;

            string dir = Path.Combine(Path.GetDirectoryName(path) ?? "", FolderName);
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            string target = Path.Combine(dir, $"{name}_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            File.Copy(path, target, true);
            _done[path] = target;
            return target;
        }
    }
}
