using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.LinkMemory.V001.Core.Models;
using Revit26_Plugin_Revised.LinkMemory.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace Revit26_Plugin_Revised.LinkMemory.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Link Memory window: saves which Revit links are loaded as named sets and
    /// loads / unloads links to match a set. The window is modal and opened from the command, so
    /// the Revit API is called directly from its commands.
    /// </summary>
    public partial class LinkMemoryViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly ModelIdentity _model;
        private readonly LinkMemoryFile _file;
        private List<LiveLink> _live = new();

        /// <summary>Saved sets of this model, newest first.</summary>
        public ObservableCollection<LinkSetRow> Sets { get; } = new();

        /// <summary>Comparison of the selected set with the model now.</summary>
        public ObservableCollection<LinkRow> Rows { get; } = new();

        /// <summary>Run log, newest first.</summary>
        public ObservableCollection<LogEntry> Log { get; } = new();

        /// <summary>Set being compared and applied.</summary>
        [ObservableProperty] private LinkSetRow selectedSet;

        /// <summary>Name for a new set.</summary>
        [ObservableProperty] private string newSetName;

        /// <summary>Unload links the set had unloaded (default on).</summary>
        [ObservableProperty] private bool unloadUnloaded = true;

        /// <summary>Reload links that are already loaded (default off).</summary>
        [ObservableProperty] private bool reloadLoaded;

        /// <summary>Header tile: top-level Revit links.</summary>
        [ObservableProperty] private int linkCount;

        /// <summary>Header tile: links loaded now.</summary>
        [ObservableProperty] private int loadedNowCount;

        /// <summary>Header tile: saved sets.</summary>
        [ObservableProperty] private int savedSetCount;

        /// <summary>Status strip text: what a reload would change.</summary>
        [ObservableProperty] private string summaryText = string.Empty;

        /// <summary>True while links are being loaded.</summary>
        [ObservableProperty] private bool isRunning;

        /// <summary>Log panel expanded.</summary>
        [ObservableProperty] private bool isLogExpanded = true;

        /// <summary>Creates the view model for the active project <paramref name="doc"/>.</summary>
        public LinkMemoryViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _model = ModelIdentity.FromDocument(doc);
            _file = LinkMemoryService.Load(out string warning);
            if (warning != null) AddLog(LogLevel.Warning, warning);

            NewSetName = DefaultSetName();
            RefreshLive();
            ReloadSets(null);
        }

        /// <summary>Model name and location for the "Saved sets" title.</summary>
        public string ModelLocation => _model == null ? "" : $"for {_model.Name} · {_model.Location}";

        partial void OnSelectedSetChanged(LinkSetRow value)
        {
            Rebuild();
            OverwriteCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            ReloadCommand.NotifyCanExecuteChanged();
        }

        partial void OnUnloadUnloadedChanged(bool value) => Rebuild();

        partial void OnReloadLoadedChanged(bool value) => Rebuild();

        partial void OnIsRunningChanged(bool value) => ReloadCommand.NotifyCanExecuteChanged();

        /// <summary>Saves which links are loaded now as a new set (or replaces a set of the same name).</summary>
        [RelayCommand]
        private void Remember()
        {
            if (_model == null)
            {
                MessageBox.Show("Save the model once before its links can be remembered.",
                    ToolCatalog.LinkMemory.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string name = (NewSetName ?? string.Empty).Trim();
            if (name.Length == 0) name = DefaultSetName();

            LinkModelEntry entry = EnsureEntry();
            LinkSet existing = entry.Sets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null && Ask($"A set called '{name}' already exists. Replace it with the links loaded now?") != MessageBoxResult.Yes)
                return;

            LinkSet set = LinkMemoryService.Capture(_doc, name);
            if (existing != null) entry.Sets.Remove(existing);
            entry.Sets.Insert(0, set);
            entry.Model = _model;

            if (Persist())
            {
                AddLog(LogLevel.Success, $"Remembered '{name}': {set.LoadedCount} of {set.Links.Count} links loaded.");
                NewSetName = DefaultSetName();
            }
            ReloadSets(set);
        }

        /// <summary>Replaces the selected set with the links loaded now.</summary>
        [RelayCommand(CanExecute = nameof(HasSet))]
        private void Overwrite()
        {
            LinkSet old = SelectedSet.Set;
            if (Ask($"Replace '{old.Name}' with the links loaded now?") != MessageBoxResult.Yes) return;

            LinkModelEntry entry = EnsureEntry();
            LinkSet set = LinkMemoryService.Capture(_doc, old.Name);
            int index = Math.Max(0, entry.Sets.IndexOf(old));
            entry.Sets.Remove(old);
            entry.Sets.Insert(index, set);

            if (Persist()) AddLog(LogLevel.Success, $"Updated '{set.Name}'.");
            ReloadSets(set);
        }

        /// <summary>Deletes the selected set.</summary>
        [RelayCommand(CanExecute = nameof(HasSet))]
        private void Delete()
        {
            LinkSet set = SelectedSet.Set;
            if (Ask($"Delete the set '{set.Name}'?") != MessageBoxResult.Yes) return;

            LinkModelEntry entry = _file.Find(_model?.Key);
            entry?.Sets.Remove(set);
            if (entry != null && entry.Sets.Count == 0) _file.Models.Remove(entry);

            if (Persist()) AddLog(LogLevel.Info, $"Deleted '{set.Name}'.");
            ReloadSets(null);
        }

        private bool HasSet() => SelectedSet != null;

        /// <summary>Loads and unloads links to match the selected set.</summary>
        [RelayCommand(CanExecute = nameof(CanReload))]
        private void Reload()
        {
            if (!_doc.IsValidObject)
            {
                AddLog(LogLevel.Error, "The model was closed. Close this window.");
                return;
            }
            if (_doc.IsReadOnly)
            {
                AddLog(LogLevel.Error, "The model is read-only, so links can't be loaded or unloaded.");
                return;
            }

            var changes = Rows.Where(r => r.Change is LinkChange.Loads or LinkChange.Unloads or LinkChange.Reloads).ToList();
            if (changes.Count == 0)
            {
                AddLog(LogLevel.Info, "Nothing to change: the links already match the set.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Apply '{SelectedSet.Name}'?\n");
            foreach (var r in changes.Take(15)) sb.AppendLine($"  • {r.ChangeText}: {r.Name}");
            if (changes.Count > 15) sb.AppendLine($"  … and {changes.Count - 15} more");
            sb.AppendLine("\nLoading links can take a while, and Revit clears the Undo list when a link loads.");
            if (Ask(sb.ToString()) != MessageBoxResult.Yes) return;

            var options = new LinkReloadOptions { UnloadUnloaded = UnloadUnloaded, ReloadLoaded = ReloadLoaded };
            IsRunning = true;
            try
            {
                AddLog(LogLevel.Info, $"Applying '{SelectedSet.Name}'…");
                LinkReloadResult result = LinkMemoryService.Apply(_doc, SelectedSet.Set, options, AddLog);
                AddLog(result.Skipped > 0 ? LogLevel.Warning : LogLevel.Success, "— " + result);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
            {
                AddLog(LogLevel.Error, $"Stopped: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                RefreshLive();
                Rebuild();
            }
        }

        private bool CanReload() => !IsRunning && SelectedSet != null;

        /// <summary>Copies the log to the clipboard.</summary>
        [RelayCommand]
        private void CopyLog()
        {
            if (Log.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in Log.Reverse()) sb.AppendLine(e.ToString());
            try { Clipboard.SetText(sb.ToString()); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private void RefreshLive()
        {
            _live = _doc.IsValidObject ? LinkMemoryService.ReadLinks(_doc) : new List<LiveLink>();
            LinkCount = _live.Count;
            LoadedNowCount = _live.Count(l => l.IsLoaded);
        }

        private LinkModelEntry EnsureEntry()
        {
            LinkModelEntry entry = _file.Find(_model.Key);
            if (entry != null) return entry;
            entry = new LinkModelEntry { Model = _model };
            _file.Models.Add(entry);
            return entry;
        }

        private void ReloadSets(LinkSet select)
        {
            Sets.Clear();
            foreach (var s in _file.Find(_model?.Key)?.Sets ?? new List<LinkSet>())
                Sets.Add(new LinkSetRow(s));
            SavedSetCount = Sets.Count;
            SelectedSet = Sets.FirstOrDefault(r => r.Set == select) ?? Sets.FirstOrDefault();
            Rebuild();
        }

        // Builds the comparison grid and summary for the selected set.
        private void Rebuild()
        {
            Rows.Clear();
            LinkSet set = SelectedSet?.Set;
            bool workshared = _doc.IsValidObject && _doc.IsWorkshared;
            bool isLocal = _doc.IsValidObject && LinkMemoryService.IsLocalCopy(_doc);

            if (set == null)
            {
                foreach (var l in _live)
                    Rows.Add(new LinkRow { Name = l.Name, Path = l.Path, Now = StatusText(l.Status), InSet = "—" });
                SummaryText = "No set saved yet — click Remember current.";
                return;
            }

            var matched = new HashSet<LiveLink>();
            int loads = 0, unloads = 0, reloads = 0, skipped = 0;

            foreach (var saved in set.Links)
            {
                LiveLink live = LinkMemoryService.Match(_live, saved);
                if (live != null) matched.Add(live);

                string note = null;
                LinkChange change = LinkChange.None;
                if (live == null)
                {
                    note = "not in model";
                    if (saved.IsLoaded) change = LinkChange.Skipped;
                }
                else if (saved.IsLoaded && !live.IsLoaded)
                {
                    change = LinkChange.Loads;
                    if (live.Status == LinkedFileStatus.InClosedWorkset)
                    {
                        change = LinkChange.Skipped;
                        note = "workset closed";
                    }
                    else if (!(isLocal && live.Type.LocallyUnloaded) && OwnerOf(live, workshared) is string owner)
                    {
                        change = LinkChange.Skipped;
                        note = $"checked out by {owner}";
                    }
                }
                else if (!saved.IsLoaded && live.IsLoaded && UnloadUnloaded)
                {
                    change = LinkChange.Unloads;
                    if (!isLocal && OwnerOf(live, workshared) is string owner)
                    {
                        change = LinkChange.Skipped;
                        note = $"checked out by {owner}";
                    }
                }
                else if (saved.IsLoaded && live.IsLoaded && ReloadLoaded)
                {
                    change = LinkChange.Reloads;
                }

                if (change == LinkChange.Loads) loads++;
                if (change == LinkChange.Unloads) unloads++;
                if (change == LinkChange.Reloads) reloads++;
                if (change == LinkChange.Skipped) skipped++;

                Rows.Add(new LinkRow
                {
                    Name = live?.Name ?? saved.Name,
                    Note = note,
                    Path = live?.Path ?? saved.Path,
                    Now = live == null ? "—" : StatusText(live.Status),
                    InSet = saved.IsLoaded ? "Loaded" : "Unloaded",
                    Change = change
                });
            }

            // Links added after the set was saved are left as they are.
            foreach (var live in _live.Where(l => !matched.Contains(l)))
                Rows.Add(new LinkRow { Name = live.Name, Path = live.Path, Now = StatusText(live.Status), InSet = "Not in set" });

            var ordered = Rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Rows.Clear();
            foreach (var r in ordered) Rows.Add(r);

            var parts = new List<string> { $"{loads} load", $"{unloads} unload" };
            if (ReloadLoaded) parts.Add($"{reloads} reload");
            parts.Add($"{skipped} skipped");
            SummaryText = string.Join(" · ", parts);
        }

        private string OwnerOf(LiveLink live, bool workshared)
        {
            if (!workshared) return null;
            try
            {
                return WorksharingUtils.GetCheckoutStatus(_doc, live.Type.Id, out string owner) == CheckoutStatus.OwnedByOtherUser
                    ? owner
                    : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private static string StatusText(LinkedFileStatus status) => status switch
        {
            LinkedFileStatus.Loaded => "Loaded",
            LinkedFileStatus.NotFound => "Not found",
            LinkedFileStatus.InClosedWorkset => "Closed workset",
            LinkedFileStatus.LocallyUnloaded => "Unloaded (me)",
            LinkedFileStatus.Invalid => "Invalid",
            _ => "Unloaded"
        };

        private static string DefaultSetName() => $"Set {DateTime.Now:dd MMM HH:mm}";

        private bool Persist()
        {
            try
            {
                LinkMemoryService.Save(_file);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException)
            {
                AddLog(LogLevel.Error, $"Could not save to {LinkMemoryService.FilePath}: {ex.Message}");
                return false;
            }
        }

        private void AddLog(LogLevel level, string message) => AddLog(new LogEntry(level, message));

        private void AddLog(LogEntry entry) => Log.Insert(0, entry);

        private static MessageBoxResult Ask(string text)
            => MessageBox.Show(text, ToolCatalog.LinkMemory.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
    }
}
