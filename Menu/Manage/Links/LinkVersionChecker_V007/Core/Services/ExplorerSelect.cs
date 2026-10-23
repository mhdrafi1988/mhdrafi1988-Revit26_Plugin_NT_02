using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>
    /// Opens an Explorer window on a folder with several files highlighted
    /// (<c>explorer /select</c> can highlight only one). Falls back to <c>/select</c> on the first
    /// file when the shell call fails.
    /// </summary>
    internal static class ExplorerSelect
    {
        /// <summary>Opens <paramref name="folder"/> with <paramref name="files"/> (all inside it) selected.</summary>
        public static void Open(string folder, IList<string> files)
        {
            if (files == null || files.Count == 0) return;

            IntPtr folderPidl = IntPtr.Zero;
            var items = new List<IntPtr>();
            try
            {
                if (SHParseDisplayName(folder, IntPtr.Zero, out folderPidl, 0, out _) == 0 && folderPidl != IntPtr.Zero)
                {
                    foreach (string f in files)
                        if (SHParseDisplayName(f, IntPtr.Zero, out IntPtr p, 0, out _) == 0 && p != IntPtr.Zero) items.Add(p);

                    if (items.Count > 0 && SHOpenFolderAndSelectItems(folderPidl, (uint)items.Count, items.ToArray(), 0) == 0)
                        return;
                }
            }
            catch
            {
                // fall back below
            }
            finally
            {
                foreach (IntPtr p in items) ILFree(p);
                if (folderPidl != IntPtr.Zero) ILFree(folderPidl);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{files[0]}\"") { UseShellExecute = true });
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);
    }
}
