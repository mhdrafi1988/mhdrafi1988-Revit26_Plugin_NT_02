using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    public sealed class RvtHeader
    {
        public bool Ok;
        public string Year = "";
        public bool IsCurrent, IsLater, IsWorkshared, IsCentral, IsLocal;
        public string Error = "";
    }

    /// <summary>One entry of a file's link table (TransmissionData), independent of any host.</summary>
    public sealed class RefData
    {
        public ElementId Id;
        public string RefType = "";
        public string Stored = "";
        public string Full = "";
        public string PathType = "";
        public string Status = "";
        public bool Cloud;

        // ---- V004: where it lives ----
        public LinkSource Source = LinkSource.Unknown;
        public string ResolvedUnc = "";
        public string InSessionPath = "";
        public string ProjectId = "";
        public string ModelId = "";
        public string Region = "";
    }

    /// <summary>
    /// Revit API reads that don't open a model: BasicFileInfo (version header) and
    /// TransmissionData (link table). Call on Revit's main thread only. Results are cached
    /// so a folder audit reads each shared link once.
    /// </summary>
    public sealed class RevitFileReader
    {
        private readonly Dictionary<string, RvtHeader> _headers = new Dictionary<string, RvtHeader>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RefData>> _tables = new Dictionary<string, List<RefData>>(StringComparer.OrdinalIgnoreCase);

        public void ClearCache() { _headers.Clear(); _tables.Clear(); }

        public RvtHeader ReadHeader(string path)
        {
            if (_headers.TryGetValue(path, out var cached)) return cached;
            var h = new RvtHeader();
            try
            {
                BasicFileInfo bfi = BasicFileInfo.Extract(path);
                var m = Regex.Match(bfi.Format ?? "", @"\d{4}");
                h.Year = m.Success ? m.Value : (bfi.Format ?? "?");
                h.IsCurrent = bfi.IsSavedInCurrentVersion;
                h.IsLater = bfi.IsSavedInLaterVersion;
                h.IsWorkshared = bfi.IsWorkshared;
                h.IsCentral = bfi.IsCentral;
                h.IsLocal = bfi.IsLocal;
                h.Ok = true;
            }
            catch (Exception ex) { h.Error = ex.Message; }
            _headers[path] = h;
            return h;
        }

        /// <summary>Link table of a host or link file. Returns an empty list when it has no links.</summary>
        public List<RefData> ReadLinkTable(string filePath, string hostProjectId = "")
        {
            if (_tables.TryGetValue(filePath, out var cached)) return cached;

            var list = new List<RefData>();
            TransmissionData td = TransmissionData.ReadTransmissionData(ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath));
            if (td != null)
            {
                string baseDir = Path.GetDirectoryName(filePath);
                foreach (ElementId id in td.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference efr;
                    try { efr = td.GetLastSavedReferenceData(id); } catch { continue; }
                    if (efr == null || IsIgnoredType(efr)) continue;
                    var rd = FromReference(efr, baseDir, hostProjectId);
                    rd.Id = id;
                    list.Add(rd);
                }
            }
            _tables[filePath] = list;
            return list;
        }

        public static bool IsIgnoredType(ExternalFileReference efr)
        {
            string t = efr.ExternalFileReferenceType.ToString();
            return t == "KeynoteTable" || t == "AssemblyCodeTable" || t == "Decal";
        }

        // =====================================================================
        // File-based references (ExternalFileReference)
        // =====================================================================
        public static RefData FromReference(ExternalFileReference efr, string baseDir, string hostProjectId = "")
        {
            var rd = new RefData { RefType = efr.ExternalFileReferenceType.ToString() };
            ModelPath mp = efr.GetPath();
            rd.Stored = UserPath(mp);
            try { rd.Status = efr.GetLinkedFileStatus().ToString(); } catch { rd.Status = "Unknown"; }

            bool serverPath = false;
            try { serverPath = mp != null && mp.ServerPath; } catch { /* ignore */ }
            try { rd.Cloud = mp != null && mp.CloudPath; } catch { /* ignore */ }

            // ---- cloud (ACC / BIM 360) ------------------------------------
            if (rd.Cloud || rd.Stored.StartsWith("Autodesk Docs", StringComparison.OrdinalIgnoreCase)
                         || rd.Stored.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase))
            {
                rd.Cloud = true;
                rd.Full = rd.Stored;
                rd.InSessionPath = rd.Stored;
                rd.PathType = "Cloud";
                ReadCloudIds(mp, rd);
                rd.Source = ClassifyCloud(rd, hostProjectId);
                return rd;
            }

            // ---- Revit Server ---------------------------------------------
            if (serverPath || rd.Stored.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase))
            {
                rd.Full = rd.Stored;
                rd.InSessionPath = rd.Stored;
                rd.PathType = "Server";
                rd.Source = LinkSource.RevitServer;
                return rd;
            }

            // ---- plain file -------------------------------------------------
            string full = "";
            try { full = UserPath(efr.GetAbsolutePath()); } catch { /* fall back */ }
            if (string.IsNullOrEmpty(full)) full = rd.Stored;
            if (!Path.IsPathRooted(full) && !string.IsNullOrEmpty(baseDir))
            {
                try { full = Path.GetFullPath(Path.Combine(baseDir, rd.Stored)); } catch { /* keep */ }
            }
            rd.Full = full;
            try { rd.PathType = efr.PathType.ToString(); } catch { rd.PathType = ""; }

            var facts = SourceDetector.Detect(rd.Full);
            rd.Source = facts.Source;
            rd.ResolvedUnc = facts.ResolvedUnc;
            return rd;
        }

        // =====================================================================
        // Cloud references (ExternalResourceReference) — what V003 missed
        // =====================================================================
        /// <summary>
        /// Builds a RefData from an external RESOURCE reference. Cloud links (ACC / Autodesk Docs,
        /// BIM 360) are not ExternalFileReferences: <c>Element.IsExternalFileReference()</c> returns
        /// false for them and they never appear in <c>ExternalFileUtils.GetAllExternalFileReferences</c>.
        /// </summary>
        public static RefData FromResourceReference(ExternalResourceReference err, string refType, string hostProjectId)
        {
            var rd = new RefData { RefType = refType, Cloud = true, PathType = "Cloud" };

            try { rd.InSessionPath = err.InSessionPath ?? ""; } catch { /* ignore */ }
            rd.Stored = rd.InSessionPath;
            rd.Full = rd.InSessionPath;

            IDictionary<string, string> info = null;
            try { info = err.GetReferenceInformation(); } catch { /* ignore */ }
            if (info != null)
            {
                rd.ProjectId = Find(info, "project");
                rd.ModelId = Find(info, "model");
                rd.Region = Find(info, "region");
            }

            bool autodesk = rd.InSessionPath.StartsWith("Autodesk Docs", StringComparison.OrdinalIgnoreCase)
                         || rd.InSessionPath.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase)
                         || rd.InSessionPath.StartsWith("ACC", StringComparison.OrdinalIgnoreCase)
                         || !string.IsNullOrEmpty(rd.ProjectId);

            rd.Source = autodesk ? ClassifyCloud(rd, hostProjectId) : LinkSource.OtherResourceServer;
            if (!autodesk) { rd.Cloud = false; rd.PathType = "Server"; }
            return rd;
        }

        /// <summary>ACC this project vs another project, or a legacy BIM 360 path.</summary>
        private static LinkSource ClassifyCloud(RefData rd, string hostProjectId)
        {
            if (rd.InSessionPath.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase) ||
                rd.Stored.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase))
                return LinkSource.Bim360Legacy;

            if (string.IsNullOrEmpty(hostProjectId) || string.IsNullOrEmpty(rd.ProjectId))
                return LinkSource.AccThisProject;        // can't compare — assume the host's own project

            return string.Equals(Normalise(rd.ProjectId), Normalise(hostProjectId), StringComparison.OrdinalIgnoreCase)
                ? LinkSource.AccThisProject
                : LinkSource.AccOtherProject;
        }

        private static void ReadCloudIds(ModelPath mp, RefData rd)
        {
            if (mp == null) return;
            try { rd.ProjectId = mp.GetProjectGUID().ToString(); } catch { /* ignore */ }
            try { rd.ModelId = mp.GetModelGUID().ToString(); } catch { /* ignore */ }
            try { rd.Region = mp.Region ?? ""; } catch { /* ignore */ }
        }

        /// <summary>The cloud project id of an open model, used to tell "this project" from "another project".</summary>
        public static string HostProjectId(Document doc)
        {
            try
            {
                if (doc == null || !doc.IsModelInCloud) return "";
                ModelPath mp = doc.GetCloudModelPath();
                return mp == null ? "" : mp.GetProjectGUID().ToString();
            }
            catch { return ""; }
        }

        private static string Find(IDictionary<string, string> info, string keyPart)
        {
            foreach (var kv in info)
                if (kv.Key != null && kv.Key.IndexOf(keyPart, StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(kv.Value))
                    return kv.Value;
            return "";
        }

        private static string Normalise(string id) => (id ?? "").Trim().Trim('{', '}').Replace("b.", "");

        private static string UserPath(ModelPath mp)
        {
            try { return mp == null ? "" : ModelPathUtils.ConvertModelPathToUserVisiblePath(mp); }
            catch { return ""; }
        }
    }
}
