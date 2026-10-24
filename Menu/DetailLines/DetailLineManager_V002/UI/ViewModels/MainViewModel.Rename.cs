using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.UI.ViewModels
{
    /// <summary>This part: the Rename styles tab.</summary>
    public sealed partial class MainViewModel
    {
        /// <summary>Tab index of Rename styles.</summary>
        public const int RenameTabIndex = 2;

        private bool _suspendRenamePreview;

        /// <summary>Preview of the renames, one row per listed style.</summary>
        public ObservableCollection<RenameRow> RenameRows { get; } = new();

        /// <summary>Letter case options, in the order of the Case list.</summary>
        public NameCase[] NameCaseOptions { get; } = (NameCase[])Enum.GetValues(typeof(NameCase));

        /// <summary>Which styles are listed.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RenameFromFound), nameof(RenameFromTicked), nameof(RenameFromAll))]
        private RenameScope renameScope = RenameScope.FoundLines;

        /// <summary>Scope = styles of the found lines; bound to its radio button.</summary>
        public bool RenameFromFound { get => RenameScope == RenameScope.FoundLines; set { if (value) RenameScope = RenameScope.FoundLines; } }

        /// <summary>Scope = styles ticked in the Line styles list; bound to its radio button.</summary>
        public bool RenameFromTicked { get => RenameScope == RenameScope.TickedStyles; set { if (value) RenameScope = RenameScope.TickedStyles; } }

        /// <summary>Scope = every non-built-in style; bound to its radio button.</summary>
        public bool RenameFromAll { get => RenameScope == RenameScope.AllCustom; set { if (value) RenameScope = RenameScope.AllCustom; } }

        /// <summary>Text to find.</summary>
        [ObservableProperty] private string renameFind = string.Empty;

        /// <summary>Replacement.</summary>
        [ObservableProperty] private string renameReplace = string.Empty;

        /// <summary>Find is a regular expression.</summary>
        [ObservableProperty] private bool renameUseRegex;

        /// <summary>Find matches case.</summary>
        [ObservableProperty] private bool renameMatchCase;

        /// <summary>Prefix.</summary>
        [ObservableProperty] private string renamePrefix = string.Empty;

        /// <summary>Suffix.</summary>
        [ObservableProperty] private string renameSuffix = string.Empty;

        /// <summary>Letter case.</summary>
        [ObservableProperty] private NameCase renameCase = NameCase.Unchanged;

        /// <summary>Trim and collapse spaces.</summary>
        [ObservableProperty] private bool renameTrim = true;

        /// <summary>What to do with a taken name.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RenameSkipDuplicates), nameof(RenameNumberDuplicates))]
        private DuplicateNameRule renameOnDuplicate = DuplicateNameRule.Skip;

        /// <summary>On duplicate = skip; bound to its radio button.</summary>
        public bool RenameSkipDuplicates { get => RenameOnDuplicate == DuplicateNameRule.Skip; set { if (value) RenameOnDuplicate = DuplicateNameRule.Skip; } }

        /// <summary>On duplicate = add a number; bound to its radio button.</summary>
        public bool RenameNumberDuplicates { get => RenameOnDuplicate == DuplicateNameRule.AddNumber; set { if (value) RenameOnDuplicate = DuplicateNameRule.AddNumber; } }

        /// <summary>Styles listed.</summary>
        [ObservableProperty] private int renameTotalCount;

        /// <summary>Styles that will be renamed.</summary>
        [ObservableProperty] private int renameWillCount;

        /// <summary>Styles with a duplicate or invalid new name.</summary>
        [ObservableProperty] private int renameProblemCount;

        /// <summary>Styles whose name stays the same.</summary>
        [ObservableProperty] private int renameUnchangedCount;

        /// <summary>Invalid pattern message, or empty.</summary>
        [ObservableProperty] private string renameRuleError = string.Empty;

        /// <summary>Error of the last rename transaction (rolled back), or empty. Shown as a red banner.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRenameError))]
        private string renameError = string.Empty;

        /// <summary>True when <see cref="RenameError"/> is shown.</summary>
        public bool HasRenameError => !string.IsNullOrEmpty(RenameError);

        partial void OnRenameScopeChanged(RenameScope value) => RefreshRenamePreview();
        partial void OnRenameFindChanged(string value) => RefreshRenamePreview();
        partial void OnRenameReplaceChanged(string value) => RefreshRenamePreview();
        partial void OnRenameUseRegexChanged(bool value) => RefreshRenamePreview();
        partial void OnRenameMatchCaseChanged(bool value) => RefreshRenamePreview();
        partial void OnRenamePrefixChanged(string value) => RefreshRenamePreview();
        partial void OnRenameSuffixChanged(string value) => RefreshRenamePreview();
        partial void OnRenameCaseChanged(NameCase value) => RefreshRenamePreview();
        partial void OnRenameTrimChanged(bool value) => RefreshRenamePreview();
        partial void OnRenameOnDuplicateChanged(DuplicateNameRule value) => RefreshRenamePreview();

        /// <summary>Rebuilds the preview and logs a summary.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void PreviewRename()
        {
            RefreshRenamePreview();
            Log(LogLevel.Info, $"Rename preview: {RenameWillCount} to rename, {RenameProblemCount} duplicate or invalid, {RenameUnchangedCount} unchanged.");
            foreach (var r in RenameRows.Where(r => r.IsProblem).Take(15))
                Log(LogLevel.Warning, $"'{r.CurrentName}' → '{r.NewName}': {r.StatusText}.");
        }

        /// <summary>Renames the ticked rows that will be renamed.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void RenameChecked() => RunRename(RenameRows.Where(r => r.CanRename && r.IsChecked).ToList(), "checked");

        /// <summary>Renames every row that will be renamed.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void RenameAllValid() => RunRename(RenameRows.Where(r => r.CanRename).ToList(), "valid");

        /// <summary>Hides the rename error banner.</summary>
        [RelayCommand]
        private void DismissRenameError() => RenameError = string.Empty;

        private void RunRename(List<RenameRow> rows, string which)
        {
            HideConfirm();
            RenameError = string.Empty;
            if (RenameRuleError.Length > 0) { Log(LogLevel.Warning, RenameRuleError); return; }
            if (rows.Count == 0)
            {
                Log(LogLevel.Warning, $"No {which} line style to rename. Change the rules, or click Find lines to list styles.");
                return;
            }

            var renames = rows.Select(r => new StyleRename { CategoryId = r.CategoryId, ExpectedName = r.CurrentName, NewName = r.NewName }).ToList();
            Run($"Renaming {renames.Count} line style(s)…", app =>
            {
                var result = new DetailLineActions(app.ActiveUIDocument.Document, Log).RenameStyles(renames);
                if (result.Error != null) _dispatcher.Invoke(() => RenameError = result.Error);
                if (result.Renamed > 0) RefreshAfterChange();
            });
        }

        // Lists the styles of the chosen scope and works out their new names. Runs on the UI thread
        // from the last search's style list; the rename checks everything again in Revit.
        private void RefreshRenamePreview()
        {
            if (_suspendRenamePreview || _loadingSettings) return;

            IEnumerable<LineStyleItem> source = RenameScope switch
            {
                RenameScope.TickedStyles => Styles.Where(s => s.IsChecked),
                RenameScope.AllCustom => Styles.Where(s => !s.IsBuiltIn),
                _ => FoundStyles()
            };
            var styles = source.Where(s => s.CategoryId != null).GroupBy(s => s.CategoryId).Select(g => g.First())
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            var rules = new RenameRules
            {
                Find = RenameFind ?? string.Empty,
                Replace = RenameReplace ?? string.Empty,
                UseRegex = RenameUseRegex,
                MatchCase = RenameMatchCase,
                Prefix = RenamePrefix ?? string.Empty,
                Suffix = RenameSuffix ?? string.Empty,
                Case = RenameCase,
                Trim = RenameTrim
            };
            RenameRuleError = StyleRenamer.Validate(rules) ?? string.Empty;

            var plan = styles.Select(s => new RenamePlanItem { Key = s.CategoryId.Value, CurrentName = s.Name, IsBuiltIn = s.IsBuiltIn }).ToList();
            try
            {
                StyleRenamer.Plan(plan, Styles.Select(s => s.Name), rules, RenameOnDuplicate);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                RenameRuleError = "The rules could not be applied: " + ex.Message;
                foreach (var p in plan) { p.NewName = p.CurrentName; p.Status = RenameStatus.Invalid; p.Reason = RenameRuleError; }
            }

            // Keep the user's ticks for styles still listed.
            var unticked = new HashSet<string>(RenameRows.Where(r => !r.IsChecked).Select(r => r.CurrentName), StringComparer.Ordinal);
            RenameRows.Clear();
            for (int i = 0; i < styles.Count; i++)
            {
                var s = styles[i];
                var p = plan[i];
                RenameRows.Add(new RenameRow
                {
                    CategoryId = s.CategoryId,
                    CurrentName = s.Name,
                    IsBuiltIn = s.IsBuiltIn,
                    CountText = s.CountText,
                    NewName = p.NewName,
                    Status = p.Status,
                    Reason = p.Reason,
                    IsChecked = p.Status == RenameStatus.WillRename && !unticked.Contains(s.Name)
                });
            }

            RenameTotalCount = RenameRows.Count;
            RenameWillCount = RenameRows.Count(r => r.CanRename);
            RenameProblemCount = RenameRows.Count(r => r.IsProblem);
            RenameUnchangedCount = RenameRows.Count(r => r.Status == RenameStatus.Unchanged);
        }

        // Styles used by the lines the grid shows (after filters).
        private IEnumerable<LineStyleItem> FoundStyles()
        {
            var ids = new HashSet<Autodesk.Revit.DB.ElementId>(VisibleRows().Select(r => r.StyleCategoryId).Where(id => id != null));
            return Styles.Where(s => ids.Contains(s.CategoryId));
        }
    }
}
