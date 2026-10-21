using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Services;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.Revit
{
    /// <summary>
    /// Creates the isolated 3D view in one transaction (one Undo step; rolled back on any error):
    /// new 3D view → name → template / detail / style → section box turned to the chosen side →
    /// camera facing that side → isolate the elements. Then opens the view and zooms to fit.
    /// </summary>
    public static class IsolatedViewBuilder
    {
        private const double MmPerFoot = 304.8;

        /// <summary>The document's 3D view types, by name.</summary>
        public static List<NamedId> ViewTypes(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.ThreeDimensional)
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => new NamedId(t.Id.Value, t.Name))
                .ToList();

        /// <summary>The document's 3D view templates, by name, after a "&lt;None&gt;" entry (id −1).</summary>
        public static List<NamedId> Templates(Document doc)
        {
            var list = new List<NamedId> { new(-1, "<None>") };
            list.AddRange(new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(v => new NamedId(v.Id.Value, v.Name)));
            return list;
        }

        /// <summary>
        /// Builds the view described by <paramref name="request"/>. Progress goes to <paramref name="log"/>.
        /// Throws when nothing can be created; the transaction is rolled back first.
        /// </summary>
        public static CreateResult Build(UIDocument uidoc, CreateRequest request, Action<string> log)
        {
            Document doc = uidoc.Document;
            if (doc.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
            if (doc.IsFamilyDocument) throw new InvalidOperationException("Isolated Section works in projects, not families.");

            var result = new CreateResult();

            // 1 — re-read every element: it may have moved, been deleted, or its link unloaded.
            var elements = new List<PickedElement>();
            foreach (PickedElement p in request.Elements)
            {
                PickedElement fresh = SelectionService.Refresh(doc, p, out string reason);
                if (fresh != null) elements.Add(fresh);
                else result.Warnings.Add($"{p.Category} {p.ElementId} skipped: {reason}");
            }
            if (elements.Count == 0) throw new InvalidOperationException("None of the selected elements can be used any more.");
            result.ElementCount = elements.Count;

            // 2 — alignment and section box (pure maths).
            var footprints = elements.Select(e => e.Footprint).ToList();
            Alignment alignment = AlignmentPlanner.Choose(footprints, request.Side, request.PickedFaceNormal);
            result.AlignmentText = alignment.SourceIndex >= 0
                ? $"{alignment.Description} of {elements[alignment.SourceIndex].Category} {elements[alignment.SourceIndex].ElementId}"
                : alignment.Description;
            log?.Invoke(result.AlignmentText);

            SectionBoxPlan box = SectionBoxPlanner.Plan(footprints, alignment,
                request.SideMarginMm / MmPerFoot, request.DepthMarginMm / MmPerFoot, request.TopBottomMarginMm / MmPerFoot,
                request.Camera);
            result.BoxText = $"{box.Width * MmPerFoot:N0} × {box.Height * MmPerFoot:N0} × {box.Depth * MmPerFoot:N0}";
            log?.Invoke($"Section box W × H × D: {result.BoxText} mm");

            View3D view;
            using (var tx = new Transaction(doc, "Isolated Section 3D View"))
            {
                tx.Start();

                ElementId typeId = ResolveViewType(doc, request.ViewTypeId);
                view = View3D.CreateIsometric(doc, typeId);

                // Name
                PickedElement source = alignment.SourceIndex >= 0 ? elements[alignment.SourceIndex] : elements[0];
                string sideText = alignment.Side?.Key.ToString() ?? "Face";
                string category = elements.Select(e => e.Category).Distinct().Count() == 1 ? source.Category : "Mixed";
                string name = ViewNameBuilder.Build(request.NamePattern, category, source.ElementId, elements.Count, sideText, DateTime.Now);
                var existing = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToList();
                view.Name = ViewNameBuilder.MakeUnique(name, existing);

                // Template, or detail level and visual style
                ApplyLook(doc, view, request, result);

                // Section box, turned in plan to face the chosen side
                Transform frame = Transform.Identity;
                frame.Origin = new XYZ(box.Origin.X, box.Origin.Y, box.Origin.Z);
                frame.BasisX = new XYZ(box.Right.X, box.Right.Y, 0);
                frame.BasisY = new XYZ(box.Forward.X, box.Forward.Y, 0);
                frame.BasisZ = XYZ.BasisZ;
                view.IsSectionBoxActive = true;
                view.SetSectionBox(new BoundingBoxXYZ
                {
                    Transform = frame,
                    Min = new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                    Max = new XYZ(box.Max.X, box.Max.Y, box.Max.Z),
                });

                // Camera facing the side
                view.SetOrientation(new ViewOrientation3D(
                    new XYZ(box.Eye.X, box.Eye.Y, box.Eye.Z),
                    new XYZ(box.Up.X, box.Up.Y, box.Up.Z),
                    new XYZ(box.ViewDirection.X, box.ViewDirection.Y, box.ViewDirection.Z)));

                // Isolation
                Isolate(view, elements, request.Isolation, result);

                TransactionStatus status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"Revit did not accept the new view (transaction {status}).");
            }

            result.ViewId = view.Id.Value;
            result.ViewName = view.Name;

            if (request.OpenView) Open(uidoc, view, result);
            return result;
        }

        private static ElementId ResolveViewType(Document doc, long id)
        {
            if (id >= 0 && doc.GetElement(new ElementId(id)) is ViewFamilyType t && t.ViewFamily == ViewFamily.ThreeDimensional)
                return t.Id;
            ViewFamilyType first = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
            return first?.Id ?? throw new InvalidOperationException("The model has no 3D view type.");
        }

        private static void ApplyLook(Document doc, View3D view, CreateRequest request, CreateResult result)
        {
            if (request.TemplateId >= 0)
            {
                if (doc.GetElement(new ElementId(request.TemplateId)) is View3D template && template.IsTemplate)
                {
                    view.ViewTemplateId = template.Id;
                    return; // the template decides detail level and visual style
                }
                result.Warnings.Add("The chosen view template no longer exists; none was applied.");
            }

            try
            {
                view.DetailLevel = request.Detail switch
                {
                    DetailChoice.Coarse => ViewDetailLevel.Coarse,
                    DetailChoice.Medium => ViewDetailLevel.Medium,
                    _ => ViewDetailLevel.Fine,
                };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Warnings.Add($"Detail level not set: {ex.Message}");
            }

            try
            {
                view.DisplayStyle = request.Style switch
                {
                    StyleChoice.HiddenLine => DisplayStyle.HLR,
                    StyleChoice.ConsistentColors => DisplayStyle.FlatColors,
                    StyleChoice.Realistic => DisplayStyle.Realistic,
                    _ => DisplayStyle.Shading,
                };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Warnings.Add($"Visual style not set: {ex.Message}");
            }
        }

        private static void Isolate(View3D view, List<PickedElement> elements, IsolationMode mode, CreateResult result)
        {
            if (mode == IsolationMode.None) return;
            if (!view.CanUseTemporaryVisibilityModes())
            {
                result.Warnings.Add("This view cannot isolate elements; only the section box limits it.");
                return;
            }

            // A linked element cannot be isolated on its own: its link instance is isolated instead,
            // and the section box keeps the rest of the link out of view.
            var ids = elements
                .Select(e => new ElementId(e.IsLinked ? e.LinkInstanceId : e.ElementId))
                .Distinct()
                .ToList();
            if (elements.Any(e => e.IsLinked))
                result.Warnings.Add("Linked elements: the whole link is isolated; the section box hides the rest of it.");

            view.IsolateElementsTemporary(ids);
            if (mode == IsolationMode.Permanent) view.ConvertTemporaryHideIsolateToPermanent();
        }

        private static void Open(UIDocument uidoc, View3D view, CreateResult result)
        {
            try
            {
                uidoc.ActiveView = view;
                UIView uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                uiView?.ZoomToFit();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Warnings.Add($"The view was created but could not be opened: {ex.Message}");
            }
        }
    }
}
