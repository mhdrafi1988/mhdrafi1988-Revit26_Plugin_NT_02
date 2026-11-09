// Split Roof V001 — view model: Check (preview the pieces) and Split (create them).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Services;
using Revit26_Plugin_Revised.SplitRoof.V001.Infrastructure.Helpers;

namespace Revit26_Plugin_Revised.SplitRoof.V001.UI.ViewModels
{
    /// <summary>Drives the Split Roof window.</summary>
    public partial class SplitRoofViewModel : ObservableObject
    {
        private const double SqFtPerSqM = 10.7639104167;
        private readonly UIDocument _uidoc;
        private readonly FootPrintRoof _roof;
        private readonly RoofFootprint _footprint;
        private readonly IReadOnlyList<CutLine> _lines;
        private SplitResult? _result;

        /// <summary>Creates the view model for the roof and cut lines.</summary>
        public SplitRoofViewModel(UIDocument uidoc, FootPrintRoof roof, IReadOnlyList<CutLine> lines)
        {
            _uidoc = uidoc;
            _roof = roof;
            _lines = lines;
            _footprint = FootprintReader.Read(roof);
            _deleteOriginal = SplitSettingsStore.Load().DeleteOriginal;
            RoofLabel = $"Roof {roof.Id}  —  {roof.Name}";
            foreach (var l in lines) Lines.Add($"Line {l.Id}");
            Notes.Add("Each line is extended past its ends and cuts the whole roof. Shape-editing points and sub-elements are not copied to the pieces.");
            Status = "Press Check to preview the pieces.";
        }

        /// <summary>Window title.</summary>
        public string Title => ToolCatalog.SplitRoof.Title;
        /// <summary>The roof being split.</summary>
        public string RoofLabel { get; }
        /// <summary>The cut lines.</summary>
        public ObservableCollection<string> Lines { get; } = new();
        /// <summary>The pieces found by Check.</summary>
        public ObservableCollection<string> Pieces { get; } = new();
        /// <summary>Informational notes and warnings.</summary>
        public ObservableCollection<string> Notes { get; } = new();

        [ObservableProperty] private bool _deleteOriginal;
        [ObservableProperty] private string _status = "";
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))] private bool _checked;
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SplitCommand))] private bool _done;

        /// <summary>Previews the pieces the lines produce.</summary>
        [RelayCommand]
        private void Check()
        {
            _result = RoofSplitter.Split(_footprint, _lines);
            Pieces.Clear();
            int n = 1;
            foreach (var p in _result.Pieces) Pieces.Add($"Piece {n++}  —  {p.AreaFeet / SqFtPerSqM:0.00} m²{(p.HasSlope ? "" : "  (flat)")}");
            Notes.Clear();
            foreach (var note in _result.Notes) Notes.Add(note);
            Status = _result.CanCreate
                ? $"The roof will be split into {_result.Pieces.Count} pieces. Ready to split."
                : "The lines do not divide the roof. Move or add a line that crosses it.";
            Checked = _result.CanCreate;
            Done = false;
        }

        /// <summary>Creates the pieces.</summary>
        [RelayCommand(CanExecute = nameof(CanSplit))]
        private void Split()
        {
            SplitSettingsStore.Save(new SplitSettings { DeleteOriginal = DeleteOriginal });
            try
            {
                var (roofs, usedArcs) = RoofBuilder.Create(_uidoc.Document, _roof, _result!.Pieces, DeleteOriginal);
                Status = $"Created {roofs.Count} roofs ({(usedArcs ? "arcs kept" : "arcs flattened to segments")}). " +
                         (DeleteOriginal ? "The original roof was deleted." : "The original roof was kept: hide or delete it yourself.");
                Done = true;
            }
            catch (Exception ex)
            {
                Status = "Nothing was changed. Revit could not create the roofs: " + ex.Message;
            }
        }

        private bool CanSplit() => Checked && !Done;
    }
}
