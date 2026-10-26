using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TagToText.V001.Core.Services
{
    /// <summary>
    /// Finds supported tags in a view and reads their text, text position and leader.
    /// Never throws for a single bad tag: the problem goes into <see cref="TagSnapshot.Problem"/>.
    /// </summary>
    public static class TagReader
    {
        private static readonly BuiltInCategory[] SpatialTagCategories =
        {
            BuiltInCategory.OST_RoomTags, BuiltInCategory.OST_AreaTags, BuiltInCategory.OST_MEPSpaceTags
        };

        private static readonly ViewType[] SupportedViewTypes =
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
            ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.DraftingView
        };

        /// <summary>
        /// Returns null when Tag To Text can work in <paramref name="view"/>, otherwise the reason it can't.
        /// </summary>
        public static string CheckView(View view)
        {
            if (view == null) return "There is no active view.";
            if (view.IsTemplate) return "The active view is a view template.";
            if (!SupportedViewTypes.Contains(view.ViewType))
                return $"Tag To Text works in plan, section, elevation, detail and drafting views. The active view is a {view.ViewType} view.";
            return null;
        }

        /// <summary>Every supported tag owned by <paramref name="view"/>.</summary>
        public static List<Element> CollectInView(Document doc, View view)
        {
            var result = new List<Element>();
            result.AddRange(new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpotDimension)).ToElements());
            result.AddRange(new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).ToElements());
            result.AddRange(new FilteredElementCollector(doc, view.Id)
                .WherePasses(new ElementMulticategoryFilter(SpatialTagCategories))
                .WhereElementIsNotElementType()
                .ToElements());
            return result.Where(e => e.OwnerViewId == view.Id).ToList();
        }

        /// <summary>The supported tags among <paramref name="ids"/> that belong to <paramref name="view"/>.</summary>
        public static List<Element> CollectFromIds(Document doc, View view, IEnumerable<ElementId> ids)
        {
            return ids.Select(doc.GetElement)
                      .Where(e => e != null && e.OwnerViewId == view.Id && Classify(e) != null)
                      .ToList();
        }

        /// <summary>The <see cref="TagKind"/> of <paramref name="e"/>, or null when it isn't a supported tag.</summary>
        public static TagKind? Classify(Element e)
        {
            switch (e)
            {
                case SpotDimension spot:
                    long cat = spot.Category?.Id.Value ?? 0;
                    if (cat == (long)BuiltInCategory.OST_SpotElevations) return TagKind.SpotElevation;
                    if (cat == (long)BuiltInCategory.OST_SpotCoordinates) return TagKind.SpotCoordinate;
                    if (cat == (long)BuiltInCategory.OST_SpotSlopes) return TagKind.SpotSlope;
                    return null;
                case IndependentTag:
                    return TagKind.ElementTag;
                case SpatialElementTag:
                    return TagKind.SpatialTag;
                default:
                    return null;
            }
        }

        /// <summary>Reads one tag. Returns null when <paramref name="e"/> isn't a supported tag.</summary>
        public static TagSnapshot Read(Document doc, View view, Element e)
        {
            TagKind? kind = Classify(e);
            if (kind == null) return null;

            bool inGroup = e.GroupId != null && e.GroupId != ElementId.InvalidElementId;
            string owner = OwnerOf(doc, e.Id);
            string label = e.Category?.Name ?? kind.ToString();

            try
            {
                return kind switch
                {
                    TagKind.SpotElevation or TagKind.SpotCoordinate or TagKind.SpotSlope
                        => ReadSpot(doc, view, (SpotDimension)e, kind.Value, label, inGroup, owner),
                    TagKind.ElementTag => ReadIndependentTag(doc, view, (IndependentTag)e, label, inGroup, owner),
                    _ => ReadSpatialTag(view, (SpatialElementTag)e, label, inGroup, owner)
                };
            }
            catch (Exception ex)
            {
                return new TagSnapshot
                {
                    TagId = e.Id, Kind = kind.Value, TypeLabel = label, Lines = Array.Empty<string>(),
                    InGroup = inGroup, OwnedBy = owner, Problem = "Can't read this tag: " + ex.Message
                };
            }
        }

        private static TagSnapshot ReadSpot(Document doc, View view, SpotDimension spot, TagKind kind,
            string label, bool inGroup, string owner)
        {
            string[] lines = TextRules.SplitLines(spot.ValueString);
            Element type = doc.GetElement(spot.GetTypeId());

            if (kind == TagKind.SpotElevation && lines.Length > 0)
            {
                // The type's "Elevation Indicator" and the instance prefix/suffix are not always part of
                // ValueString; add them only when missing.
                string indicator = TypeString(type, BuiltInParameter.SPOT_ELEV_IND_ELEVATION);
                lines[0] = TextRules.PrefixOnce(TryString(() => spot.Prefix), TextRules.PrefixOnce(indicator, lines[0]));
                lines[^1] = TextRules.SuffixOnce(lines[^1], TryString(() => spot.Suffix));
            }
            else if (kind == TagKind.SpotCoordinate && lines.Length >= 2)
            {
                lines[0] = TextRules.PrefixOnce(TypeString(type, BuiltInParameter.SPOT_ELEV_IND_NS), lines[0]);
                lines[1] = TextRules.PrefixOnce(TypeString(type, BuiltInParameter.SPOT_ELEV_IND_EW), lines[1]);
            }
            else if (kind == TagKind.SpotSlope && lines.Length > 0)
            {
                lines[0] = TextRules.PrefixOnce(TryString(() => spot.Prefix), lines[0]);
                lines[^1] = TextRules.SuffixOnce(lines[^1], TryString(() => spot.Suffix));
            }

            XYZ origin = OnViewPlane(view, spot.Origin);
            XYZ textPoint;
            XYZ end = null, elbow = null;

            if (kind == TagKind.SpotSlope)
            {
                // Spot slopes have no text position or leader end in the API: the text goes just above
                // the slope arrow, with a leader back to the spotted point.
                textPoint = origin + view.UpDirection * PaperToModel(view, 5.0 / 304.8);
                end = origin;
            }
            else
            {
                textPoint = OnViewPlane(view, TryPoint(() => spot.TextPosition) ?? spot.Origin);
                if (spot.HasLeader)
                {
                    end = origin;
                    if (spot.LeaderHasShoulder)
                        elbow = OnViewPlane(view, TryPoint(() => spot.LeaderShoulderPosition));
                }
            }

            return new TagSnapshot
            {
                TagId = spot.Id, Kind = kind, TypeLabel = label, Lines = lines,
                TextPoint = textPoint, LeaderEnd = end, LeaderElbow = elbow, Rotation = 0,
                InGroup = inGroup, OwnedBy = owner,
                Problem = lines.Length == 0 ? "The tag shows no text." : null
            };
        }

        private static TagSnapshot ReadIndependentTag(Document doc, View view, IndependentTag tag,
            string label, bool inGroup, string owner)
        {
            string[] lines = TextRules.SplitLines(tag.TagText);
            XYZ head = OnViewPlane(view, tag.TagHeadPosition);
            XYZ end = null, elbow = null;

            if (tag.HasLeader)
            {
                Reference reference = tag.GetTaggedReferences().FirstOrDefault();
                if (reference != null)
                {
                    end = tag.LeaderEndCondition == LeaderEndCondition.Free
                        ? TryPoint(() => tag.GetLeaderEnd(reference))
                        : null;
                    end ??= TaggedElementPoint(doc, view, tag);
                    if (TryBool(() => tag.HasLeaderElbow(reference)))
                        elbow = TryPoint(() => tag.GetLeaderElbow(reference));
                }
                end = end == null ? null : OnViewPlane(view, end);
                elbow = elbow == null ? null : OnViewPlane(view, elbow);
            }

            return new TagSnapshot
            {
                TagId = tag.Id, Kind = TagKind.ElementTag, TypeLabel = label, Lines = lines,
                TextPoint = head, LeaderEnd = end, LeaderElbow = elbow,
                Rotation = TryDouble(() => tag.RotationAngle),
                InGroup = inGroup, OwnedBy = owner,
                Problem = lines.Length == 0 ? "The tag shows no text." : null
            };
        }

        private static TagSnapshot ReadSpatialTag(View view, SpatialElementTag tag, string label, bool inGroup, string owner)
        {
            string[] lines = TextRules.SplitLines(tag.TagText);
            XYZ end = null, elbow = null;
            if (tag.HasLeader)
            {
                end = OnViewPlane(view, TryPoint(() => tag.LeaderEnd));
                if (tag.HasElbow) elbow = OnViewPlane(view, TryPoint(() => tag.LeaderElbow));
            }

            return new TagSnapshot
            {
                TagId = tag.Id, Kind = TagKind.SpatialTag, TypeLabel = label, Lines = lines,
                TextPoint = OnViewPlane(view, tag.TagHeadPosition), LeaderEnd = end, LeaderElbow = elbow,
                Rotation = TryDouble(() => tag.RotationAngle),
                InGroup = inGroup, OwnedBy = owner,
                Problem = lines.Length == 0 ? "The tag shows no text." : null
            };
        }

        // An attached leader ends on the tagged element; the API gives no point for it, so use the
        // element's location (or the centre of its box in the view). Linked elements are moved by the
        // link's transform.
        private static XYZ TaggedElementPoint(Document doc, View view, IndependentTag tag)
        {
            foreach (LinkElementId linkId in tag.GetTaggedElementIds())
            {
                Element element;
                Transform transform = Transform.Identity;
                if (linkId.LinkInstanceId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(linkId.LinkInstanceId) is not RevitLinkInstance link) continue;
                    Document linkDoc = link.GetLinkDocument();
                    element = linkDoc?.GetElement(linkId.LinkedElementId);
                    transform = link.GetTotalTransform();
                }
                else
                {
                    element = doc.GetElement(linkId.HostElementId);
                }
                if (element == null) continue;

                XYZ p = element.Location switch
                {
                    LocationPoint lp => lp.Point,
                    LocationCurve lc => lc.Curve.Evaluate(0.5, true),
                    _ => null
                };
                if (p == null)
                {
                    BoundingBoxXYZ box = element.get_BoundingBox(linkId.LinkInstanceId == ElementId.InvalidElementId ? view : null)
                                         ?? element.get_BoundingBox(null);
                    if (box != null) p = (box.Min + box.Max) / 2;
                }
                if (p != null) return transform.OfPoint(p);
            }
            return null;
        }

        /// <summary>Moves <paramref name="p"/> onto the plane of <paramref name="view"/>. Null stays null.</summary>
        public static XYZ OnViewPlane(View view, XYZ p)
        {
            if (p == null) return null;
            if (view.ViewType == ViewType.DraftingView) return new XYZ(p.X, p.Y, 0);
            XYZ n = view.ViewDirection;
            double d = (p - view.Origin).DotProduct(n);
            return p - n * d;
        }

        /// <summary>Converts a length on paper (feet) to model feet at the view's scale.</summary>
        public static double PaperToModel(View view, double paperFeet) => paperFeet * Math.Max(1, view.Scale);

        private static string OwnerOf(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared) return null;
            try
            {
                if (WorksharingUtils.GetCheckoutStatus(doc, id) != CheckoutStatus.OwnedByOtherUser) return null;
                return WorksharingUtils.GetWorksharingTooltipInfo(doc, id)?.Owner ?? "another user";
            }
            catch
            {
                return null;
            }
        }

        private static string TypeString(Element type, BuiltInParameter bip)
        {
            try { return type?.get_Parameter(bip)?.AsString(); }
            catch { return null; }
        }

        private static string TryString(Func<string> get)
        {
            try { return get(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static XYZ TryPoint(Func<XYZ> get)
        {
            try { return get(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static bool TryBool(Func<bool> get)
        {
            try { return get(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private static double TryDouble(Func<double> get)
        {
            try { return get(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return 0; }
        }
    }
}
