using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Revit26_Plugin_Revised.LinkMemory.V002.UI.ViewModels
{
    /// <summary>
    /// View model of the Link Memory V002 window. It shows one "working" memory: either what Collect
    /// just found (not saved yet) or the selected saved memory. Save new, Update, Reload and Export
    /// act on it. The window is modal and opened from the command, so the Revit API is called
    /// directly from its commands.
    /// </summary>
    public partial class LinkMemoryViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly ModelIdentity _model;
        private LinkMemoryFile _file;   // as last read from or saved to disk
        private List<LiveLink> _live = new();
        private LinkMemoryItem _working;

        /// <summary>Saved memories of this model, newest first.</summary>
        public ObservableCollection<MemoryRow> Memories { get; } = new();

        /// <summary>Views of the working memory.</summary>
        public ObservableCollection<ViewRow> Views { get; } = new();

        /// <summary>Links of the working memory, compared with the model now.</summary>
        public ObservableCollection<LinkRow> Links { get; } = new();

        /// <summary>Print set names of the model.</summary>
        public ObservableCollection<string> PrintSets { get; } = new();

        /// <summary>Run log, newest first.</summary>
        public ObservableCollection<LogEntry> Log { get; } = new();

        /// <summary>Collect the active view and its related views.</summary>
        [ObservableProperty] private bool isViewScope = true;

        /// <summary>Collect a print set.</summary>
        [ObservableProperty] private bool isPrintSetScope;

        /// <summary>Chosen print set.</summary>
        [ObservableProperty] private string selectedPrintSet;

        /// <summary>Follow section marks.</summary>
        [ObservableProperty] private bool includeSections = true;
        /// <summary>Follow elevation marks.</summary>
        [ObservableProperty] private bool includeElevations = true;
        /// <summary>Follow callouts.</summary>
        [ObservableProperty] private bool includeCallouts = true;
        /// <summary>Follow references to drafting views.</summary>
        [ObservableProperty] private bool includeDrafting = true;
        /// <summary>Print set: include views placed on sheets.</summary>
        [ObservableProperty] private bool includePlacedViews = true;

        /// <summary>List Revit links.</summary>
        [ObservableProperty] private bool kindRevit = true;
        /// <summary>List CAD links and imports.</summary>
        [ObservableProperty] private bool kindCad = true;
        /// <summary>List IFC links.</summary>
        [ObservableProperty] private bool kindIfc = true;
        /// <summary>List point clouds.</summary>
        [ObservableProperty] private bool kindPointCloud = true;
        /// <summary>List images and PDFs.</summary>
        [ObservableProperty] private bool kindImage = true;

        /// <summary>Unload Revit / IFC links the memory doesn't use (default on).</summary>
        [ObservableProperty] private bool unloadUnused = true;

        /// <summary>Also reload used links that are already loaded (default off).</summary>
        [ObservableProperty] private bool reloadUsed;

        /// <summary>Selected saved memory.</summary>
        [ObservableProperty] private MemoryRow selectedMemory;

        /// <summary>Name box: name for Save new and Rename.</summary>
        [ObservableProperty] private string memoryName;

        /// <summary>What the lists show ("Collected now – not saved", "Saved memory 'X'").</summary>
        [ObservableProperty] private string workingTitle = string.Empty;

        /// <summary>Header tile: views collected.</summary>
        [ObservableProperty] private int viewCount;
        /// <summary>Header tile: links the working memory uses.</summary>
        [ObservableProperty] private int usedCount;
        /// <summary>Header tile: links in the model.</summary>
        [ObservableProperty] private int modelLinkCount;
        /// <summary>Header tile: saved memories.</summary>
        [ObservableProperty] private int memoryCount;

        /// <summary>Status strip text: what a reload would change.</summary>
        [ObservableProperty] private string summaryText = string.Empty;

        /// <summary>True while links are being loaded.</summary>
        [ObservableProperty] private bool isRunning;

        /// <summary>Log panel expanded.</summary>
        [ObservableProperty] private bool isLogExpanded = true;

        /// <summary>Creates the view model for <paramref name="doc"/> with <paramref name="activeView"/> as the active view.</summary>
        public LinkMemoryViewModel(Document doc, View activeView)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView;
            _model = ModelIdentity.FromDocument(doc);
            _file = LinkMemoryStore.Load(out string warning);
            if (warning != null) AddLog(LogLevel.Warning, warning);
            if (!_file.V001Imported) ImportV001();
            ApplySettings(LinkMemoryStore.LoadSettings());

            foreach (ViewSheetSet set in LinkMemoryCapture.PrintSets(doc)) PrintSets.Add(set.Name);
            SelectedPrintSet = PrintSets.FirstOrDefault();

            RefreshLive();
            ReloadMemories(null);

            if (ViewCollector.IsCollectable(_activeView))
            {
                Collect();
            }
            else
            {
                AddLog(LogLevel.Info, $"The active view ({_activeView?.Name ?? "none"}) can't show links. " +
                                      "Open a plan, section or sheet, or pick a print set.");
                if (PrintSets.Count > 0) IsPrintSetScope = true;
                ShowWorking(null, "Nothing collected yet");
            }
        }

        /// <summary>Active view name and type for the scope card.</summary>
        public string ActiveViewText => _activeView == null ? "—"
            : $"{ViewCollector.NameOf(_activeView)}  ({ViewCollector.TypeText(_activeView)})";

        /// <summary>Model name and location for the "Saved memories" title.</summary>
        public string ModelLocation => _model == null ? "" : $"for {_model.Name} · {_model.Location}";

        partial void OnIsViewScopeChanged(bool value) { if (value) IsPrintSetScope = false; }

        partial void OnIsPrintSetScopeChanged(bool value) { if (value) IsViewScope = false; }

        partial void OnSelectedMemoryChanged(MemoryRow value)
        {
            if (value != null)
            {
                MemoryName = value.Name;
                ShowWorking(value.Memory, $"Saved memory '{value.Name}'");
            }
            UpdateCommand.NotifyCanExecuteChanged();
            RenameCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }

        partial void OnUnloadUnusedChanged(bool value) => RebuildLinks();

        partial void OnReloadUsedChanged(bool value) => RebuildLinks();

        partial void OnIsRunningChanged(bool value) => ReloadCommand.NotifyCanExecuteChanged();

        // ── commands ───────────────────────────────────────────────────────

        /// <summary>Collects the active view (and related views) or the chosen print set into the working memory.</summary>
        [RelayCommand]
        private void Collect()
        {
            if (!CheckModel()) return;
            CollectOptions options = CurrentOptions();
            LinkMemoryItem memory;

            if (IsPrintSetScope)
            {
                ViewSheetSet set = FindPrintSet(SelectedPrintSet);
                if (set == null)
                {
                    AddLog(LogLevel.Warning, PrintSets.Count == 0
                        ? "The model has no saved print sets (Print › Print Setup › View/Sheet Set)."
                        : "Pick a print set first.");
                    return;
                }
                memory = LinkMemoryCapture.FromPrintSet(_doc, set, options, AddLog);
            }
            else
            {
                if (!ViewCollector.IsCollectable(_activeView))
                {
                    AddLog(LogLevel.Warning, "The active view can't show links. Open a plan, section or sheet first.");
                    return;
                }
                memory = LinkMemoryCapture.FromView(_doc, _activeView, options, AddLog);
            }

            SelectedMemory = null;
            MemoryName = memory.Name;
            ShowWorking(memory, "Collected now – not saved");
            LogCollected(memory);
        }

        /// <summary>Saves the working memory under the name in the name box (or replaces one of that name).</summary>
        [RelayCommand]
        private void SaveNew()
        {
            if (!CheckSavedModel()) return;
            if (_working == null)
            {
                AddLog(LogLevel.Warning, "Collect a view or print set first.");
                return;
            }

            string name = (MemoryName ?? string.Empty).Trim();
            if (name.Length == 0) name = _working.SourceName ?? $"Memory {DateTime.Now:dd MMM HH:mm}";

            LinkMemoryItem existing = _file.Find(_model.Key)?.FindMemory(name);
            if (existing != null && existing != _working
                && Ask($"A memory called '{name}' already exists. Replace it?") != MessageBoxResult.Yes)
                return;

            LinkMemoryItem copy = Copy(_working);
            copy.Name = name;
            copy.SavedAt = DateTime.Now;
            copy.SavedBy = _doc.Application.Username;
            ModelIdentity model = _model;
            bool saved = Commit(file =>
            {
                LinkModelEntry entry = file.Ensure(model);
                entry.Memories.RemoveAll(m => SameName(m.Name, name));
                entry.Memories.Insert(0, copy);
                entry.Model = model;
                return true;
            });

            if (saved) AddLog(LogLevel.Success, $"Saved '{name}': {copy.UsedCount} links used in {copy.Views.Count} views.");
            ReloadMemories(saved ? name : SelectedMemory?.Name);
        }

        /// <summary>Collects the selected memory's view or print set again and overwrites the memory.</summary>
        [RelayCommand(CanExecute = nameof(HasMemory))]
        private void Update()
        {
            if (!CheckSavedModel()) return;
            LinkMemoryItem old = SelectedMemory.Memory;
            CollectOptions options = old.Options?.Clone() ?? new CollectOptions();
            LinkMemoryItem fresh;

            switch (old.Scope)
            {
                case MemoryScope.ActiveView:
                    View view = FindView(old);
                    if (view == null)
                    {
                        AddLog(LogLevel.Error, $"Can't update '{old.Name}': the view '{old.SourceName}' is no longer in the model.");
                        return;
                    }
                    fresh = LinkMemoryCapture.FromView(_doc, view, options, AddLog);
                    break;
                case MemoryScope.PrintSet:
                    ViewSheetSet set = FindPrintSet(old.SourceName);
                    if (set == null)
                    {
                        AddLog(LogLevel.Error, $"Can't update '{old.Name}': the print set '{old.SourceName}' is no longer in the model.");
                        return;
                    }
                    fresh = LinkMemoryCapture.FromPrintSet(_doc, set, options, AddLog);
                    break;
                default:
                    fresh = LinkMemoryCapture.WholeModel(_doc, options);
                    break;
            }

            if (Ask($"Collect '{old.SourceName}' again and replace the saved '{old.Name}' with what is found now?") != MessageBoxResult.Yes)
                return;

            string name = old.Name;
            fresh.Name = name;
            fresh.FromV001 = false;
            ModelIdentity model = _model;
            bool saved = Commit(file =>
            {
                LinkModelEntry entry = file.Ensure(model);
                int index = entry.Memories.FindIndex(m => SameName(m.Name, name));
                if (index >= 0) entry.Memories[index] = fresh;
                else entry.Memories.Insert(0, fresh);   // deleted in another session meanwhile
                return true;
            });

            if (saved) AddLog(LogLevel.Success, $"Updated '{name}': {fresh.UsedCount} links used in {fresh.Views.Count} views.");
            LogCollected(fresh);
            ReloadMemories(name);
        }

        /// <summary>Renames the selected memory to the name in the name box.</summary>
        [RelayCommand(CanExecute = nameof(HasMemory))]
        private void Rename()
        {
            LinkMemoryItem memory = SelectedMemory.Memory;
            string name = (MemoryName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                AddLog(LogLevel.Warning, "Type the new name in the Name box first.");
                return;
            }
            if (string.Equals(name, memory.Name, StringComparison.Ordinal)) return;

            string oldName = memory.Name;
            string key = _model?.Key;
            bool taken = false, gone = false;
            bool saved = Commit(file =>
            {
                LinkModelEntry entry = file.Find(key);
                LinkMemoryItem target = entry?.FindMemory(oldName);
                if (target == null) { gone = true; return false; }
                if (entry.Memories.Any(m => m != target && SameName(m.Name, name))) { taken = true; return false; }
                target.Name = name;
                return true;
            });

            if (taken) AddLog(LogLevel.Warning, $"A memory called '{name}' already exists. Pick another name.");
            else if (gone) AddLog(LogLevel.Warning, $"'{oldName}' is no longer saved (deleted in another Revit session?).");
            else if (saved) AddLog(LogLevel.Success, $"Renamed '{oldName}' to '{name}'.");
            ReloadMemories(saved && !taken && !gone ? name : oldName);
        }

        /// <summary>Deletes the selected memory.</summary>
        [RelayCommand(CanExecute = nameof(HasMemory))]
        private void Delete()
        {
            string name = SelectedMemory.Name;
            if (Ask($"Delete the memory '{name}'?") != MessageBoxResult.Yes) return;

            string key = _model?.Key;
            bool saved = Commit(file =>
            {
                LinkModelEntry entry = file.Find(key);
                if (entry == null) return false;
                int removed = entry.Memories.RemoveAll(m => SameName(m.Name, name));
                if (entry.Memories.Count == 0) file.Models.Remove(entry);
                return removed > 0;
            });

            if (saved) AddLog(LogLevel.Info, $"Deleted '{name}'.");
            ReloadMemories(saved ? null : name);
            if (saved) ShowWorking(null, "Nothing selected");
        }

        private bool HasMemory() => SelectedMemory != null;

        /// <summary>Exports the working memory to Excel.</summary>
        [RelayCommand]
        private void Export()
        {
            if (_working == null)
            {
                AddLog(LogLevel.Warning, "Collect a view or print set, or pick a memory, first.");
                return;
            }

            string name = string.IsNullOrWhiteSpace(MemoryName) ? _working.Name : MemoryName.Trim();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export link memory",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"LinkMemory_{SafeFileName(name)}_{DateTime.Now:yyyy-MM-dd}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var copy = Copy(_working);
                copy.Name = name;
                LinkMemoryExporter.Save(dlg.FileName, _model?.Name ?? _doc.Title, copy, _live);
                AddLog(LogLevel.Success, $"Exported to {dlg.FileName}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException || ex is ArgumentException)
            {
                AddLog(LogLevel.Error, $"Could not export: {ex.Message} (is the file open in Excel?)");
            }
        }

        /// <summary>Loads the links the working memory uses and unloads the ones it doesn't (if chosen).</summary>
        [RelayCommand(CanExecute = nameof(CanReload))]
        private void Reload()
        {
            if (!CheckModel()) return;
            if (_doc.IsReadOnly)
            {
                AddLog(LogLevel.Error, "The model is read-only, so links can't be loaded or unloaded.");
                return;
            }
            if (_working == null)
            {
                AddLog(LogLevel.Warning, "Collect a view or print set, or pick a memory, first.");
                return;
            }

            var changes = Links.Where(r => !r.NotInMemory && r.Action is LinkAction.Load or LinkAction.Unload or LinkAction.Reload).ToList();
            if (changes.Count == 0)
            {
                AddLog(LogLevel.Info, "Nothing to change: the links already match the memory.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Apply '{(string.IsNullOrWhiteSpace(MemoryName) ? _working.Name : MemoryName)}'?\n");
            foreach (var r in changes.Take(15)) sb.AppendLine($"  • {r.ActionText}: {r.Name}");
            if (changes.Count > 15) sb.AppendLine($"  … and {changes.Count - 15} more");
            sb.AppendLine("\nLoading links can take a while, and Revit clears the Undo list when a link loads.");
            if (Ask(sb.ToString()) != MessageBoxResult.Yes) return;

            IsRunning = true;
            try
            {
                AddLog(LogLevel.Info, $"Applying '{_working.Name}'…");
                ReloadResult result = LinkApplier.Apply(_doc, _working, CurrentReloadOptions(), AddLog);
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
                RebuildLinks();
            }
        }

        private bool CanReload() => !IsRunning;

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

        // ── helpers ────────────────────────────────────────────────────────

        private CollectOptions CurrentOptions() => new()
        {
            Sections = IncludeSections,
            Elevations = IncludeElevations,
            Callouts = IncludeCallouts,
            DraftingViews = IncludeDrafting,
            PlacedViews = IncludePlacedViews,
            Revit = KindRevit,
            Cad = KindCad,
            Ifc = KindIfc,
            PointClouds = KindPointCloud,
            Images = KindImage
        };

        private ReloadOptions CurrentReloadOptions() => new() { UnloadUnused = UnloadUnused, ReloadUsed = ReloadUsed };

        private bool CheckModel()
        {
            if (_doc.IsValidObject) return true;
            AddLog(LogLevel.Error, "The model was closed. Close this window.");
            return false;
        }

        private bool CheckSavedModel()
        {
            if (!CheckModel()) return false;
            if (_model != null) return true;
            MessageBox.Show("Save the model once before its links can be remembered.",
                ToolCatalog.LinkMemoryV002.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void RefreshLive()
        {
            _live = _doc.IsValidObject ? LinkCatalog.Read(_doc) : new List<LiveLink>();
            ModelLinkCount = _live.Count;
        }

        private ViewSheetSet FindPrintSet(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return LinkMemoryCapture.PrintSets(_doc).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private View FindView(LinkMemoryItem memory)
        {
            if (!string.IsNullOrEmpty(memory.SourceUniqueId) && _doc.GetElement(memory.SourceUniqueId) is View byId) return byId;
            return new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(ViewCollector.NameOf(v), memory.SourceName, StringComparison.OrdinalIgnoreCase));
        }

        // Shows the saved memories of this model, selecting the one called selectName (or none).
        private void ReloadMemories(string selectName)
        {
            Memories.Clear();
            foreach (var m in _file.Find(_model?.Key)?.Memories ?? new List<LinkMemoryItem>())
                Memories.Add(new MemoryRow(m));
            MemoryCount = Memories.Count;
            SelectedMemory = selectName == null ? null : Memories.FirstOrDefault(r => SameName(r.Name, selectName));
        }

        // Imports the Link Memory V001 sets once, into the latest file on disk.
        private void ImportV001()
        {
            int imported = 0;
            string importWarning = null;
            bool saved = Commit(file =>
            {
                bool wasImported = file.V001Imported;
                imported = LinkMemoryStore.ImportV001(file, out importWarning);
                return imported > 0 || file.V001Imported != wasImported;
            });

            if (importWarning != null) AddLog(LogLevel.Warning, importWarning);
            if (saved && imported > 0)
                AddLog(LogLevel.Success, $"Imported {imported} set(s) from Link Memory V001 as whole-model memories.");
        }

        private void ApplySettings(LinkMemorySettings settings)
        {
            CollectOptions c = settings.Collect;
            IncludeSections = c.Sections;
            IncludeElevations = c.Elevations;
            IncludeCallouts = c.Callouts;
            IncludeDrafting = c.DraftingViews;
            IncludePlacedViews = c.PlacedViews;
            KindRevit = c.Revit;
            KindCad = c.Cad;
            KindIfc = c.Ifc;
            KindPointCloud = c.PointClouds;
            KindImage = c.Images;
            UnloadUnused = settings.UnloadUnused;
            ReloadUsed = settings.ReloadUsed;
        }

        /// <summary>Saves the window options so the next session starts with them.</summary>
        public void SaveSettings() => LinkMemoryStore.SaveSettings(new LinkMemorySettings
        {
            Collect = CurrentOptions(),
            UnloadUnused = UnloadUnused,
            ReloadUsed = ReloadUsed
        });

        private void ShowWorking(LinkMemoryItem memory, string title)
        {
            _working = memory;
            WorkingTitle = title;
            Views.Clear();
            foreach (SavedView v in memory?.Views ?? new List<SavedView>())
            {
                Views.Add(new ViewRow
                {
                    Name = v.Name, TypeText = v.ViewType, Reason = v.Reason, Depth = v.Depth, LinkCount = v.LinkIds?.Count ?? 0
                });
            }
            ViewCount = Views.Count;
            RebuildLinks();
        }

        // Builds the links grid and summary for the working memory.
        private void RebuildLinks()
        {
            Links.Clear();
            LinkMemoryItem memory = _working;
            if (memory == null)
            {
                UsedCount = 0;
                SummaryText = "Collect a view or print set, or pick a saved memory.";
                return;
            }

            ReloadOptions options = CurrentReloadOptions();
            var rows = new List<LinkRow>();
            var matched = new HashSet<LiveLink>();
            int loads = 0, unloads = 0, reloads = 0, skipped = 0, unknown = 0;

            foreach (SavedLink saved in memory.Links)
            {
                LiveLink live = _doc.IsValidObject ? LinkCatalog.Match(_live, saved) : null;
                if (live != null) matched.Add(live);
                LinkPlan plan = _doc.IsValidObject ? LinkApplier.Plan(_doc, saved, live, options) : new LinkPlan();

                switch (plan.Action)
                {
                    case LinkAction.Load: loads++; break;
                    case LinkAction.Unload: unloads++; break;
                    case LinkAction.Reload: reloads++; break;
                    case LinkAction.Skipped: skipped++; break;
                }
                if (saved.Use == LinkUse.Unknown) unknown++;

                rows.Add(new LinkRow
                {
                    Name = live?.Name ?? saved.Name,
                    Note = plan.Note ?? (live == null ? "not in model" : null),
                    Path = live?.Path ?? saved.Path,
                    KindText = LinkCatalog.KindText(saved.Kind),
                    ViewsText = memory.Scope == MemoryScope.WholeModel
                        ? (saved.Use == LinkUse.Used ? "loaded" : "—")
                        : saved.Use == LinkUse.Unknown ? "unknown" : saved.ViewCount > 0 ? $"{saved.ViewCount} views" : "—",
                    Now = live == null ? "—" : LinkMemoryExporter.StatusText(live),
                    Use = saved.Use,
                    Action = plan.Action
                });
            }

            // Links added after the memory was collected (of the kinds it lists) are left as they are.
            foreach (LiveLink live in _live.Where(l => !matched.Contains(l) && (memory.Options ?? new CollectOptions()).Includes(l.Kind)))
            {
                if (memory.Scope == MemoryScope.WholeModel && memory.FromV001 && live.Kind is not (LinkKind.Revit or LinkKind.Ifc)) continue;
                rows.Add(new LinkRow
                {
                    Name = live.Name, Path = live.Path, KindText = LinkCatalog.KindText(live.Kind), ViewsText = "—",
                    Now = LinkMemoryExporter.StatusText(live), Use = LinkUse.NotUsed, NotInMemory = true
                });
            }

            foreach (LinkRow r in rows.OrderBy(r => r.IsDim).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) Links.Add(r);

            UsedCount = memory.UsedCount;
            var parts = new List<string> { $"Reload will load {loads}", $"unload {unloads}" };
            if (ReloadUsed) parts.Add($"reload {reloads}");
            if (skipped > 0) parts.Add($"skip {skipped}");
            string text = string.Join(", ", parts) + ".";
            if (unknown > 0) text += $" {unknown} unloaded link(s) not checked: load them and collect again.";
            SummaryText = text;
        }

        private void LogCollected(LinkMemoryItem memory)
        {
            int byKind(LinkKind k) => memory.Links.Count(l => l.Use == LinkUse.Used && l.Kind == k);
            AddLog(LogLevel.Info, $"Collected {memory.Views.Count} view(s) from '{memory.SourceName}'.");
            AddLog(LogLevel.Info, $"{memory.UsedCount} link(s) visible: {byKind(LinkKind.Revit)} Revit, {byKind(LinkKind.Ifc)} IFC, " +
                                  $"{byKind(LinkKind.CadLink) + byKind(LinkKind.CadImport)} CAD, {byKind(LinkKind.PointCloud)} point cloud, " +
                                  $"{byKind(LinkKind.Image)} image/PDF.");
            int unknown = memory.Links.Count(l => l.Use == LinkUse.Unknown);
            if (unknown > 0)
                AddLog(LogLevel.Warning, $"{unknown} link(s) are unloaded, so Revit can't tell if these views show them. " +
                                         "Load them and collect again to include them.");
        }

        private static LinkMemoryItem Copy(LinkMemoryItem m) => new()
        {
            Name = m.Name,
            Scope = m.Scope,
            SourceUniqueId = m.SourceUniqueId,
            SourceName = m.SourceName,
            Options = m.Options?.Clone() ?? new CollectOptions(),
            SavedAt = m.SavedAt,
            SavedBy = m.SavedBy,
            FromV001 = m.FromV001,
            Views = m.Views.Select(v => new SavedView
            {
                UniqueId = v.UniqueId, Name = v.Name, ViewType = v.ViewType, SheetNumber = v.SheetNumber,
                SheetName = v.SheetName, Reason = v.Reason, Depth = v.Depth, LinkIds = new List<string>(v.LinkIds ?? new List<string>())
            }).ToList(),
            Links = m.Links.Select(l => new SavedLink
            {
                UniqueId = l.UniqueId, Name = l.Name, Kind = l.Kind, Path = l.Path,
                WasLoaded = l.WasLoaded, Use = l.Use, ViewCount = l.ViewCount
            }).ToList()
        };

        private static string SafeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "Memory") sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            return sb.ToString();
        }

        // Applies change to the latest memory file on disk (so memories saved by another Revit session
        // are kept) and saves it. False when it could not be saved.
        private bool Commit(Func<LinkMemoryFile, bool> change)
        {
            try
            {
                _file = LinkMemoryStore.Update(change);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                AddLog(LogLevel.Error, $"Could not save to {LinkMemoryStore.FilePath}: {ex.Message}");
                return false;
            }
        }

        private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private void AddLog(LogLevel level, string message) => AddLog(new LogEntry(level, message));

        private void AddLog(LogEntry entry) => Log.Insert(0, entry);

        private static MessageBoxResult Ask(string text)
            => MessageBox.Show(text, ToolCatalog.LinkMemoryV002.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
    }
}
