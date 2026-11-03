using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.CreateSections.V013.Models;
using Revit26_Plugin_Revised.CreateSections.V013.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CreateSections.V013.Services
{
    /// <summary>
    /// V009: new. Revit ExternalEvent handler that populates the
    /// "Sections To Create" preview grid.
    ///
    /// Deliberately READ-ONLY — no Transaction of any kind is opened.
    /// Resolves, for each selected detail line: Level (from the plan view
    /// the command was run from — same for every row), the section name
    /// SectionCreationService would generate, the host category that would
    /// be used, and a Status (Ready / No Host Found / Duplicate Name).
    ///
    /// Runs once automatically right after the dialog opens (using the
    /// references already picked by the command), and again whenever the
    /// user clicks Refresh in the grid toolbar — e.g. after changing Host
    /// Source or Geometry Settings, so the preview reflects current inputs.
    ///
    /// Separate ExternalEvent from SectionCreationExternalEvent per the
    /// "shared orchestrator only if multiple distinct Revit-side actions"
    /// rule — scanning/previewing and creating/committing are distinct.
    /// </summary>
    public class SectionPreviewScanHandler : IExternalEventHandler
    {
        private readonly UIDocument _uiDoc;
        private readonly ViewPlan _planView;
        private readonly SectionFromLineViewModel _viewModel;
        private readonly ExternalEvent _externalEvent;

        private IList<Reference> _references;

        public SectionPreviewScanHandler(
            UIDocument uiDoc,
            ViewPlan planView,
            SectionFromLineViewModel viewModel)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _planView = planView ?? throw new ArgumentNullException(nameof(planView));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            _externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>Call to (re-)scan. Safe to call repeatedly (e.g. on Refresh).</summary>
        public void Raise(IList<Reference> references)
        {
            _references = references;
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            Document doc = _uiDoc.Document;

            if (_references == null || _references.Count == 0)
            {
                _viewModel.PreviewRows.Clear();
                return;
            }

            try
            {
                var hostSource = new HostSourceSelection
                {
                    FloorHost = _viewModel.SearchFloorHost,
                    RoofHost = _viewModel.SearchRoofHost,
                    FloorLinked = _viewModel.SearchFloorLinked,
                    RoofLinked = _viewModel.SearchRoofLinked
                };

                var orientSvc = new SectionOrientationService();
                var hostSvc = new HostElementSearchService(doc);
                var naming = new SectionNamingService(doc);

                string levelName = _planView?.GenLevel != null
                    ? _planView.GenLevel.Name
                    : "—";

                var rows = new ObservableCollection<SectionPreviewRow>();
                var namesSeenThisScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in _references)
                {
                    if (doc.GetElement(r) is not DetailLine dl ||
                        dl.GeometryCurve is not Line line)
                        continue;

                    var orient = orientSvc.Calculate(line);
                    if (!orient.Success)
                        continue;

                    var candidates = hostSvc.FindCandidates(
                        orient.MidPoint,
                        orient.MidPoint.Z,
                        _viewModel.SearchThresholdMm,
                        hostSource);

                    var host = candidates.Count > 0 ? candidates[0] : null;

                    // Preview-only name resolution. Uses the same
                    // SectionNamingService.Generate as the real Create run
                    // would — including its own _existingNames tracking,
                    // so duplicate detection across THIS scan's rows works
                    // the same way live duplicate detection would during
                    // an actual Create run.
                    string previewName = naming.Generate(
                        _planView,
                        _viewModel.SectionPrefix,
                        dl.Id,
                        out bool wouldRename);

                    SectionPreviewStatus status;
                    string statusDisplay;
                    bool includedByDefault = true;

                    if (host == null && !_viewModel.UseThresholdFallback)
                    {
                        status = SectionPreviewStatus.NoHostFound;
                        statusDisplay = "No Host Found";
                    }
                    else if (wouldRename)
                    {
                        status = SectionPreviewStatus.DuplicateName;
                        statusDisplay = "Duplicate Name";
                        // Confirmed default: duplicate-flagged rows start
                        // unchecked so the user consciously re-includes them.
                        includedByDefault = false;
                    }
                    else if (host == null)
                    {
                        // No host, but threshold fallback is enabled — still Ready.
                        status = SectionPreviewStatus.Ready;
                        statusDisplay = "Ready";
                    }
                    else
                    {
                        status = SectionPreviewStatus.Ready;
                        statusDisplay = "Ready";
                    }

                    string hostTypeDisplay = host == null
                        ? "—"
                        : DescribeHostType(host);

                    rows.Add(new SectionPreviewRow
                    {
                        SourceLineId = dl.Id,
                        SourceReference = r,
                        Level = levelName,
                        SectionName = previewName,
                        HostTypeDisplay = hostTypeDisplay,
                        Status = status,
                        StatusDisplay = statusDisplay,
                        IsIncluded = includedByDefault
                    });
                }

                _viewModel.PreviewRows.Clear();
                foreach (var row in rows)
                    _viewModel.PreviewRows.Add(row);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Create Sections From Detail Lines — Preview",
                    $"Failed to build preview: {ex.Message}");
            }
        }

        /// <summary>
        /// V009: Host/Linked + Floor/Roof description for the grid, read
        /// directly from CandidateHostElement.SourceCategory — set by
        /// HostElementSearchService at collection time, not re-derived.
        /// </summary>
        private static string DescribeHostType(CandidateHostElement host)
            => host.SourceCategory switch
            {
                HostSourceCategory.FloorHost   => "Floor (Host)",
                HostSourceCategory.RoofHost    => "Roof (Host)",
                HostSourceCategory.FloorLinked => "Floor (Linked)",
                HostSourceCategory.RoofLinked  => "Roof (Linked)",
                _                              => "—"
            };

        public string GetName() => "Create Sections From Detail Lines — Preview Scan";
    }
}
