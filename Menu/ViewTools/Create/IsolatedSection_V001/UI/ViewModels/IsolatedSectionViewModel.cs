using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Services;
using Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.UI.ViewModels
{
    /// <summary>
    /// View model of the Isolated Section window. Holds the selection (host or linked elements), the
    /// options and a plan preview of the alignment. Every model read and write after the window opens
    /// runs through a <see cref="RevitJobQueue"/>, so it happens in a Revit API context.
    /// </summary>
    public partial class IsolatedSectionViewModel : ToolViewModelBase
    {
        private const double MmPerFoot = 304.8;
        private const double PreviewWidth = 300, PreviewHeight = 170, PreviewPad = 14;

        private readonly Document _doc;
        private readonly RevitJobQueue _jobs;
        private readonly Dispatcher _dispatcher;
        private readonly List<PickedElement> _elements = new();
        private Vec2? _pickedFaceNormal;

        /// <summary>Hides the window while Revit shows a pick prompt. Set by the window.</summary>
        public Action HideWindow { get; set; }

        /// <summary>Shows the window again after a pick prompt. Set by the window.</summary>
        public Action ShowWindow { get; set; }

        /// <summary>
        /// Creates the view model for <paramref name="uidoc"/> with the elements selected when the tool
        /// started. Must be created in a Revit API context (while the command runs): it reads the model
        /// and creates the external event.
        /// </summary>
        public IsolatedSectionViewModel(UIDocument uidoc, ReadResult initial)
        {
            if (uidoc == null) throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _jobs = new RevitJobQueue("Isolated Section V001");
            _jobs.BusyChanged += (_, _) => OnUi(() =>
            {
                IsRunning = _jobs.IsBusy;
                RefreshCommands();
            });

            SideOptions = new ObservableCollection<Choice<SideChoice>>
            {
                new(SideChoice.Auto, "Largest side (auto)"),
                new(SideChoice.LongA, "Long side A"),
                new(SideChoice.LongB, "Long side B (back)"),
                new(SideChoice.ShortC, "Short side C"),
                new(SideChoice.ShortD, "Short side D"),
                new(SideChoice.PickedFace, "Picked face (use Pick face…)"),
            };

            // Safe here: the command is still running, so this is a Revit API context.
            ViewTypes = new ObservableCollection<NamedId>(IsolatedViewBuilder.ViewTypes(_doc));
            Templates = new ObservableCollection<NamedId>(IsolatedViewBuilder.Templates(_doc));

            IsolatedSectionSettings settings = IsolatedSectionSettingsService.Load(out string warning);
            ApplySettings(settings);

            AddLog(LogLevel.Info, $"Model '{_doc.Title}': {ViewTypes.Count} 3D view type(s), {Templates.Count - 1} 3D view template(s).");
            if (warning != null) AddLog(LogLevel.Warning, warning);
            if (ViewTypes.Count == 0) AddLog(LogLevel.Error, "The model has no 3D view type, so no 3D view can be created.");

            Merge(initial, "Selection");
            if (_elements.Count == 0)
                AddLog(LogLevel.Info, "Nothing usable was selected. Use Pick elements… or Pick in link… to choose elements.");
        }

        // ── Lists and options ─────────────────────────────────────────────────

        /// <summary>The selection, one row per element.</summary>
        public ObservableCollection<ElementRow> Rows { get; } = new();

        /// <summary>Side options; labels name the compass direction of each side.</summary>
        public ObservableCollection<Choice<SideChoice>> SideOptions { get; }

        /// <summary>Isolation options.</summary>
        public IReadOnlyList<Choice<IsolationMode>> IsolationOptions { get; } = new[]
        {
            new Choice<IsolationMode>(IsolationMode.Temporary, "Temporary isolate (reset when the view closes)"),
            new Choice<IsolationMode>(IsolationMode.Permanent, "Hide everything else (permanent)"),
            new Choice<IsolationMode>(IsolationMode.None, "None — section box only"),
        };

        /// <summary>Camera options.</summary>
        public IReadOnlyList<Choice<CameraMode>> CameraOptions { get; } = new[]
        {
            new Choice<CameraMode>(CameraMode.FacingSide, "Facing the side (level)"),
            new Choice<CameraMode>(CameraMode.Angled, "Angled 3D (30° right, 35° above)"),
        };

        /// <summary>Detail level options.</summary>
        public IReadOnlyList<Choice<DetailChoice>> DetailOptions { get; } = new[]
        {
            new Choice<DetailChoice>(DetailChoice.Coarse, "Coarse"),
            new Choice<DetailChoice>(DetailChoice.Medium, "Medium"),
            new Choice<DetailChoice>(DetailChoice.Fine, "Fine"),
        };

        /// <summary>Visual style options.</summary>
        public IReadOnlyList<Choice<StyleChoice>> StyleOptions { get; } = new[]
        {
            new Choice<StyleChoice>(StyleChoice.HiddenLine, "Hidden Line"),
            new Choice<StyleChoice>(StyleChoice.Shaded, "Shaded"),
            new Choice<StyleChoice>(StyleChoice.ConsistentColors, "Consistent Colors"),
            new Choice<StyleChoice>(StyleChoice.Realistic, "Realistic"),
        };

        /// <summary>The model's 3D view types.</summary>
        public ObservableCollection<NamedId> ViewTypes { get; }

        /// <summary>"&lt;None&gt;" and the model's 3D view templates.</summary>
        public ObservableCollection<NamedId> Templates { get; }

        [ObservableProperty] private Choice<SideChoice> _selectedSide;
        [ObservableProperty] private Choice<IsolationMode> _selectedIsolation;
        [ObservableProperty] private Choice<CameraMode> _selectedCamera;
        [ObservableProperty] private Choice<DetailChoice> _selectedDetail;
        [ObservableProperty] private Choice<StyleChoice> _selectedStyle;
        [ObservableProperty] private NamedId _selectedViewType;
        [ObservableProperty] private NamedId _selectedTemplate;
        [ObservableProperty] private ElementRow _selectedRow;
        [ObservableProperty] private string _sideMarginText = "300";
        [ObservableProperty] private string _depthMarginText = "300";
        [ObservableProperty] private string _topBottomMarginText = "300";
        [ObservableProperty] private string _namePattern = string.Empty;
        [ObservableProperty] private bool _openView = true;

        /// <summary>True while a margin is not a number from 0 to 100 000 mm.</summary>
        [ObservableProperty] private bool _hasInputError;

        /// <summary>True when a template is chosen: detail level and visual style come from it.</summary>
        public bool TemplateControlsLook => SelectedTemplate != null && SelectedTemplate.Id >= 0;

        // ── Header metrics ───────────────────────────────────────────────────

        [ObservableProperty] private string _elementCountText = "0";
        [ObservableProperty] private string _largestSideText = "—";
        [ObservableProperty] private string _lookingAtText = "—";
        [ObservableProperty] private string _boxText = "—";

        /// <summary>One line under the plan preview explaining the alignment.</summary>
        [ObservableProperty] private string _alignmentText = "Select elements to see the alignment.";

        // ── Plan preview (canvas coordinates) ────────────────────────────────

        /// <summary>Outlines of every element.</summary>
        [ObservableProperty] private Geometry _previewElements = Geometry.Empty;

        /// <summary>The section box in plan.</summary>
        [ObservableProperty] private Geometry _previewBox = Geometry.Empty;

        /// <summary>The side the view looks at.</summary>
        [ObservableProperty] private Geometry _previewSide = Geometry.Empty;

        /// <summary>Arrow showing the view direction.</summary>
        [ObservableProperty] private Geometry _previewArrow = Geometry.Empty;

        /// <summary>Width of the preview canvas.</summary>
        public double PreviewCanvasWidth => PreviewWidth;

        /// <summary>Height of the preview canvas.</summary>
        public double PreviewCanvasHeight => PreviewHeight;

        partial void OnSelectedSideChanged(Choice<SideChoice> value) => Refresh();
        partial void OnSelectedCameraChanged(Choice<CameraMode> value) => Refresh();
        partial void OnSideMarginTextChanged(string value) => Refresh();
        partial void OnDepthMarginTextChanged(string value) => Refresh();
        partial void OnTopBottomMarginTextChanged(string value) => Refresh();
        partial void OnSelectedTemplateChanged(NamedId value) => OnPropertyChanged(nameof(TemplateControlsLook));
        partial void OnSelectedRowChanged(ElementRow value) => RemoveSelectedCommand.NotifyCanExecuteChanged();

        // ── Commands ─────────────────────────────────────────────────────────

        /// <summary>Prompts for host-model elements and adds them.</summary>
        [RelayCommand(CanExecute = nameof(CanUseRevit))]
        private void PickElements() => RunPick("Pick elements", SelectionService.PickHostElements);

        /// <summary>Prompts for elements inside linked models and adds them.</summary>
        [RelayCommand(CanExecute = nameof(CanUseRevit))]
        private void PickLinked() => RunPick("Pick in link", SelectionService.PickLinkedElements);

        /// <summary>Prompts for a face; the view will look at it.</summary>
        [RelayCommand(CanExecute = nameof(CanUseRevit))]
        private void PickFace()
        {
            HideWindow?.Invoke();
            Queue(uidoc =>
            {
                try
                {
                    PickedFace face = SelectionService.PickFace(uidoc, out string problem);
                    OnUi(() =>
                    {
                        if (face == null)
                        {
                            AddLog(LogLevel.Warning, problem);
                            SummaryText = problem;
                            return;
                        }
                        _pickedFaceNormal = face.PlanNormal;
                        var result = new ReadResult();
                        result.Elements.Add(face.Element);
                        Merge(result, "Pick face");
                        AddLog(LogLevel.Info, $"Face picked on {face.Element.Category} {face.Element.ElementId}: faces {PlanSides.CompassName(face.PlanNormal)}.");
                        SelectedSide = SideOptions.First(o => o.Value == SideChoice.PickedFace);
                    });
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    OnUi(() => AddLog(LogLevel.Info, "Pick face cancelled."));
                }
                finally
                {
                    OnUi(() => ShowWindow?.Invoke());
                }
            }, onDenied: () => ShowWindow?.Invoke());
        }

        /// <summary>Removes the selected row.</summary>
        [RelayCommand(CanExecute = nameof(CanRemove))]
        private void RemoveSelected()
        {
            if (SelectedRow == null) return;
            _elements.RemoveAll(e => e.SameAs(SelectedRow.Element));
            AddLog(LogLevel.Info, $"Removed {SelectedRow.Element.Category} {SelectedRow.Id}.");
            Refresh();
        }

        private bool CanRemove() => !IsRunning && SelectedRow != null;

        /// <summary>Empties the selection.</summary>
        [RelayCommand(CanExecute = nameof(CanClear))]
        private void ClearElements()
        {
            _elements.Clear();
            _pickedFaceNormal = null;
            AddLog(LogLevel.Info, "Selection cleared.");
            Refresh();
        }

        private bool CanClear() => !IsRunning && _elements.Count > 0;

        /// <summary>Puts every option back to its default.</summary>
        [RelayCommand(CanExecute = nameof(CanUseRevit))]
        private void Reset()
        {
            ApplySettings(new IsolatedSectionSettings());
            AddLog(LogLevel.Info, "Options reset to defaults.");
        }

        /// <summary>Creates the isolated 3D view.</summary>
        [RelayCommand(CanExecute = nameof(CanCreate))]
        private void Create()
        {
            if (!TryReadMargins(out double side, out double depth, out double topBottom))
            {
                AddLog(LogLevel.Error, "Margins must be numbers from 0 to 100 000 mm.");
                return;
            }
            if (SelectedSide?.Value == SideChoice.PickedFace && _pickedFaceNormal == null)
            {
                AddLog(LogLevel.Warning, "Pick a face first (Pick face…), or choose another side.");
                return;
            }

            var request = new CreateRequest
            {
                Elements = _elements.ToList(),
                Side = SelectedSide?.Value ?? SideChoice.Auto,
                PickedFaceNormal = _pickedFaceNormal,
                Isolation = SelectedIsolation?.Value ?? IsolationMode.Temporary,
                Camera = SelectedCamera?.Value ?? CameraMode.FacingSide,
                Detail = SelectedDetail?.Value ?? DetailChoice.Fine,
                Style = SelectedStyle?.Value ?? StyleChoice.Shaded,
                SideMarginMm = side,
                DepthMarginMm = depth,
                TopBottomMarginMm = topBottom,
                NamePattern = NamePattern,
                ViewTypeId = SelectedViewType?.Id ?? -1,
                TemplateId = SelectedTemplate?.Id ?? -1,
                OpenView = OpenView,
            };
            SaveSettings();
            AddLog(LogLevel.Info, $"Creating the 3D view for {request.Elements.Count} element(s)…");

            Queue(uidoc =>
            {
                CreateResult result = IsolatedViewBuilder.Build(uidoc, request, msg => OnUi(() => AddLog(LogLevel.Info, msg)));
                OnUi(() =>
                {
                    foreach (string w in result.Warnings) AddLog(LogLevel.Warning, w);
                    AddLog(LogLevel.Success, $"Created 3D view '{result.ViewName}' (Id {result.ViewId}) for {result.ElementCount} element(s). One Undo step in Revit removes it.");
                    SummaryText = $"Created '{result.ViewName}'.";
                });
            });
        }

        private bool CanCreate() => !IsRunning && _elements.Count > 0 && ViewTypes.Count > 0 && !HasInputError;

        private bool CanUseRevit() => !IsRunning;

        /// <summary>Saves the options and releases the external event. Called when the window closes.</summary>
        public void Shutdown()
        {
            SaveSettings();
            _jobs.Dispose();
        }

        // ── Selection ────────────────────────────────────────────────────────

        private void RunPick(string what, Func<UIDocument, ReadResult> pick)
        {
            HideWindow?.Invoke();
            Queue(uidoc =>
            {
                try
                {
                    ReadResult result = pick(uidoc);
                    OnUi(() => Merge(result, what));
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    OnUi(() => AddLog(LogLevel.Info, $"{what} cancelled."));
                }
                finally
                {
                    OnUi(() => ShowWindow?.Invoke());
                }
            }, onDenied: () => ShowWindow?.Invoke());
        }

        private void Merge(ReadResult result, string source)
        {
            if (result == null) return;
            int added = 0;
            foreach (PickedElement e in result.Elements)
            {
                if (_elements.Any(x => x.SameAs(e))) continue;
                _elements.Add(e);
                added++;
            }
            foreach (string s in result.Skipped) AddLog(LogLevel.Warning, s);
            if (result.Elements.Count > 0 || result.Skipped.Count > 0)
                AddLog(LogLevel.Info, $"{source}: {added} element(s) added, {result.Elements.Count - added} already listed, {result.Skipped.Count} skipped.");
            Refresh();
        }

        // ── Preview and metrics ──────────────────────────────────────────────

        private void Refresh()
        {
            HasInputError = !TryReadMargins(out double side, out double depth, out double topBottom);

            int largest = _elements.Count > 0 ? AlignmentPlanner.LargestSideIndex(_elements.Select(e => e.Footprint).ToList()) : -1;
            Rows.Clear();
            for (int i = 0; i < _elements.Count; i++) Rows.Add(new ElementRow(_elements[i], i == largest));
            ElementCountText = _elements.Count.ToString(CultureInfo.CurrentCulture);

            UpdateSideLabels(largest);
            UpdatePreview(side, depth, topBottom);
            RefreshCommands();
        }

        private void UpdateSideLabels(int largest)
        {
            if (largest < 0)
            {
                LargestSideText = "—";
                return;
            }

            ElementFootprint f = _elements[largest].Footprint;
            IReadOnlyList<PlanSide> sides = PlanSides.Of(f.Rectangle);
            LargestSideText = $"{f.Rectangle.Length * MmPerFoot:N0} × {f.Height * MmPerFoot:N0}";
            foreach (Choice<SideChoice> o in SideOptions)
            {
                o.Label = o.Value switch
                {
                    SideChoice.Auto => $"Largest side (auto) — A, faces {sides[0].Compass}",
                    SideChoice.LongA => $"Long side A — faces {sides[0].Compass}",
                    SideChoice.LongB => $"Long side B — faces {sides[1].Compass}",
                    SideChoice.ShortC => $"Short side C — faces {sides[2].Compass}",
                    SideChoice.ShortD => $"Short side D — faces {sides[3].Compass}",
                    _ => _pickedFaceNormal is Vec2 n
                        ? $"Picked face — faces {PlanSides.CompassName(n)}"
                        : "Picked face (use Pick face…)",
                };
            }
        }

        private void UpdatePreview(double sideMm, double depthMm, double topBottomMm)
        {
            PreviewElements = PreviewBox = PreviewSide = PreviewArrow = Geometry.Empty;
            BoxText = "—";
            LookingAtText = "—";
            if (_elements.Count == 0)
            {
                AlignmentText = "Select elements to see the alignment.";
                return;
            }

            var footprints = _elements.Select(e => e.Footprint).ToList();
            Alignment alignment;
            try
            {
                alignment = AlignmentPlanner.Choose(footprints, SelectedSide?.Value ?? SideChoice.Auto, _pickedFaceNormal);
            }
            catch (InvalidOperationException ex)
            {
                AlignmentText = ex.Message;
                return;
            }

            SectionBoxPlan box = SectionBoxPlanner.Plan(footprints, alignment, sideMm / MmPerFoot, depthMm / MmPerFoot,
                topBottomMm / MmPerFoot, SelectedCamera?.Value ?? CameraMode.FacingSide);
            BoxText = $"{box.Width * MmPerFoot:N0} × {box.Height * MmPerFoot:N0} × {box.Depth * MmPerFoot:N0}";
            LookingAtText = alignment.Side != null
                ? $"Side {alignment.Side.Key} · {alignment.Side.Compass}"
                : $"Face · {PlanSides.CompassName(-alignment.Forward)}";
            AlignmentText = alignment.SourceIndex >= 0
                ? $"{alignment.Description} of {_elements[alignment.SourceIndex].Category} {_elements[alignment.SourceIndex].ElementId}. The view is parallel to that side."
                : $"{alignment.Description}. The view is parallel to that face.";

            // Box corners in plan
            Vec2 o = box.Origin.Plan;
            Vec2[] boxCorners =
            {
                o + box.Right * box.Min.X + box.Forward * box.Min.Y,
                o + box.Right * box.Max.X + box.Forward * box.Min.Y,
                o + box.Right * box.Max.X + box.Forward * box.Max.Y,
                o + box.Right * box.Min.X + box.Forward * box.Max.Y,
            };

            // Fit everything into the canvas (Y flipped: north is up).
            var all = footprints.SelectMany(f => f.Hull).Concat(boxCorners).ToList();
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            double spanX = Math.Max(maxX - minX, 1e-6), spanY = Math.Max(maxY - minY, 1e-6);
            double scale = Math.Min((PreviewWidth - 2 * PreviewPad) / spanX, (PreviewHeight - 2 * PreviewPad) / spanY);
            double offX = (PreviewWidth - spanX * scale) / 2, offY = (PreviewHeight - spanY * scale) / 2;
            Point ToCanvas(Vec2 p) => new(offX + (p.X - minX) * scale, PreviewHeight - (offY + (p.Y - minY) * scale));

            PreviewElements = Polygons(footprints.Select(f => f.Hull.Select(ToCanvas).ToList()));
            PreviewBox = Polygons(new[] { boxCorners.Select(ToCanvas).ToList() });

            // Highlighted side, and an arrow from outside the box pointing at it.
            Vec2 target;
            if (alignment.Side != null)
            {
                PreviewSide = Line(ToCanvas(alignment.Side.Start), ToCanvas(alignment.Side.End));
                target = (alignment.Side.Start + alignment.Side.End) * 0.5;
            }
            else
            {
                target = o;
            }
            Vec2 tail = target - alignment.Forward * (box.Depth / 2 + 12 / scale);
            Vec2 head = target - alignment.Forward * (4 / scale);
            PreviewArrow = Arrow(ToCanvas(tail), ToCanvas(head));
        }

        private static Geometry Polygons(IEnumerable<List<Point>> polygons)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                foreach (List<Point> poly in polygons)
                {
                    if (poly.Count == 0) continue;
                    ctx.BeginFigure(poly[0], true, true);
                    ctx.PolyLineTo(poly.Skip(1).ToList(), true, false);
                }
            }
            g.Freeze();
            return g;
        }

        private static Geometry Line(Point a, Point b)
        {
            var g = new LineGeometry(a, b);
            g.Freeze();
            return g;
        }

        private static Geometry Arrow(Point tail, Point head)
        {
            Vector d = head - tail;
            if (d.Length < 1e-6) return Geometry.Empty;
            d.Normalize();
            Vector n = new(-d.Y, d.X);
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(tail, false, false);
                ctx.LineTo(head, true, false);
                ctx.BeginFigure(head, true, true);
                ctx.LineTo(head - d * 8 + n * 4, true, false);
                ctx.LineTo(head - d * 8 - n * 4, true, false);
            }
            g.Freeze();
            return g;
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void ApplySettings(IsolatedSectionSettings s)
        {
            SelectedSide = SideOptions.FirstOrDefault(o => o.Value == s.Side) ?? SideOptions[0];
            SelectedIsolation = IsolationOptions.FirstOrDefault(o => o.Value == s.Isolation) ?? IsolationOptions[0];
            SelectedCamera = CameraOptions.FirstOrDefault(o => o.Value == s.Camera) ?? CameraOptions[0];
            SelectedDetail = DetailOptions.FirstOrDefault(o => o.Value == s.Detail) ?? DetailOptions[2];
            SelectedStyle = StyleOptions.FirstOrDefault(o => o.Value == s.Style) ?? StyleOptions[1];
            SelectedViewType = ViewTypes.FirstOrDefault(t => t.Name == s.ViewTypeName) ?? ViewTypes.FirstOrDefault();
            SelectedTemplate = Templates.FirstOrDefault(t => t.Id >= 0 && t.Name == s.TemplateName) ?? Templates[0];
            SideMarginText = FormatMm(s.SideMarginMm);
            DepthMarginText = FormatMm(s.DepthMarginMm);
            TopBottomMarginText = FormatMm(s.TopBottomMarginMm);
            NamePattern = s.NamePattern;
            OpenView = s.OpenView;
        }

        private void SaveSettings()
        {
            var s = new IsolatedSectionSettings
            {
                Side = SelectedSide?.Value ?? SideChoice.Auto,
                Isolation = SelectedIsolation?.Value ?? IsolationMode.Temporary,
                Camera = SelectedCamera?.Value ?? CameraMode.FacingSide,
                Detail = SelectedDetail?.Value ?? DetailChoice.Fine,
                Style = SelectedStyle?.Value ?? StyleChoice.Shaded,
                NamePattern = NamePattern,
                OpenView = OpenView,
                ViewTypeName = SelectedViewType?.Name ?? string.Empty,
                TemplateName = SelectedTemplate != null && SelectedTemplate.Id >= 0 ? SelectedTemplate.Name : string.Empty,
            };
            if (TryReadMargins(out double side, out double depth, out double topBottom))
            {
                s.SideMarginMm = side;
                s.DepthMarginMm = depth;
                s.TopBottomMarginMm = topBottom;
            }
            if (!IsolatedSectionSettingsService.Save(s, out string warning)) AddLog(LogLevel.Warning, warning);
        }

        private bool TryReadMargins(out double side, out double depth, out double topBottom)
        {
            depth = topBottom = 0;
            return TryParseMm(SideMarginText, out side) & TryParseMm(DepthMarginText, out depth) & TryParseMm(TopBottomMarginText, out topBottom);
        }

        private static bool TryParseMm(string text, out double mm)
        {
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out mm)
                      || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mm);
            if (ok && mm >= 0 && mm <= 100000 && !double.IsNaN(mm)) return true;
            mm = 0;
            return false;
        }

        private static string FormatMm(double mm) => mm.ToString("0.##", CultureInfo.CurrentCulture);

        // ── Revit jobs ───────────────────────────────────────────────────────

        private void Queue(Action<UIDocument> work, Action onDenied = null)
        {
            void Body(UIApplication app)
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || !uidoc.Document.Equals(_doc))
                    throw new InvalidOperationException($"Switch back to '{_doc.Title}' — this window works on that model.");
                work(uidoc);
            }

            if (_jobs.TryRun(Body, OnJobFailed)) return;

            onDenied?.Invoke();
            SummaryText = "Revit is busy. Close any open dialog and try again.";
            AddLog(LogLevel.Warning, SummaryText);
        }

        private void OnJobFailed(Exception ex)
        {
            OnUi(() =>
            {
                ShowWindow?.Invoke();
                SummaryText = "Stopped, nothing was changed: " + ex.Message;
                AddLog(LogLevel.Error, SummaryText);
            });
            if (ex is not InvalidOperationException) ToolGuard.Report(GetType(), ex);
        }

        private void RefreshCommands()
        {
            PickElementsCommand.NotifyCanExecuteChanged();
            PickLinkedCommand.NotifyCanExecuteChanged();
            PickFaceCommand.NotifyCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            ClearElementsCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
        }

        private void OnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }
    }
}
