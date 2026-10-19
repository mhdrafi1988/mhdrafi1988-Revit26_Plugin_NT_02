using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models
{
    /// <summary>Where a linked file physically lives. One value per row of the Source column.</summary>
    public enum LinkSource
    {
        Unknown = 0,          // path blank or unresolvable
        AccThisProject,       // ACC / Autodesk Docs, same project as the host
        AccOtherProject,      // ACC / Autodesk Docs, different project (or hub)
        Bim360Legacy,         // legacy "BIM 360://" cloud path
        DesktopConnector,     // ACC content synced to disk by Desktop Connector
        CloudSyncFolder,      // OneDrive / SharePoint (and optionally Dropbox / Google Drive)
        LocalPc,              // fixed drive on this PC
        RemovableDrive,       // USB / external drive
        MappedDrive,          // drive letter mapped to a network share
        NetworkUnc,           // \\server\share\...
        RevitServer,          // RSN://server/...
        OtherResourceServer,  // a third-party external resource server
        InModelImport         // imported CAD / embedded image — no file at all
    }

    /// <summary>The four buckets the Source filter groups by.</summary>
    public enum SourceGroup { Acc, Disk, Server, NoFile }

    /// <summary>Display text, grouping and "how we know" for every <see cref="LinkSource"/>.</summary>
    public static class LinkSourceInfo
    {
        public const string All = "All";

        private sealed class Meta
        {
            public string Text;
            public SourceGroup Group;
            public string DetectedBy;
            public bool HasFile;
        }

        private static readonly Dictionary<LinkSource, Meta> Map = new Dictionary<LinkSource, Meta>
        {
            [LinkSource.AccThisProject]      = new Meta { Text = "ACC — this project",    Group = SourceGroup.Acc,    HasFile = false, DetectedBy = "ExternalResourceReference · ProjectId == host" },
            [LinkSource.AccOtherProject]     = new Meta { Text = "ACC — other project",   Group = SourceGroup.Acc,    HasFile = false, DetectedBy = "ExternalResourceReference · ProjectId differs" },
            [LinkSource.Bim360Legacy]        = new Meta { Text = "BIM 360 (legacy)",      Group = SourceGroup.Acc,    HasFile = false, DetectedBy = "cloud path starts with BIM 360://" },
            [LinkSource.DesktopConnector]    = new Meta { Text = "Desktop Connector",     Group = SourceGroup.Acc,    HasFile = true,  DetectedBy = "disk path under an ACCDocs / Desktop Connector root" },
            [LinkSource.CloudSyncFolder]     = new Meta { Text = "OneDrive / SharePoint", Group = SourceGroup.Disk,   HasFile = true,  DetectedBy = "disk path under a cloud-sync root (%OneDrive%)" },
            [LinkSource.LocalPc]             = new Meta { Text = "Local PC",              Group = SourceGroup.Disk,   HasFile = true,  DetectedBy = "DriveType == Fixed" },
            [LinkSource.RemovableDrive]      = new Meta { Text = "Removable drive",       Group = SourceGroup.Disk,   HasFile = true,  DetectedBy = "DriveType == Removable" },
            [LinkSource.MappedDrive]         = new Meta { Text = "Mapped drive",          Group = SourceGroup.Disk,   HasFile = true,  DetectedBy = "DriveType == Network · UNC via WNetGetConnection" },
            [LinkSource.NetworkUnc]          = new Meta { Text = "Network (UNC)",         Group = SourceGroup.Disk,   HasFile = true,  DetectedBy = @"path starts with \\" },
            [LinkSource.RevitServer]         = new Meta { Text = "Revit Server",          Group = SourceGroup.Server, HasFile = false, DetectedBy = "ModelPath.ServerPath == true (RSN://)" },
            [LinkSource.OtherResourceServer] = new Meta { Text = "Other resource server", Group = SourceGroup.Server, HasFile = false, DetectedBy = "external resource server that is not Autodesk's" },
            [LinkSource.InModelImport]       = new Meta { Text = "In model (imported)",   Group = SourceGroup.NoFile, HasFile = false, DetectedBy = "ImportInstance.IsLinked == false / embedded image" },
            [LinkSource.Unknown]             = new Meta { Text = "Unknown / unresolved",  Group = SourceGroup.NoFile, HasFile = false, DetectedBy = "path blank or GetAbsolutePath() failed" },
        };

        /// <summary>Group entries offered in the Source filter, in display order.</summary>
        public static readonly IReadOnlyList<string> GroupNames = new[]
        {
            "ACC (all)", "On disk (all)", "Server (all)", "No file (all)"
        };

        private static readonly Dictionary<string, SourceGroup> GroupByName = new Dictionary<string, SourceGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACC (all)"] = SourceGroup.Acc,
            ["On disk (all)"] = SourceGroup.Disk,
            ["Server (all)"] = SourceGroup.Server,
            ["No file (all)"] = SourceGroup.NoFile,
        };

        /// <summary>Every source value in display order (used when "Available only" is off).</summary>
        public static readonly IReadOnlyList<LinkSource> AllSources = new[]
        {
            LinkSource.AccThisProject, LinkSource.AccOtherProject, LinkSource.Bim360Legacy, LinkSource.DesktopConnector,
            LinkSource.CloudSyncFolder, LinkSource.LocalPc, LinkSource.RemovableDrive, LinkSource.MappedDrive, LinkSource.NetworkUnc,
            LinkSource.RevitServer, LinkSource.OtherResourceServer, LinkSource.InModelImport, LinkSource.Unknown
        };

        public static string Text(LinkSource s) => Map[s].Text;
        public static SourceGroup Group(LinkSource s) => Map[s].Group;
        public static string DetectedBy(LinkSource s) => Map[s].DetectedBy;

        /// <summary>True when there is a real file on disk we can probe, size and version-check.</summary>
        public static bool HasLocalFile(LinkSource s) => Map[s].HasFile;

        /// <summary>Desktop Connector is ACC content, so it counts as ACC as well as disk.</summary>
        public static bool IsAcc(LinkSource s) => Map[s].Group == SourceGroup.Acc;

        /// <summary>True for the pure cloud sources — no local file, path is a cloud path.</summary>
        public static bool IsCloudPath(LinkSource s) =>
            s == LinkSource.AccThisProject || s == LinkSource.AccOtherProject || s == LinkSource.Bim360Legacy;

        public static string Colour(LinkSource s)
        {
            switch (Group(s))
            {
                case SourceGroup.Acc: return s == LinkSource.AccOtherProject || s == LinkSource.DesktopConnector ? "#B26A00" : "#0B6BCB";
                case SourceGroup.Server: return "#6A3FA0";
                case SourceGroup.NoFile: return "#C62828";
                default: return s == LinkSource.RemovableDrive || s == LinkSource.CloudSyncFolder ? "#B26A00" : "#1F2937";
            }
        }

        /// <summary>Does a link of this source pass the chosen filter entry (a group name, a value, or "All")?</summary>
        public static bool Matches(LinkSource s, string filterEntry)
        {
            string f = Strip(filterEntry);
            if (string.IsNullOrWhiteSpace(f) || f.Equals(All, StringComparison.OrdinalIgnoreCase)) return true;
            if (GroupByName.TryGetValue(f, out var g)) return Group(s) == g;
            return string.Equals(Text(s), f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Network (UNC) — 3" → "Network (UNC)". Filter entries carry their count.</summary>
        public static string Strip(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return All;
            int i = entry.IndexOf(" — ", StringComparison.Ordinal);
            return (i < 0 ? entry : entry.Substring(0, i)).Trim();
        }

        public static string WithCount(string entry, int count) => $"{entry} — {count}";

        /// <summary>
        /// Builds the Source filter list. When <paramref name="availableOnly"/> is true only the
        /// values present in <paramref name="present"/> are listed; otherwise all 13 are, zeros included.
        /// </summary>
        public static List<string> BuildOptions(IEnumerable<LinkSource> present, bool availableOnly)
        {
            var counts = new Dictionary<LinkSource, int>();
            int total = 0;
            foreach (var s in present ?? Enumerable.Empty<LinkSource>())
            {
                counts[s] = counts.TryGetValue(s, out int n) ? n + 1 : 1;
                total++;
            }

            var list = new List<string> { WithCount(All, total) };

            foreach (string gName in GroupNames)
            {
                var g = GroupByName[gName];
                int n = counts.Where(kv => Group(kv.Key) == g).Sum(kv => kv.Value);
                if (availableOnly && n == 0) continue;
                list.Add(WithCount(gName, n));
            }

            foreach (var s in AllSources)
            {
                int n = counts.TryGetValue(s, out int c) ? c : 0;
                if (availableOnly && n == 0) continue;
                list.Add(WithCount(Text(s), n));
            }
            return list;
        }
    }
}
