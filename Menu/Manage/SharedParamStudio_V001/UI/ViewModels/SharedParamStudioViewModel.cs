using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Shared Parameter Studio window: four tabs (Create &amp; Assign, Merge Files,
    /// Duplicate Check, Retrieve from Project) sharing one activity log. File-only tabs run directly;
    /// tabs that touch the project queue a job on the Revit API thread.
    /// </summary>
    public sealed partial class SharedParamStudioViewModel : ToolViewModelBase
    {
        private const string FileFilter = "Shared parameter files (*.txt)|*.txt|All files (*.*)|*.*";

        private readonly StudioExternalEventHandler _handler;
        private readonly ExternalEvent _event;

        [ObservableProperty] private int _selectedTab;

        /// <summary>Creates the view model; <paramref name="categories"/> come from the project open at start.</summary>
        public SharedParamStudioViewModel(StudioExternalEventHandler handler, ExternalEvent externalEvent, IEnumerable<CategoryItem> categories, IEnumerable<string> projectParameterNames = null)
        {
            foreach (var n in projectParameterNames ?? Enumerable.Empty<string>()) _projectNames.Add(n);
            _handler = handler;
            _event = externalEvent;

            foreach (var c in categories) Categories.Add(c);
            CategoriesView = CollectionViewSource.GetDefaultView(Categories);
            CategoriesView.Filter = o => o is CategoryItem c &&
                (string.IsNullOrWhiteSpace(CategoryFilter) || c.Name.IndexOf(CategoryFilter.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);

            SelectedDataType = DataTypeOptions[0];
            SelectedPaletteGroup = PaletteGroups[0];
            SelectedPolicy = PolicyOptions[0];

            var s = SettingsService<SharedParamStudioSettings>.Load(ToolCatalog.SharedParamStudio.Name);
            SelectedTab = Math.Clamp(s.LastTab, 0, 3);
            CreateFilePath = s.CreateFilePath;
            CreateFileGroup = s.CreateFileGroup;
            MergeFirstPath = s.MergeFirstPath;
            MergeSecondPath = s.MergeSecondPath;
            MergeOutputPath = s.MergeOutputPath;
            DuplicatePath = s.DuplicatePath;
            RetrieveFilePath = s.RetrieveFilePath;
            RetrieveFileGroup = string.IsNullOrWhiteSpace(s.RetrieveFileGroup) ? "Project parameters" : s.RetrieveFileGroup;

            InitRetrievedView();
            InitDuplicateView();

            SummaryText = "Ready.";
            AddLog(LogLevel.Info, $"{ToolCatalog.SharedParamStudio.Name} ready.");
        }

        /// <summary>Saves the window state; called when the window closes.</summary>
        public void OnWindowClosing()
        {
            SettingsService<SharedParamStudioSettings>.Save(ToolCatalog.SharedParamStudio.Name, new SharedParamStudioSettings
            {
                LastTab = SelectedTab,
                CreateFilePath = CreateFilePath ?? string.Empty,
                CreateFileGroup = CreateFileGroup ?? string.Empty,
                MergeFirstPath = MergeFirstPath ?? string.Empty,
                MergeSecondPath = MergeSecondPath ?? string.Empty,
                MergeOutputPath = MergeOutputPath ?? string.Empty,
                DuplicatePath = DuplicatePath ?? string.Empty,
                RetrieveFilePath = RetrieveFilePath ?? string.Empty,
                RetrieveFileGroup = RetrieveFileGroup ?? string.Empty
            });
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private void Log(LogLevel level, string message)
        {
            AddLog(level, message);
            SummaryText = message;
        }

        private void LogAll(IEnumerable<JobMessage> messages)
        {
            foreach (var m in messages) Log(m.Level, m.Text);
        }

        private static bool Confirm(string text)
        {
            var td = new TaskDialog(ToolCatalog.SharedParamStudio.Title)
            {
                MainContent = text,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            return td.Show() == TaskDialogResult.Yes;
        }

        private static string PickOpen(string current)
        {
            var dlg = new OpenFileDialog { Title = "Select shared parameter file", Filter = FileFilter, CheckFileExists = true };
            TrySetFolder(dlg, current);
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static string PickSave(string current, string title)
        {
            var dlg = new SaveFileDialog { Title = title, Filter = FileFilter, OverwritePrompt = false };
            TrySetFolder(dlg, current);
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static void TrySetFolder(Microsoft.Win32.FileDialog dlg, string current)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    string dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                }
            }
            catch { /* a bad remembered path just means the dialog opens at its default folder */ }
        }

        /// <summary>Queues <paramref name="job"/> on the Revit API thread; <paramref name="after"/> runs there when it finishes.</summary>
        private void RunJob(Action<Document> job, Action after)
        {
            if (IsRunning)
            {
                Log(LogLevel.Warning, "Another job is still running.");
                return;
            }

            IsRunning = true;
            _handler.Job = job;
            _handler.Completed = failure =>
            {
                IsRunning = false;
                if (failure != null)
                {
                    Log(LogLevel.Error, $"Failed — {failure.Message}");
                    ToolGuard.Report(GetType(), failure);
                    return;
                }
                after?.Invoke();
            };

            var request = _event.Raise();
            if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
            {
                IsRunning = false;
                Log(LogLevel.Error, "Revit is not ready to run the job (a command or dialog is open). Try again.");
            }
        }

        private static string Csv(string s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
