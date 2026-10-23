using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>ACC account, project and folder read from a link path.  [V007]</summary>
    public sealed class AccPathParts
    {
        /// <summary>ACC account (hub) name, or empty when the path does not name one.</summary>
        public string Account { get; init; } = "";

        /// <summary>ACC project name, or empty.</summary>
        public string Project { get; init; } = "";

        /// <summary>Folder inside the project, '\'-separated ("02_ARC\Models"), or "(project root)".</summary>
        public string Folder { get; init; } = "";

        /// <summary>File name, e.g. ARC_Tower.rvt.</summary>
        public string FileName { get; init; } = "";
    }

    /// <summary>
    /// Reads ACC names from the two path shapes a linked ACC model has:
    /// <list type="bullet">
    /// <item>Desktop Connector: <c>C:\Users\…\ACCDocs\&lt;Account&gt;\&lt;Project&gt;\Project Files\…\file.rvt</c></item>
    /// <item>Cloud link: <c>Autodesk Docs://&lt;Account&gt;/&lt;Project&gt;/…/file.rvt</c> (or <c>BIM 360://…</c>)</item>
    /// </list>
    /// Pure string work, never throws.  [V007]
    /// </summary>
    public static class AccPath
    {
        private static readonly string[] CloudSchemes = { "Autodesk Docs:", "BIM 360:" };
        private const string ProjectFiles = "Project Files";
        private const string AccDocs = "ACCDocs";

        /// <summary>True when <paramref name="path"/> is a cloud model path (<c>Autodesk Docs://</c> or <c>BIM 360://</c>).</summary>
        public static bool IsCloudPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            CloudSchemes.Any(s => path.TrimStart().StartsWith(s, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Splits a cloud path. When the path has a "Project Files" level, the names just before
        /// it are the project and the account. Otherwise the first name is the account and the
        /// second the project, unless <paramref name="isKnownProject"/> says the first name is
        /// already a project (paths saved without the account).
        /// </summary>
        public static AccPathParts ParseCloud(string path, Func<string, bool> isKnownProject = null)
        {
            if (!IsCloudPath(path)) return new AccPathParts();

            string rest = path.Trim();
            int colon = rest.IndexOf(':');
            rest = rest.Substring(colon + 1);
            string[] seg = Split(rest);
            if (seg.Length == 0) return new AccPathParts();

            string file = seg[^1];
            string[] dirs = seg.Take(seg.Length - 1).ToArray();
            string account = "", project = "";
            string[] folder;

            int pf = Array.FindIndex(dirs, d => d.Equals(ProjectFiles, StringComparison.OrdinalIgnoreCase));
            if (pf >= 0)
            {
                project = pf >= 1 ? dirs[pf - 1] : "";
                account = pf >= 2 ? dirs[pf - 2] : "";
                folder = dirs.Skip(pf + 1).ToArray();
            }
            else if (dirs.Length >= 2 && !(isKnownProject?.Invoke(dirs[0]) ?? false))
            {
                account = dirs[0];
                project = dirs[1];
                folder = dirs.Skip(2).ToArray();
            }
            else if (dirs.Length >= 1)
            {
                project = dirs[0];
                folder = dirs.Skip(1).ToArray();
            }
            else
            {
                folder = Array.Empty<string>();
            }

            return new AccPathParts
            {
                Account = account,
                Project = project,
                Folder = folder.Length == 0 ? "(project root)" : string.Join("\\", folder),
                FileName = file
            };
        }

        /// <summary>Account and project of a Desktop Connector path (the two folders after "ACCDocs"); empty parts otherwise.</summary>
        public static AccPathParts ParseDesktopConnector(string path)
        {
            if (string.IsNullOrEmpty(path)) return new AccPathParts();
            string[] seg = Split(path);
            int acc = Array.FindIndex(seg, s => s.Equals(AccDocs, StringComparison.OrdinalIgnoreCase));
            if (acc < 0) return new AccPathParts();
            return new AccPathParts
            {
                Account = acc + 1 < seg.Length - 1 ? seg[acc + 1] : "",
                Project = acc + 2 < seg.Length - 1 ? seg[acc + 2] : "",
                FileName = seg[^1]
            };
        }

        /// <summary>Compares two cloud GUIDs, ignoring braces, case and the "b." prefix ACC puts on project ids.</summary>
        public static bool SameId(string a, string b) =>
            Normalise(a).Length > 0 && string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);

        private static string Normalise(string id)
        {
            string s = (id ?? "").Trim().Trim('{', '}');
            if (s.StartsWith("b.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return s == Guid.Empty.ToString() ? "" : s;
        }

        private static string[] Split(string s) =>
            s.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(p => p.Trim())
             .Where(p => p.Length > 0)
             .ToArray();

        /// <summary>Returns the first value in <paramref name="info"/> whose key contains <paramref name="keyPart"/>, or empty.</summary>
        internal static string Find(IDictionary<string, string> info, string keyPart)
        {
            if (info == null) return "";
            foreach (var kv in info)
                if (kv.Key != null && kv.Key.IndexOf(keyPart, StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(kv.Value))
                    return kv.Value;
            return "";
        }
    }
}
