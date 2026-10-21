using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.Revit
{
    /// <summary>Result of reading a set of elements: the usable ones, and why the others were skipped.</summary>
    public sealed class ReadResult
    {
        /// <summary>Elements the tool can use.</summary>
        public List<PickedElement> Elements { get; } = new();

        /// <summary>One line per skipped element.</summary>
        public List<string> Skipped { get; } = new();
    }

    /// <summary>A picked face: the element it belongs to and the face's outward plan direction.</summary>
    public sealed class PickedFace
    {
        /// <summary>The element holding the face.</summary>
        public PickedElement Element { get; init; }

        /// <summary>Unit outward normal of the face, flattened to plan.</summary>
        public Vec2 PlanNormal { get; init; }
    }

    /// <summary>
    /// Reads the current selection and runs the pick prompts. Must run in a Revit API context. Esc in a
    /// prompt throws <see cref="Autodesk.Revit.Exceptions.OperationCanceledException"/>, which callers handle.
    /// </summary>
    public static class SelectionService
    {
        /// <summary>A face flatter than this (horizontal part of its unit normal) cannot be looked at from the side.</summary>
        public const double MinHorizontalNormal = 0.1;

        /// <summary>Reads the elements selected in Revit.</summary>
        public static ReadResult ReadSelection(UIDocument uidoc)
        {
            var result = new ReadResult();
            Document doc = uidoc.Document;
            foreach (ElementId id in uidoc.Selection.GetElementIds())
                AddHost(doc.GetElement(id), id, result);
            return result;
        }

        /// <summary>Prompts for host-model elements (Finish or Enter to end).</summary>
        public static ReadResult PickHostElements(UIDocument uidoc)
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new HostFilter(),
                "Isolated Section: pick model elements, then click Finish");
            var result = new ReadResult();
            foreach (Reference r in refs)
                AddHost(uidoc.Document.GetElement(r), r.ElementId, result);
            return result;
        }

        /// <summary>Prompts for elements inside linked models (Finish or Enter to end).</summary>
        public static ReadResult PickLinkedElements(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.LinkedElement, new LinkedFilter(doc),
                "Isolated Section: pick elements in linked models, then click Finish");
            var result = new ReadResult();
            foreach (Reference r in refs)
            {
                var link = doc.GetElement(r.ElementId) as RevitLinkInstance;
                Element e = link?.GetLinkDocument()?.GetElement(r.LinkedElementId);
                string reason = "the link is not loaded";
                PickedElement picked = e == null ? null : ElementGeometryReader.Read(e, link, out reason);
                if (picked != null) result.Elements.Add(picked);
                else result.Skipped.Add($"Linked element {r.LinkedElementId.Value} skipped: {reason}");
            }
            return result;
        }

        /// <summary>
        /// Prompts for one face of a host-model element. Returns null, with <paramref name="problem"/>, when
        /// the face is (nearly) horizontal or cannot be read.
        /// </summary>
        public static PickedFace PickFace(UIDocument uidoc, out string problem)
        {
            problem = null;
            Reference r = uidoc.Selection.PickObject(ObjectType.Face, new HostFilter(),
                "Isolated Section: pick the face the 3D view should look at");
            Element e = uidoc.Document.GetElement(r);

            PickedElement picked = ElementGeometryReader.Read(e, null, out string reason);
            if (picked == null)
            {
                problem = $"That element cannot be used: {reason}.";
                return null;
            }

            XYZ normal = r.GlobalPoint == null ? null : ElementGeometryReader.FaceNormalAt(e, r.GlobalPoint);
            if (normal == null)
            {
                problem = "The picked face could not be read. Pick another face, or choose a side instead.";
                return null;
            }

            var plan = new Vec2(normal.X, normal.Y);
            if (plan.Length < MinHorizontalNormal)
            {
                problem = "That face is horizontal (a top or bottom). Pick a vertical or sloped face.";
                return null;
            }
            return new PickedFace { Element = picked, PlanNormal = plan.Normalized() };
        }

        /// <summary>
        /// Reads <paramref name="picked"/> again from the model (it may have moved since it was picked).
        /// Returns null, with <paramref name="reason"/>, when it is gone or its link is unloaded.
        /// </summary>
        public static PickedElement Refresh(Document doc, PickedElement picked, out string reason)
        {
            reason = null;
            RevitLinkInstance link = null;
            Document source = doc;
            if (picked.IsLinked)
            {
                link = doc.GetElement(new ElementId(picked.LinkInstanceId)) as RevitLinkInstance;
                source = link?.GetLinkDocument();
                if (source == null)
                {
                    reason = $"link '{picked.LinkName}' is not loaded";
                    return null;
                }
            }
            return ElementGeometryReader.Read(source.GetElement(new ElementId(picked.ElementId)), link, out reason);
        }

        private static void AddHost(Element e, ElementId id, ReadResult result)
        {
            PickedElement picked = ElementGeometryReader.Read(e, null, out string reason);
            if (picked != null) result.Elements.Add(picked);
            else result.Skipped.Add($"Element {id.Value} skipped: {reason}");
        }

        private sealed class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => ElementGeometryReader.IsUsable(elem, out _);

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class LinkedFilter : ISelectionFilter
        {
            private readonly Document _doc;

            public LinkedFilter(Document doc) => _doc = doc;

            public bool AllowElement(Element elem) => elem is RevitLinkInstance;

            public bool AllowReference(Reference reference, XYZ position)
            {
                var link = _doc.GetElement(reference.ElementId) as RevitLinkInstance;
                Element e = link?.GetLinkDocument()?.GetElement(reference.LinkedElementId);
                return ElementGeometryReader.IsUsable(e, out _);
            }
        }
    }
}
