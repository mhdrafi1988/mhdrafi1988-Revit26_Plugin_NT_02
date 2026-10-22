using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.RoofViewFocus.V003.Core.Models;
using Revit26_Plugin_Revised.RoofViewFocus.V003.Core.Services;
using Revit26_Plugin_Revised.RoofViewFocus.V003.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RoofViewFocus.V003.UI.ViewModels
{
    /// <summary>
    /// Window state for Roof View Focus. No Revit API types here â€” the Revit-side work
    /// is delegated to <see cref="RoofViewFocusHandler"/> via a <see cref="FocusRequest"/>.
    /// </summary>
    public partial class RoofViewFocusViewModel : ObservableObject
    {
        /// <summary>Fixed log folder â€” no folder picker; both auto-save and Export write here silently.</summary>
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", RoofViewFocusDefaults.ToolName, "Logs");

        private readonly string _viewUniqueId;
        private readonly int _ignoredCount;
        private readonly Dispatcher _dispatcher;

        public RoofViewFocusViewModel(
            string viewUniqueId,
            string viewName,
            string viewTypeText,
            string scopeBoxName,
            IReadOnlyList<RoofInfo> roofs,
            int ignoredCount)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _viewUniqueId = viewUniqueId;
            _ignoredCount = ignoredCount;

            // Constructed inside Command.Execute â†’ valid Revit API context for ExternalEvent.Create.
            RoofViewFocusEventManager.Init();

            ViewName = viewName;
            ViewTypeText = viewTypeText;
            Roofs = new ObservableCollection<RoofInfo>(roofs);

            RoofCountText = roofs.Count.ToString(CultureInfo.CurrentCulture);
            ViewStatusText = viewTypeText;
            ScopeBoxText = string.IsNullOrEmpty(scopeBoxName) ? "None" : $"{scopeBoxName}  â†’  None on Run";

            RoofViewFocusSettings settings = RoofViewFocusSettingsService.Load(out string? loadWarning);
            ViewMarginText = FormatMm(settings.ViewMarginMm);
            AnnotationMarginText = FormatMm(settings.AnnotationMarginMm);
            Validate();

            AddLog(LogLevel.Info, $"Tool opened â€” {RoofViewFocusDefaults.Title}");
            AddLog(LogLevel.Info, $"Target view: '{viewName}' ({viewTypeText}), scope box: {(string.IsNullOrEmpty(scopeBoxName) ? "None" : scopeBoxName)}");
            AddLog(LogLevel.Info, $"Roofs selected: {roofs.Count} â€” Ids: {FormatIds(roofs)}");
            if (ignoredCount > 0)
                AddLog(LogLevel.Warning, $"{ignoredCount} non-roof element(s) in the selection were ignored");
            if (loadWarning != null)
                AddLog(LogLevel.Warning, loadWarning);
            AddLog(LogLevel.Info, $"Settings loaded â€” margins {ViewMarginText} / {AnnotationMarginText} mm");
        }

        // â”€â”€ Bindable data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string Title => RoofViewFocusDefaults.Title;
        public string ViewName { get; }
        public string ViewTypeText { get; }
        public string ViewDisplay => $"{ViewName} ({ViewTypeText})";
        public string DefaultOffsetText => $"{RoofViewFocusDefaults.DefaultOffsetMm:0.##} mm (fixed)";

        public ObservableCollection<RoofInfo> Roofs { get; }
        public ObservableCollection<LogEntry> Log { get; } = new();

        // â”€â”€ Metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private string _roofCountText = "0";
        [ObservableProperty] private string _boundingBoxText = "â€”";
        [ObservableProperty] private string _marginText = "â€”";
        [ObservableProperty] private string _viewStatusText = string.Empty;
        [ObservableProperty] private string _scopeBoxText = "None";
        [ObservableProperty] private string _summaryText = string.Empty;

        // â”€â”€ Inputs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private string _viewMarginText = "20";
        [ObservableProperty] private string _annotationMarginText = "20";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _hasInputError;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
        private bool _isBusy;

        partial void OnViewMarginTextChanged(string value) => Validate();
        partial void OnAnnotationMarginTextChanged(string value) => Validate();

        private void Validate()
            => HasInputError = !TryParseMm(ViewMarginText, out _) || !TryParseMm(AnnotationMarginText, out _);

        // â”€â”€ Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool CanRun() => !IsBusy && !HasInputError && Roofs.Count > 0;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            if (!TryParseMm(ViewMarginText, out double viewMm) ||
                !TryParseMm(AnnotationMarginText, out double annMm))
            {
                HasInputError = true;
                return;
            }

            IsBusy = true;
            SummaryText = string.Empty;
            AddLog(LogLevel.Info, $"Run started â€” view '{ViewName}', {Roofs.Count} roof(s)");

            var request = new FocusRequest
            {
                ViewUniqueId = _viewUniqueId,
                RoofUniqueIds = Roofs.Select(r => r.UniqueId).ToList(),
                ViewMarginMm = viewMm,
                AnnotationMarginMm = annMm,
                DefaultOffsetMm = RoofViewFocusDefaults.DefaultOffsetMm,
                OnLog = (level, msg) => PostToUi(() => AddLog(level, msg)),
                OnCompleted = result => PostToUi(() => OnRunCompleted(result))
            };

            try
            {
                if (!RoofViewFocusEventManager.TryRaise(request, out string status))
                {
                    AddLog(LogLevel.Error, $"Revit did not accept the request (status: {status})");
                    IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Could not start the operation: {ex.Message}");
                IsBusy = false;
            }
        }

        private bool CanReset() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanReset))]
        private void Reset()
        {
            ViewMarginText = FormatMm(RoofViewFocusDefaults.DefaultMarginMm);
            AnnotationMarginText = FormatMm(RoofViewFocusDefaults.DefaultMarginMm);
            BoundingBoxText = "â€”";
            MarginText = "â€”";
            ViewStatusText = ViewTypeText;
            SummaryText = string.Empty;
            AddLog(LogLevel.Info, "Inputs reset to defaults");
        }

        [RelayCommand]
        private void CopyAll()
        {
            if (Log.Count == 0) return;
            CopyToClipboard(string.Join(Environment.NewLine, Log.Select(e => e.ToString())));
        }

        [RelayCommand]
        private void CopySelected(IList? selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0) return;
            CopyToClipboard(string.Join(Environment.NewLine,
                selectedItems.Cast<LogEntry>().Select(e => e.ToString())));
        }

        [RelayCommand]
        private void ExportLog()
        {
            if (Log.Count == 0) return;

            try
            {
                string path = RoofViewFocusLogExporter.Write(LogFolder, Log);
                AddLog(LogLevel.Info, $"Log exported: {path}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Log export failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            Log.Clear();
            SummaryText = string.Empty;
        }

        /// <summary>Called by the window on Closing.</summary>
        public void OnWindowClosing() => SaveSettings();

        // â”€â”€ Run completion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnRunCompleted(FocusResult r)
        {
            IsBusy = false;

            if (r.Success)
            {
                BoundingBoxText = string.Format(CultureInfo.CurrentCulture, "{0:N0} Ã— {1:N0}", r.BoxWidthMm, r.BoxHeightMm);
                MarginText = r.ViewMarginTotalMm.ToString("0.##", CultureInfo.CurrentCulture);
                ViewStatusText = "Crop On";
                ScopeBoxText = "None";
            }
            else
            {
                BoundingBoxText = "â€”";
                MarginText = "â€”";
            }

            int skipped = r.Skipped + _ignoredCount;
            string summary = string.Format(CultureInfo.CurrentCulture,
                "{0} roof(s) focused | {1} skipped | {2} failed â€” {3:0.0} s",
                r.Included, skipped, r.Failed, r.Duration.TotalSeconds);

            SummaryText = summary;
            AddLog(r.Failed > 0 ? LogLevel.Error : skipped > 0 ? LogLevel.Warning : LogLevel.Success, summary);

            SaveSettings();
            AutoSaveLog();
        }

        // â”€â”€ Log / settings helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void AddLog(LogLevel level, string message) => Log.Add(new LogEntry(level, message));

        private void PostToUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.BeginInvoke(action);
        }

        private void AutoSaveLog()
        {
            try
            {
                string path = RoofViewFocusLogExporter.Write(LogFolder, Log);
                AddLog(LogLevel.Info, $"Log auto-saved: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Log auto-save failed: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            if (!TryParseMm(ViewMarginText, out double v) || !TryParseMm(AnnotationMarginText, out double a))
                return;

            RoofViewFocusSettingsService.Save(
                new RoofViewFocusSettings { ViewMarginMm = v, AnnotationMarginMm = a },
                out string? warning);
            if (warning != null)
                AddLog(LogLevel.Warning, warning);
        }

        private static void CopyToClipboard(string text)
        {
            try { System.Windows.Clipboard.SetText(text); }
            catch (Exception) { /* clipboard can be locked by another process â€” nothing to recover */ }
        }

        // â”€â”€ Pure helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static bool TryParseMm(string? text, out double mm)
        {
            string t = (text ?? string.Empty).Trim();
            bool ok = double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out mm)
                   || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out mm);
            return ok && !double.IsNaN(mm) && !double.IsInfinity(mm) && mm >= 0;
        }

        private static string FormatMm(double mm) => mm.ToString("0.##", CultureInfo.CurrentCulture);

        private static string FormatIds(IReadOnlyList<RoofInfo> roofs)
        {
            const int max = 50;
            string ids = string.Join(", ", roofs.Take(max).Select(r => r.Id));
            return roofs.Count > max ? $"{ids} â€¦ (+{roofs.Count - max} more)" : ids;
        }
    }
}

