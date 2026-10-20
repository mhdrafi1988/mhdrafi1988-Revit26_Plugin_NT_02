using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    public sealed class LoadTimeEntry
    {
        public string Path { get; set; } = "";
        public string Host { get; set; } = "";
        public double Seconds { get; set; }
        public DateTime At { get; set; }
        public string Revit { get; set; } = "";
    }

    public sealed class LoadTimeData
    {
        public Dictionary<string, List<LoadTimeEntry>> Links { get; set; } = new Dictionary<string, List<LoadTimeEntry>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<LoadTimeEntry>> Hosts { get; set; } = new Dictionary<string, List<LoadTimeEntry>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Real measured load times, written by <see cref="LinkLoadTimeLogger"/> while you work
    /// and read by the checker window. Stored in %AppData%\Revit26_Plugin\LinkVersionChecker\loadtimes.json.
    /// </summary>
    public sealed class LoadTimeStore
    {
        private const int KeepPerFile = 10;
        private static readonly object Gate = new object();
        private static LoadTimeStore _instance;

        public static LoadTimeStore Instance
        {
            get { lock (Gate) return _instance ??= new LoadTimeStore(); }
        }

        public static string Folder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin", "LinkVersionChecker");

        private static string FilePath => System.IO.Path.Combine(Folder, "loadtimes.json");

        private LoadTimeData _data;

        private LoadTimeStore() { _data = Load(); }

        public void AddLink(LoadTimeEntry e) { lock (Gate) { Add(_data.Links, e); Save(); } }
        public void AddHost(LoadTimeEntry e) { lock (Gate) { Add(_data.Hosts, e); Save(); } }

        /// <summary>Most recent measurement for a link path (also tries the IFC cache name).</summary>
        public LoadTimeEntry LatestForLink(string path, string ifcPath = null)
        {
            lock (Gate)
            {
                foreach (var key in new[] { path, ifcPath, ifcPath != null ? ifcPath + ".RVT" : null, path + ".RVT" })
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (_data.Links.TryGetValue(Normalize(key), out var list) && list.Count > 0)
                        return list[list.Count - 1];
                }
                return null;
            }
        }

        public LoadTimeEntry LatestForHost(string hostPath)
        {
            lock (Gate)
            {
                if (string.IsNullOrEmpty(hostPath)) return null;
                return _data.Hosts.TryGetValue(Normalize(hostPath), out var list) && list.Count > 0 ? list[list.Count - 1] : null;
            }
        }

        public void Reload() { lock (Gate) _data = Load(); }

        private static void Add(Dictionary<string, List<LoadTimeEntry>> dict, LoadTimeEntry e)
        {
            string key = Normalize(e.Path);
            if (string.IsNullOrEmpty(key)) return;
            if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<LoadTimeEntry>();
            list.Add(e);
            if (list.Count > KeepPerFile) list.RemoveRange(0, list.Count - KeepPerFile);
        }

        private static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try { return System.IO.Path.GetFullPath(p).ToLowerInvariant(); } catch { return p.ToLowerInvariant(); }
        }

        private static LoadTimeData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<LoadTimeData>(File.ReadAllText(FilePath));
                    if (d != null)
                    {
                        // Rebuild with case-insensitive keys
                        return new LoadTimeData
                        {
                            Links = new Dictionary<string, List<LoadTimeEntry>>(d.Links ?? new Dictionary<string, List<LoadTimeEntry>>(), StringComparer.OrdinalIgnoreCase),
                            Hosts = new Dictionary<string, List<LoadTimeEntry>>(d.Hosts ?? new Dictionary<string, List<LoadTimeEntry>>(), StringComparer.OrdinalIgnoreCase)
                        };
                    }
                }
            }
            catch { /* corrupt file — start fresh */ }
            return new LoadTimeData();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* logging must never break Revit */ }
        }
    }

    /// <summary>
    /// Times how long each linked file (and each host) takes to open, using Revit's
    /// LinkedResourceOpening/Opened and DocumentOpening/Opened events.
    /// Call <see cref="Register"/> once from your IExternalApplication.OnStartup.
    /// A parent link's time includes its nested links.
    /// </summary>
    public static class LinkLoadTimeLogger
    {
        private static readonly Dictionary<string, Stopwatch> Running = new Dictionary<string, Stopwatch>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Stopwatch> HostRunning = new Dictionary<string, Stopwatch>(StringComparer.OrdinalIgnoreCase);
        private static string _currentHost = "";
        private static string _revitVersion = "";
        private static bool _registered;

        public static void Register(ControlledApplication app)
        {
            if (_registered) return;
            _revitVersion = app.VersionNumber;
            app.DocumentOpening += OnDocumentOpening;
            app.DocumentOpened += OnDocumentOpened;
            app.LinkedResourceOpening += OnLinkOpening;
            app.LinkedResourceOpened += OnLinkOpened;
            _registered = true;
        }

        public static void Unregister(ControlledApplication app)
        {
            if (!_registered) return;
            app.DocumentOpening -= OnDocumentOpening;
            app.DocumentOpened -= OnDocumentOpened;
            app.LinkedResourceOpening -= OnLinkOpening;
            app.LinkedResourceOpened -= OnLinkOpened;
            _registered = false;
        }

        private static void OnDocumentOpening(object sender, DocumentOpeningEventArgs e)
        {
            try
            {
                _currentHost = e.PathName ?? "";
                HostRunning[_currentHost] = Stopwatch.StartNew();
            }
            catch { /* never break opening */ }
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                string path = e.Document?.PathName ?? _currentHost;
                string key = HostRunning.ContainsKey(path) ? path : _currentHost;
                if (HostRunning.TryGetValue(key, out var sw))
                {
                    sw.Stop();
                    HostRunning.Remove(key);
                    if (e.Status == RevitAPIEventStatus.Succeeded)
                        LoadTimeStore.Instance.AddHost(new LoadTimeEntry { Path = path, Host = path, Seconds = sw.Elapsed.TotalSeconds, At = DateTime.Now, Revit = _revitVersion });
                }
                _currentHost = "";
            }
            catch { /* ignore */ }
        }

        private static void OnLinkOpening(object sender, LinkedResourceOpeningEventArgs e)
        {
            try { Running[e.LinkedResourcePathName ?? ""] = Stopwatch.StartNew(); }
            catch { /* ignore */ }
        }

        private static void OnLinkOpened(object sender, LinkedResourceOpenedEventArgs e)
        {
            try
            {
                string path = e.LinkedResourcePathName ?? "";
                if (!Running.TryGetValue(path, out var sw)) return;
                sw.Stop();
                Running.Remove(path);
                if (e.Status == RevitAPIEventStatus.Succeeded)
                    LoadTimeStore.Instance.AddLink(new LoadTimeEntry { Path = path, Host = _currentHost, Seconds = sw.Elapsed.TotalSeconds, At = DateTime.Now, Revit = _revitVersion });
            }
            catch { /* ignore */ }
        }
    }
}
