using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    /// <summary>What a file-system path turned out to be.</summary>
    public sealed class SourceFacts
    {
        public LinkSource Source = LinkSource.Unknown;
        /// <summary>For a mapped drive: the share the letter points at, e.g. \\kcg-fs01\Projects.</summary>
        public string ResolvedUnc = "";
    }

    /// <summary>
    /// Classifies a plain file-system path into a <see cref="LinkSource"/>.
    /// No Revit API, so it is safe to call from the parallel probe stage. Drive lookups are cached.
    /// Cloud and Revit Server paths are classified earlier, by RevitFileReader.
    /// </summary>
    public static class SourceDetector
    {
        /// <summary>Folder roots that mean "ACC content synced by Desktop Connector" (case-insensitive, editable in settings.json).</summary>
        public static List<string> DesktopConnectorRoots { get; set; } = new List<string>
        {
            @"%USERPROFILE%\ACCDocs",
            @"%USERPROFILE%\DC",
            @"%USERPROFILE%\Autodesk Docs",
            @"%LOCALAPPDATA%\Autodesk\Autodesk Desktop Connector",
        };

        /// <summary>Folder roots that mean "a cloud-sync folder". Add Dropbox / My Drive here if you want them flagged.</summary>
        public static List<string> CloudSyncRoots { get; set; } = new List<string>
        {
            @"%ONEDRIVE%",
            @"%USERPROFILE%\OneDrive",
            @"%USERPROFILE%\OneDrive - ",
        };

        private static readonly ConcurrentDictionary<string, SourceFacts> _driveCache =
            new ConcurrentDictionary<string, SourceFacts>(StringComparer.OrdinalIgnoreCase);

        private static List<string> _dcExpanded, _syncExpanded;
        private static readonly object _gate = new object();

        /// <summary>Call after changing the root lists (e.g. after loading settings).</summary>
        public static void ResetCache()
        {
            lock (_gate) { _dcExpanded = null; _syncExpanded = null; }
            _driveCache.Clear();
        }

        public static SourceFacts Detect(string fullPath)
        {
            var f = new SourceFacts();
            if (string.IsNullOrWhiteSpace(fullPath)) return f;

            string p = fullPath.Trim();

            if (p.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase)) { f.Source = LinkSource.RevitServer; return f; }
            if (p.StartsWith("BIM 360://", StringComparison.OrdinalIgnoreCase)) { f.Source = LinkSource.Bim360Legacy; return f; }
            if (p.StartsWith("Autodesk Docs://", StringComparison.OrdinalIgnoreCase)) { f.Source = LinkSource.AccThisProject; return f; }

            if (StartsWithAny(p, Expanded(ref _dcExpanded, DesktopConnectorRoots))) { f.Source = LinkSource.DesktopConnector; return f; }
            if (StartsWithAny(p, Expanded(ref _syncExpanded, CloudSyncRoots))) { f.Source = LinkSource.CloudSyncFolder; return f; }

            if (p.StartsWith(@"\\", StringComparison.Ordinal)) { f.Source = LinkSource.NetworkUnc; return f; }

            string root;
            try { root = Path.GetPathRoot(p); }
            catch { return f; }
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return f;

            return _driveCache.GetOrAdd(root, DetectDrive);
        }

        private static SourceFacts DetectDrive(string root)
        {
            var f = new SourceFacts { Source = LinkSource.LocalPc };
            try
            {
                var d = new DriveInfo(root);
                switch (d.DriveType)
                {
                    case DriveType.Network:
                        f.Source = LinkSource.MappedDrive;
                        f.ResolvedUnc = ResolveMappedDrive(root);
                        break;
                    case DriveType.Removable:
                        f.Source = LinkSource.RemovableDrive;
                        break;
                    case DriveType.Fixed:
                        f.Source = LinkSource.LocalPc;
                        break;
                    case DriveType.CDRom:
                        f.Source = LinkSource.RemovableDrive;
                        break;
                    default:
                        f.Source = LinkSource.LocalPc;
                        break;
                }
            }
            catch { /* disconnected or exotic drive — leave as Local PC */ }
            return f;
        }

        // ---------------------------------------------------------------- Win32
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        /// <summary>"Z:\" → "\\server\share", or "" if it can't be resolved.</summary>
        public static string ResolveMappedDrive(string root)
        {
            try
            {
                string letter = root.TrimEnd('\\', '/');      // "Z:"
                var sb = new StringBuilder(512);
                int len = sb.Capacity;
                return WNetGetConnection(letter, sb, ref len) == 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        // ---------------------------------------------------------------- helpers
        private static List<string> Expanded(ref List<string> cache, List<string> roots)
        {
            lock (_gate)
            {
                if (cache != null) return cache;
                var list = new List<string>();
                foreach (string r in roots ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(r)) continue;
                    string e;
                    try { e = Environment.ExpandEnvironmentVariables(r); } catch { e = r; }
                    if (e.IndexOf('%') >= 0) continue;         // variable not set on this PC
                    list.Add(e.TrimEnd('\\'));
                }
                cache = list;
                return cache;
            }
        }

        private static bool StartsWithAny(string path, List<string> roots) =>
            roots.Any(r => r.Length > 0 && path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }
}
