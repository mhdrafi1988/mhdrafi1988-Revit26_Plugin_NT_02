using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models;
using Revit26_Plugin_Revised.WorksetMemory.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Revit26_Plugin_Revised.WorksetMemory.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Workset Memory window: lists the saved workset sets of a model, saves the
    /// open model's current worksets as a set, and, on Reload, hands a <see cref="ReloadRequest"/>
    /// back to the command, which reopens the model once the window has closed.
    /// </summary>
    public partial class WorksetMemoryViewModel : ObservableObject
    {
        private readonly Document _doc;               // active project, or null
        private readonly ModelIdentity _current;      // identity of _doc, or null
        private readonly List<SavedWorkset> _live;    // _doc's worksets, or empty
        private WorksetMemoryFile _file;              // as last read from or saved to disk
        private readonly string _startHint = string.Empty;

        /// <summary>Raised when the window should close (after Reload is accepted).</summary>
        public event EventHandler RequestClose;

        /// <summary>Set when the user clicked Reload; the command acts on it after the window closes.</summary>
        public ReloadRequest ReloadRequest { get; private set; }

        /// <summary>The active model plus every model with saved sets.</summary>
        public ObservableCollection<ModelChoice> Models { get; } = new();

        /// <summary>Saved sets of <see cref="SelectedModel"/>, newest first.</summary>
        public ObservableCollection<SetRow> Sets { get; } = new();

        /// <summary>Comparison of the selected set with the model now.</summary>
        public ObservableCollection<WorksetRow> Rows { get; } = new();

        /// <summary>Model whose sets are shown.</summary>
        [ObservableProperty] private ModelChoice selectedModel;

        /// <summary>Set being compared and reloaded.</summary>
        [ObservableProperty] private SetRow selectedSet;

        /// <summary>Name for a new set.</summary>
        [ObservableProperty] private string newSetName;

        /// <summary>Make the set's active workset current after reload (default on).</summary>
        [ObservableProperty] private bool restoreActiveWorkset = true;

        /// <summary>Header tile: user worksets in the model.</summary>
        [ObservableProperty] private int worksetCount;

        /// <summary>Header tile: worksets open now.</summary>
        [ObservableProperty] private int openNowCount;

        /// <summary>Header tile: saved sets of the selected model.</summary>
        [ObservableProperty] private int savedSetCount;

        /// <summary>Status strip text: what a reload would change.</summary>
        [ObservableProperty] private string summaryText = string.Empty;

        /// <summary>Why Reload is disabled, or why Remember is unavailable; empty when there is nothing to say.</summary>
        [ObservableProperty] private string hintText = string.Empty;

        /// <summary>Location line under the "Saved sets" title.</summary>
        [ObservableProperty] private string modelLocation = string.Empty;

        /// <summary>Creates the view model for the active project <paramref name="doc"/> (null when none is open).</summary>
        public WorksetMemoryViewModel(Document doc)
        {
            _file = WorksetMemoryService.Load(out string warning);
            if (warning != null)
                MessageBox.Show(warning, ToolCatalog.WorksetMemory.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

            if (doc != null && doc.IsWorkshared)
            {
                _doc = doc;
                _current = ModelIdentity.FromDocument(doc);
            }
            _live = _current != null ? WorksetMemoryService.ReadWorksets(doc) : new List<SavedWorkset>();
            RestoreActiveWorkset = WorksetMemoryService.LoadSettings().RestoreActiveWorkset;
            RefreshSavedLocation();

            if (_current != null)
                Models.Add(new ModelChoice(_current, _file.Find(_current.Key), true));
            foreach (var entry in _file.Models.Where(m => m.Model != null && m.Model.Key != _current?.Key)
                                              .OrderBy(m => m.Model.Name, StringComparer.OrdinalIgnoreCase))
                Models.Add(new ModelChoice(entry.Model, entry, false));

            if (doc != null && !doc.IsWorkshared)
                _startHint = "The open model is not workshared, so it has no worksets. Pick a remembered model to open it.";
            else if (doc != null && _current == null)
                _startHint = "Save the model once before its worksets can be remembered.";
            else if (Models.Count == 0)
                _startHint = "No model is open and no sets are saved yet. Open a workshared model and click Remember current.";

            NewSetName = DefaultSetName();
            SelectedModel = Models.FirstOrDefault();
            UpdateHint();
        }

        /// <summary>True when the selected model is the active one, so its worksets can be remembered.</summary>
        public bool IsCurrentModel => SelectedModel?.IsCurrent == true;

        partial void OnSelectedModelChanged(ModelChoice value)
        {
            OnPropertyChanged(nameof(IsCurrentModel));
            ModelLocation = value == null ? string.Empty : $"for {value.Model.Name} · {value.Model.Location}";
            ReloadSets(null);
            RememberCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedSetChanged(SetRow value)
        {
            Rebuild();
            OverwriteCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            ReloadCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Saves the active model's current worksets as a new set (or replaces a set of the same name).</summary>
        [RelayCommand(CanExecute = nameof(CanRemember))]
        private void Remember()
        {
            string name = (NewSetName ?? string.Empty).Trim();
            if (name.Length == 0) name = DefaultSetName();

            if (SelectedModel.Entry?.FindSet(name) != null
                && Ask($"A set called '{name}' already exists. Replace it with the current worksets?") != MessageBoxResult.Yes)
                return;

            WorksetSet set = WorksetMemoryService.Capture(_doc, name);
            ModelIdentity model = _current;
            bool saved = Commit(file =>
            {
                WorksetModelEntry entry = file.Ensure(model);
                entry.Sets.RemoveAll(s => SameName(s.Name, name));
                entry.Sets.Insert(0, set);
                entry.Model = model;   // keep the latest local path
                return true;
            });

            if (saved) NewSetName = DefaultSetName();
            ReloadSets(name);
        }

        private bool CanRemember() => IsCurrentModel && _doc != null && _doc.IsValidObject;

        /// <summary>Replaces the selected set with the active model's current worksets.</summary>
        [RelayCommand(CanExecute = nameof(CanOverwrite))]
        private void Overwrite()
        {
            string name = SelectedSet.Name;
            if (Ask($"Replace '{name}' with the worksets open now?") != MessageBoxResult.Yes) return;

            WorksetSet set = WorksetMemoryService.Capture(_doc, name);
            ModelIdentity model = _current;
            Commit(file =>
            {
                WorksetModelEntry entry = file.Ensure(model);
                int index = entry.Sets.FindIndex(s => SameName(s.Name, name));
                if (index >= 0) entry.Sets[index] = set;
                else entry.Sets.Insert(0, set);   // deleted in another session meanwhile
                entry.Model = model;
                return true;
            });
            ReloadSets(name);
        }

        private bool CanOverwrite() => CanRemember() && SelectedSet != null;

        /// <summary>Deletes the selected set.</summary>
        [RelayCommand(CanExecute = nameof(HasSet))]
        private void Delete()
        {
            string name = SelectedSet.Name;
            if (Ask($"Delete the set '{name}'?") != MessageBoxResult.Yes) return;

            string key = SelectedModel.Model.Key;
            bool keepEntry = SelectedModel.IsCurrent;
            Commit(file =>
            {
                WorksetModelEntry entry = file.Find(key);
                if (entry == null) return false;
                int removed = entry.Sets.RemoveAll(s => SameName(s.Name, name));
                if (entry.Sets.Count == 0 && !keepEntry) file.Models.Remove(entry);
                return removed > 0;
            });
            ReloadSets(null);
        }

        private bool HasSet() => SelectedSet != null;

        /// <summary>Accepts the reload; the command reopens the model after the window closes.</summary>
        [RelayCommand(CanExecute = nameof(CanReload))]
        private void Reload()
        {
            var model = SelectedModel.Model;
            string what = IsCurrentModel || IsOpenElsewhere(model)
                ? $"'{model.Name}' will close and reopen with the worksets of '{SelectedSet.Name}' open."
                : $"'{model.Name}' will open with the worksets of '{SelectedSet.Name}' open.";
            if (Ask(what + "\n\nContinue?") != MessageBoxResult.Yes) return;

            ReloadRequest = new ReloadRequest
            {
                Model = model,
                Set = SelectedSet.Set,
                RestoreActiveWorkset = RestoreActiveWorkset
            };
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private bool CanReload()
        {
            if (SelectedSet == null || SelectedModel == null) return false;
            if (IsCurrentModel && _doc.IsValidObject && _doc.IsModified) return false;
            return true;
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private bool IsOpenElsewhere(ModelIdentity model)
            => _doc != null && _doc.IsValidObject
               && WorksetReopenService.FindOpen(_doc.Application, model) != null;

        // Shows the sets of the selected model, selecting the one called selectName (or the first).
        private void ReloadSets(string selectName)
        {
            Sets.Clear();
            foreach (var s in SelectedModel?.Entry?.Sets ?? new List<WorksetSet>())
                Sets.Add(new SetRow(s));
            SavedSetCount = Sets.Count;
            SelectedSet = Sets.FirstOrDefault(r => SameName(r.Name, selectName)) ?? Sets.FirstOrDefault();
            Rebuild();
        }

        // The open model may be a new local copy (or the central moved): store where it is now, so
        // the next session opens the right file when no model is open.
        private void RefreshSavedLocation()
        {
            if (_current == null) return;
            WorksetModelEntry entry = _file.Find(_current.Key);
            if (entry == null || !WorksetMemoryService.LocationChanged(entry.Model, _current)) return;

            ModelIdentity model = _current;
            Commit(file =>
            {
                WorksetModelEntry latest = file.Find(model.Key);
                if (latest == null || !WorksetMemoryService.LocationChanged(latest.Model, model)) return false;
                latest.Model = model;
                return true;
            });
        }

        // Builds the comparison grid, tiles and summary for the selected model and set.
        private void Rebuild()
        {
            Rows.Clear();
            bool current = IsCurrentModel;
            WorksetSet set = SelectedSet?.Set;

            WorksetCount = current ? _live.Count : set?.Worksets.Count ?? 0;
            OpenNowCount = current ? _live.Count(w => w.IsOpen) : 0;

            if (set == null)
            {
                foreach (var w in _live)
                    Rows.Add(new WorksetRow { Name = w.Name, Now = OpenText(w.IsOpen), InSet = "—", Change = WorksetChange.None });
                SummaryText = current ? "No set saved yet — click Remember current." : "No saved sets for this model.";
                UpdateHint();
                return;
            }

            var matched = new HashSet<SavedWorkset>();
            int opens = 0, closes = 0, skipped = 0;

            foreach (var saved in set.Worksets)
            {
                SavedWorkset live = current
                    ? WorksetMemoryService.Match(_live, saved.Name, saved.Id, l => l.Name, l => l.Id)
                    : null;
                if (live != null) matched.Add(live);

                bool missing = current && live == null;
                WorksetChange change;
                if (missing) change = saved.IsOpen ? WorksetChange.Skipped : WorksetChange.None;
                else if (!current) change = saved.IsOpen ? WorksetChange.Opens : WorksetChange.None;
                else if (saved.IsOpen && !live.IsOpen) change = WorksetChange.Opens;
                else if (!saved.IsOpen && live.IsOpen) change = WorksetChange.Closes;
                else change = WorksetChange.None;

                if (change == WorksetChange.Opens) opens++;
                if (change == WorksetChange.Closes) closes++;
                if (change == WorksetChange.Skipped) skipped++;

                Rows.Add(new WorksetRow
                {
                    Name = live?.Name ?? saved.Name,
                    Now = current ? (live == null ? "—" : OpenText(live.IsOpen)) : "—",
                    InSet = OpenText(saved.IsOpen),
                    Change = change,
                    IsActive = IsActiveIn(set, saved),
                    IsMissing = missing
                });
            }

            // Worksets created after the set was saved: reload opens only the set's worksets, so they close.
            foreach (var live in _live.Where(l => current && !matched.Contains(l)))
            {
                var change = live.IsOpen ? WorksetChange.Closes : WorksetChange.None;
                if (change == WorksetChange.Closes) closes++;
                Rows.Add(new WorksetRow { Name = live.Name, Now = OpenText(live.IsOpen), InSet = "Not in set", Change = change });
            }

            var ordered = Rows.OrderByDescending(r => r.IsActive).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Rows.Clear();
            foreach (var r in ordered) Rows.Add(r);

            SummaryText = current
                ? $"{opens} open · {closes} close · {skipped} skipped"
                : $"{set.OpenCount} of {set.Worksets.Count} worksets will open" + (skipped > 0 ? $" · {skipped} skipped" : "");
            UpdateHint();
        }

        private void UpdateHint()
        {
            HintText = IsCurrentModel && _doc.IsValidObject && _doc.IsModified
                ? "This model has unsaved changes. Save or Synchronize with Central, then open this window again to reload."
                : _startHint;
            ReloadCommand.NotifyCanExecuteChanged();
        }

        private static bool IsActiveIn(WorksetSet set, SavedWorkset saved)
            => !string.IsNullOrEmpty(set.ActiveWorksetName)
               && (string.Equals(set.ActiveWorksetName, saved.Name, StringComparison.OrdinalIgnoreCase)
                   || set.ActiveWorksetId == saved.Id && set.Worksets.All(w => !string.Equals(w.Name, set.ActiveWorksetName, StringComparison.OrdinalIgnoreCase)));

        private static string OpenText(bool open) => open ? "Open" : "Closed";

        private static string DefaultSetName() => $"Set {DateTime.Now:dd MMM HH:mm}";

        /// <summary>Saves the window options so the next session starts with them.</summary>
        public void SaveSettings()
            => WorksetMemoryService.SaveSettings(new WorksetMemorySettings { RestoreActiveWorkset = RestoreActiveWorkset });

        // Applies change to the latest memory file on disk (so sets saved by another Revit session
        // are kept), saves it and points the model list at the result. False when it could not be saved.
        private bool Commit(Func<WorksetMemoryFile, bool> change)
        {
            try
            {
                _file = WorksetMemoryService.Update(change);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException)
            {
                MessageBox.Show($"The sets could not be saved to {WorksetMemoryService.FilePath}:\n{ex.Message}",
                    ToolCatalog.WorksetMemory.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            foreach (ModelChoice choice in Models) choice.Entry = _file.Find(choice.Model.Key);
            return true;
        }

        private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static MessageBoxResult Ask(string text)
            => MessageBox.Show(text, ToolCatalog.WorksetMemory.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
    }
}
