using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Run;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels
{
    /// <summary>
    /// The run: turning what the window shows into one queued request, and reading the report
    /// back into the list and the log.
    /// </summary>
    public partial class ViewModel_SectionRoofDetailLines
    {
        /// <summary>
        /// Draws the lines in every ticked section, in one Undo step. Refuses, with a line in the
        /// log, when there is nothing ticked to draw from or in.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCreate))]
        private void Create()
        {
            List<SectionRowViewModel> sections = Sections.Where(s => s.IsTicked && s.CanDraw).ToList();
            if (sections.Count == 0)
            {
                AddLog(LogLevel.Warning, "Tick at least one section to draw in.");
                return;
            }

            HashSet<TypeKey> roofTypes = TickedTypes(ElementRole.Roof);
            HashSet<TypeKey> wallTypes = TickedTypes(ElementRole.Wall);

            if (roofTypes.Count == 0)
            {
                AddLog(LogLevel.Warning, DrawWallLines
                    ? "Tick at least one roof type. Wall lines are placed where a wall meets a ticked roof, so a roof is needed either way."
                    : "Tick at least one roof type to trace.");
                return;
            }
            if (DrawWallLines && wallTypes.Count == 0)
                AddLog(LogLevel.Info, "No wall type is ticked, so only the roof profiles are drawn.");

            if (sections.Count > MaxSectionsPerRun)
                AddLog(LogLevel.Warning,
                    $"{sections.Count} sections are ticked, more than the {MaxSectionsPerRun} one run is meant to cover. "
                    + "The run goes ahead as ticked; it will take longer.");

            var selected = new HashSet<TypeKey>(roofTypes);
            foreach (TypeKey key in wallTypes) selected.Add(key);

            var request = new RunRequest
            {
                ViewKeys = sections.Select(s => s.ViewKey).ToList(),
                SelectedTypes = selected,
                Settings = CurrentSettings()
            };

            AddLog(LogLevel.Info,
                $"Drawing in {sections.Count} section(s) from {roofTypes.Count} roof type(s)"
                + (DrawWallLines ? $" and {wallTypes.Count} wall type(s)" : string.Empty)
                + $"; adjacency tolerance {AdjacencyToleranceMm:F0} mm.");
            if (DrawRoofProfile)
                AddLog(LogLevel.Info, "Roof edges: " + RoofSidesNote);
            if (DrawWallLines)
                AddLog(LogLevel.Info, "Wall face: " + WallFaceText(WallFace) + ".");

            Queue($"Drawing lines in {sections.Count} section(s)…", app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (!CheckDocument(doc)) return;

                RunReport report = new SectionLineRunner().Run(doc, request);
                ReadReport(report, request);
            });
        }

        /// <summary>How a wall-face choice reads in the log.</summary>
        private static string WallFaceText(WallFaceChoice choice) => choice switch
        {
            WallFaceChoice.BothFaces => "every face of the wall's cut",
            WallFaceChoice.LeftOnly => "the leftmost face across the view",
            WallFaceChoice.RightOnly => "the rightmost face across the view",
            _ => "the face nearer the roof"
        };

        /// <summary>True when there is a section ticked and a roof type ticked to draw from.</summary>
        private bool CanCreate()
            => !IsRunning
               && Sections.Any(s => s.IsTicked && s.CanDraw)
               && TickedTypes(ElementRole.Roof).Count > 0;

        private HashSet<TypeKey> TickedTypes(ElementRole role)
            => new(Sources
                .SelectMany(s => s.Categories)
                .SelectMany(c => c.Types)
                .Where(t => t.IsChecked && t.Role == role)
                .Select(t => t.Key));

        /// <summary>The window's current choices, as the settings the run reads.</summary>
        private SectionRoofLinesSettings CurrentSettings() => new SectionRoofLinesSettings
        {
            RoofLineStyleName = RoofLineStyleName ?? string.Empty,
            WallLineStyleName = WallLineStyleName ?? string.Empty,
            DrawRoofProfile = DrawRoofProfile,
            RoofTop = RoofTop,
            RoofBottom = RoofBottom,
            RoofLeft = RoofLeft,
            RoofRight = RoofRight,
            DrawWallLines = DrawWallLines,
            AdjacencyToleranceMm = AdjacencyToleranceMm,
            WallFace = WallFace,
            SkipExistingLines = SkipExistingLines,
            ShowAllCategories = ShowAllCategories
        }.Normalize();

        private void ReadReport(RunReport report, RunRequest request)
        {
            foreach (LogEntry entry in report.Details) AddLog(entry.Level, entry.Message);

            foreach (SectionRowViewModel row in Sections)
            {
                if (!request.ViewKeys.Contains(row.ViewKey)) continue;
                row.LinesDrawn = report.LinesByView.TryGetValue(row.ViewKey, out int lines) ? lines : 0;
            }

            LinesCreatedText = report.LinesCreated.ToString();

            AddLog(report.LinesCreated > 0 ? LogLevel.Success : LogLevel.Warning,
                $"Done — {report.LinesCreated} detail line(s) in {report.SectionsDone} section(s), "
                + $"from {report.RoofsDrawn} roof cut(s) and {report.WallsDrawn} wall(s)"
                + (report.LinesSkippedExisting > 0 ? $"; {report.LinesSkippedExisting} were already in the views" : string.Empty)
                + (report.ElementsSkipped > 0 ? $"; {report.ElementsSkipped} element(s) had nothing to trace" : string.Empty)
                + ". One Ctrl+Z undoes the whole run.");

            SaveSettings();
            RefreshCards();
        }

        /// <summary>Writes "Cancelled by user" and clears the per-section figures.</summary>
        protected override void OnClearLog()
        {
            base.OnClearLog();
            foreach (SectionRowViewModel row in Sections) row.LinesDrawn = null;
            LinesCreatedText = "—";
        }
    }
}
