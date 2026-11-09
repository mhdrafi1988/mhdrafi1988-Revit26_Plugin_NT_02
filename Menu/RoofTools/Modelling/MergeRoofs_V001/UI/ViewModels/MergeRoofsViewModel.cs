// Merge Roofs V001 — view model: Check (analyse) and Create (build the roof, or offer detail lines / circles).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;
using Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.UI.ViewModels
{
    /// <summary>Drives the Merge Roofs window.</summary>
    public partial class MergeRoofsViewModel : ObservableObject
    {
        private readonly UIDocument _uidoc;
        private readonly IReadOnlyList<FootPrintRoof> _roofs;
        private readonly IReadOnlyList<RoofFootprint> _footprints;
        private MergeResult? _result;

        /// <summary>Creates the view model for the selected roofs.</summary>
        public MergeRoofsViewModel(UIDocument uidoc, IReadOnlyList<FootPrintRoof> roofs)
        {
            _uidoc = uidoc;
            _roofs = roofs;
            _footprints = roofs.Select(FootprintReader.Read).ToList();
            var s = MergeSettingsStore.Load();
            _toleranceMm = s.ToleranceMm;
            _placeGapLinks = s.PlaceGapLinks;
            foreach (var r in roofs) Roofs.Add($"Roof {r.Id}  —  {r.Name}");
            var first = roofs[0];
            if (roofs.Any(r => r.GetTypeId() != first.GetTypeId() || r.LevelId != first.LevelId))
                Notes.Add("Roofs differ in type or level: the new roof uses the first selected roof's type and level.");
            Status = "Set the gap tolerance, then press Check.";
        }

        /// <summary>Window title.</summary>
        public string Title => ToolCatalog.MergeRoofs.Title;
        /// <summary>The selected roofs.</summary>
        public ObservableCollection<string> Roofs { get; } = new();
        /// <summary>Informational notes.</summary>
        public ObservableCollection<string> Notes { get; } = new();
        /// <summary>Problems found by Check.</summary>
        public ObservableCollection<string> Issues { get; } = new();

        [ObservableProperty] private double _toleranceMm;
        [ObservableProperty] private bool _placeGapLinks;
        [ObservableProperty] private string _status = "";
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CreateCommand))] private bool _checked;
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CreateCommand))] private bool _created;

        /// <summary>Analyses the roofs with the current tolerance.</summary>
        [RelayCommand]
        private void Check()
        {
            if (!(ToleranceMm >= 0 && ToleranceMm <= 1000)) { Status = "Gap tolerance must be between 0 and 1000 mm."; return; }
            _result = MergeAnalyzer.Analyze(_footprints, ToleranceMm / MergeSettings.MmPerFoot);
            Issues.Clear();
            foreach (var i in _result.Issues) Issues.Add($"{i.Kind}: {i.Message}");
            Status = _result.CanCreate
                ? (_result.Issues.Count == 0 ? "The roofs form one outline. Ready to create." : "The roofs form one outline (see issues). Ready to create.")
                : "The roofs do not form one closed outline. Create will offer to mark the problem.";
            Checked = true;
            Created = false;
        }

        /// <summary>Creates the merged roof, or runs the failure prompts.</summary>
        [RelayCommand(CanExecute = nameof(CanCreateRoof))]
        private void Create()
        {
            var result = _result!;
            SaveSettings();
            if (!result.CanCreate) { OfferMarkup(result, null); return; }
            try
            {
                var (roof, usedArcs) = RoofBuilder.Create(_uidoc.Document, _roofs[0], result, _footprints, ToleranceMm / MergeSettings.MmPerFoot);
                Status = $"Created roof {roof.Id} ({(usedArcs ? "arcs kept" : "arcs flattened to segments")}). The original roofs were kept.";
                Created = true;
                if (PlaceGapLinks && result.GapLinks.Count > 0) DrawGapLinks(result);
            }
            catch (Exception ex)
            {
                OfferMarkup(result, ex.Message);
            }
        }

        private bool CanCreateRoof() => Checked && !Created;

        private void DrawGapLinks(MergeResult result)
        {
            try { DetailDrawer.DrawGapLinks(_uidoc.Document, _uidoc.ActiveGraphicalView, result.GapLinks); }
            catch (Exception ex) { Status += "  Gap links not placed: " + ex.Message; }
        }

        private void OfferMarkup(MergeResult result, string? revitError)
        {
            Status = revitError == null ? "The roof was not created: the roofs are not joined." : "The roof was not created: " + revitError;
            View view = _uidoc.ActiveGraphicalView;
            if (Ask("Place the profile as detail lines in the current view?",
                    "The merged outline is drawn so you can inspect it. No roof is created."))
                Try(() => DetailDrawer.DrawProfile(_uidoc.Document, view, result), "Profile lines");
            var located = result.Issues.Where(i => i.Kind != IssueKind.Gap).ToList();
            if (located.Count > 0 && Ask("Mark issue areas with circles?", $"{located.Count} circle(s) will be drawn at the problem areas."))
                Try(() => DetailDrawer.DrawCircles(_uidoc.Document, view, located), "Circles");
        }

        private void Try(Action draw, string what)
        {
            try { draw(); Status += $"  {what} placed."; }
            catch (Exception ex) { Status += $"  {what} not placed: {ex.Message}"; }
        }

        private static bool Ask(string title, string detail)
        {
            var d = new TaskDialog(ToolCatalog.MergeRoofs.Name)
            {
                MainInstruction = title,
                MainContent = detail,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            return d.Show() == TaskDialogResult.Yes;
        }

        private void SaveSettings() =>
            MergeSettingsStore.Save(new MergeSettings { ToleranceMm = ToleranceMm, PlaceGapLinks = PlaceGapLinks });
    }
}
