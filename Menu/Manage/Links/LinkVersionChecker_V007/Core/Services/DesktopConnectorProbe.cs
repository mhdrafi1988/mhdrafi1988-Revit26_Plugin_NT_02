using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>
    /// Reads Desktop Connector state from Windows.
    ///
    /// Desktop Connector (16+) is a Windows Cloud Files sync provider, so each file under its
    /// folder is a cloud placeholder. Windows keeps the placeholder state (in sync, partly on
    /// disk, pinned) that Explorer's status icons show; this class reads it without downloading
    /// the file. Every method is safe to call on any path and never throws.
    /// </summary>
    public static class DesktopConnectorProbe
    {
        private const string AccDocsSegment = @"\ACCDocs\";
        private const string ProjectFilesSegment = @"\Project Files\";

        // File attributes (winnt.h)
        private const uint AttrReadOnly = 0x1, AttrHidden = 0x2, AttrSystem = 0x4, AttrArchive = 0x20;
        private const uint AttrTemporary = 0x100, AttrOffline = 0x1000, AttrNotContentIndexed = 0x2000;
        private const uint AttrRecallOnOpen = 0x40000, AttrPinned = 0x80000, AttrUnpinned = 0x100000;
        private const uint AttrRecallOnDataAccess = 0x400000, AttrReparsePoint = 0x400;

        // Placeholder compatibility mode (ntifs.h): expose placeholders to this thread.
        private const sbyte PhcmExposePlaceholders = 2;

        // CF_PLACEHOLDER_STATE (cfapi.h)
        private const uint CfPlaceholder = 0x1, CfInSync = 0x8, CfPartial = 0x10, CfInvalid = 0xFFFFFFFF;

        /// <summary>
        /// Where Desktop Connector keeps its ACCDocs folder, in the order tried: Desktop Connector
        /// 16+ first, then older versions. Extra roots from the settings file are tried after these.  [7.1.0]
        /// </summary>
        public static readonly string[] DefaultRoots =
        {
            @"%USERPROFILE%\DC\ACCDocs",
            @"%USERPROFILE%\ACCDocs"
        };

        /// <summary>
        /// The Desktop Connector roots that exist on this PC: <see cref="DefaultRoots"/>, then
        /// <paramref name="extraRoots"/>, with environment variables expanded and duplicates removed.  [7.1.0]
        /// </summary>
        public static List<string> ExistingRoots(IEnumerable<string> extraRoots)
        {
            var roots = new List<string>();
            foreach (string r in DefaultRoots.Concat(extraRoots ?? Enumerable.Empty<string>()))
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                string e;
                try { e = Environment.ExpandEnvironmentVariables(r.Trim()).TrimEnd('\\', '/'); } catch { continue; }
                if (e.IndexOf('%') >= 0 || roots.Contains(e, StringComparer.OrdinalIgnoreCase)) continue;
                try { if (Directory.Exists(e)) roots.Add(e); } catch { /* unreadable root */ }
            }
            return roots;
        }

        /// <summary>
        /// The Desktop Connector copy of a cloud link on this PC, or empty when there is none.
        /// Desktop Connector mirrors <c>Autodesk Docs://&lt;Account&gt;/&lt;Project&gt;/…</c> as
        /// <c>&lt;root&gt;\&lt;Account&gt;\&lt;Project&gt;\…</c>; for a path saved without the account,
        /// each account folder under the root is tried too. Reads folder entries only, so an
        /// online-only copy is not downloaded. Never throws.  [7.1.0]
        /// </summary>
        public static string FindLocalCopy(string cloudPath, IReadOnlyList<string> roots)
        {
            string rel = AccPath.CloudRelativePath(cloudPath);
            if (rel.Length == 0 || roots == null) return "";
            foreach (string root in roots)
            {
                try
                {
                    string direct = Path.Combine(root, rel);
                    if (Exists(direct)) return direct;
                    foreach (string account in Directory.EnumerateDirectories(root))
                    {
                        string nested = Path.Combine(account, rel);
                        if (Exists(nested)) return nested;
                    }
                }
                catch { /* unreadable root: try the next one */ }
            }
            return "";
        }

        private static bool Exists(string path)
        {
            IntPtr h = FindFirstFileW(path, out _);
            if (h == InvalidHandle) return false;
            FindClose(h);
            return true;
        }

        /// <summary>True when <paramref name="path"/> is inside a Desktop Connector "ACCDocs" folder.</summary>
        public static bool IsDesktopConnectorPath(string path) =>
            !string.IsNullOrEmpty(path) && path.IndexOf(AccDocsSegment, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// The ACC project's "Project Files" folder that holds <paramref name="path"/>, or the
        /// ACCDocs folder when the path has no "Project Files" level. Empty for other paths.
        /// </summary>
        public static string ProjectRoot(string path)
        {
            if (!IsDesktopConnectorPath(path)) return "";
            int pf = path.IndexOf(ProjectFilesSegment, StringComparison.OrdinalIgnoreCase);
            if (pf >= 0) return path.Substring(0, pf + ProjectFilesSegment.Length - 1);
            int acc = path.IndexOf(AccDocsSegment, StringComparison.OrdinalIgnoreCase);
            return path.Substring(0, acc + AccDocsSegment.Length - 1);
        }

        /// <summary>Folder of <paramref name="path"/> relative to its <see cref="ProjectRoot"/>.</summary>
        public static string FolderInProject(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path) ?? "";
                string root = ProjectRoot(path);
                if (root.Length == 0 || !dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return dir;
                string rel = dir.Substring(root.Length).TrimStart('\\');
                return rel.Length == 0 ? "(project root)" : rel;
            }
            catch { return ""; }
        }

        /// <summary>How <see cref="GetState(string, out string)"/> read a file's state, for the hover text.  [7.1.1]</summary>
        public const string ViaCloudState = "Read from the Windows cloud-file state, the same state behind Explorer's status icon.";

        /// <summary>Hover text when the state came from the file's own flags because Windows gave no cloud-file state.  [7.1.1]</summary>
        public const string ViaFileFlags = "Read from the file's own flags (Always keep, online only): Windows did not give Revit the file's cloud-file state.";

        /// <summary>
        /// Desktop Connector state of one file. Reads directory metadata only, so an online-only
        /// file is not downloaded.
        /// </summary>
        public static DcFileState GetState(string path) => GetState(path, out _);

        /// <summary>
        /// Desktop Connector state of one file, and how it was read (<see cref="ViaCloudState"/> or
        /// <see cref="ViaFileFlags"/>; empty when the file is missing).
        ///
        /// By default Windows may disguise cloud placeholders from programs such as Revit, so the
        /// folder entry looks like a plain file. For the length of the read the calling thread is
        /// switched to see placeholders, then switched back. When Windows still reports no cloud
        /// state, the file's own pin / online-only flags decide; a file under ACCDocs with neither
        /// is downloaded but not pinned.  [7.1.1]
        /// </summary>
        public static DcFileState GetState(string path, out string via)
        {
            via = "";
            try
            {
                Win32FindData fd;
                sbyte previous = ExposePlaceholders(out bool exposed);
                try
                {
                    IntPtr h = FindFirstFileW(path, out fd);
                    if (h == InvalidHandle) return DcFileState.Missing;
                    FindClose(h);
                }
                finally
                {
                    RestorePlaceholders(previous, exposed);
                }

                uint attrs = fd.dwFileAttributes;
                uint cf = CfInvalid;
                try { cf = CfGetPlaceholderStateFromFindData(ref fd); }
                catch (DllNotFoundException) { /* Windows older than 1709: flags only */ }
                catch (EntryPointNotFoundException) { /* same */ }

                if (cf != CfInvalid && (cf & CfPlaceholder) != 0)
                {
                    via = ViaCloudState;
                    if ((cf & CfInSync) == 0) return DcFileState.SyncPending;

                    bool notOnDisk = (cf & CfPartial) != 0 ||
                                     (attrs & (AttrRecallOnDataAccess | AttrRecallOnOpen | AttrOffline)) != 0;
                    bool pinned = (attrs & AttrPinned) != 0;

                    if (notOnDisk) return pinned ? DcFileState.SyncPending : DcFileState.OnlineOnly; // pinned + not here = downloading
                    return pinned ? DcFileState.AlwaysKept : DcFileState.AvailableLocally;
                }

                // Placeholders were visible and this is a plain file with no cloud flags:
                // it really is not managed by Desktop Connector.
                if (exposed && cf != CfInvalid && (attrs & (AttrReparsePoint | CloudFlags)) == 0)
                {
                    via = ViaCloudState;
                    return DcFileState.NotCloudFile;
                }

                via = ViaFileFlags;
                return FromAttributesOnly(attrs);
            }
            catch
            {
                return DcFileState.Unknown;
            }
        }

        // Pinned, unpinned and online-only flags: set only on cloud files.
        private const uint CloudFlags = AttrPinned | AttrUnpinned | AttrRecallOnDataAccess | AttrRecallOnOpen | AttrOffline;

        /// <summary>State from the file's own flags. A file under ACCDocs with no flag is downloaded but not pinned.</summary>
        private static DcFileState FromAttributesOnly(uint attrs)
        {
            bool pinned = (attrs & AttrPinned) != 0;
            if ((attrs & (AttrRecallOnDataAccess | AttrRecallOnOpen | AttrOffline)) != 0)
                return pinned ? DcFileState.SyncPending : DcFileState.OnlineOnly;
            return pinned ? DcFileState.AlwaysKept : DcFileState.AvailableLocally;
        }

        /// <summary>Lets this thread see cloud placeholders as they are. Returns the previous mode; <paramref name="ok"/> is false when Windows does not offer the switch.</summary>
        private static sbyte ExposePlaceholders(out bool ok)
        {
            ok = false;
            try
            {
                sbyte previous = RtlSetThreadPlaceholderCompatibilityMode(PhcmExposePlaceholders);
                ok = previous >= 0;
                return previous;
            }
            catch (DllNotFoundException) { return -1; }
            catch (EntryPointNotFoundException) { return -1; }         // Windows older than 1803
        }

        private static void RestorePlaceholders(sbyte previous, bool exposed)
        {
            if (!exposed || previous == PhcmExposePlaceholders) return;
            try { RtlSetThreadPlaceholderCompatibilityMode(previous); } catch { /* best effort */ }
        }

        /// <summary>
        /// Marks the file "Always keep on this device" (what Explorer's menu item and
        /// <c>attrib +P</c> do). Desktop Connector then downloads it and keeps it current.
        /// Returns an error message, or null on success.
        /// </summary>
        public static string Pin(string path)
        {
            try
            {
                if (!File.Exists(path)) return "The file is not on this PC.";
                uint attrs = (uint)File.GetAttributes(path);
                // SetFileAttributes accepts only the user-settable bits plus pin / unpin.
                uint keep = attrs & (AttrReadOnly | AttrHidden | AttrSystem | AttrArchive | AttrTemporary | AttrNotContentIndexed);
                uint wanted = (keep | AttrPinned) & ~AttrUnpinned;
                if (!SetFileAttributesW(path, wanted))
                    return $"Windows refused the change (error {Marshal.GetLastWin32Error()}).";
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ---------------------------------------------------------------- Win32

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Win32FindData
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string lpFileName, out Win32FindData lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

        [DllImport("ntdll.dll")]
        private static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint CfGetPlaceholderStateFromFindData(ref Win32FindData findData);
    }
}
