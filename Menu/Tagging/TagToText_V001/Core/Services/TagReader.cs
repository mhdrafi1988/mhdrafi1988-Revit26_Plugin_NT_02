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

            TagSnapshot snapshot;
            try
            {
                snapshot = kind switch
                {
                    TagKind.SpotElevation or TagKind.SpotCoordinate or TagKind.SpotSlope
                        => ReadSpot(doc, view, (SpotDimension)e, kind.Value, label, inGroup, owner),
                    TagKind.ElementTag => ReadIndependentTag(doc, view, (IndependentTag)e, label, inGroup, owner),
                    _ => ReadSpatialTag(view, (SpatialElementTag)e, label, inGroup, owner)
                };
            }
            catch (Exception ex)
            {
                snapshot = new TagSnapshot
                {
                    TagId = e.Id, Kind = kind.Value, TypeLabel = label, Lines = Array.Empty<string>(),
                    InGroup = inGroup, OwnedBy = owner, Problem = "Can't read this tag: " + ex.Message
                };
            }

            // Table grouping data. A failure here only leaves "(none)"; it never stops the tag being read.
            snapshot.TagTypeName = TagTypeNameOf(doc, e);
            snapshot.TaggedCategory = TaggedCategoryOf(doc, e);
            return snapshot;
        }

        /// <summary>Text shown when a grouping value is not available.</summary>
        private const string NoValue = "(none)";

        /// <summary>"Family : Type" of the tag, or "(none)".</summary>
        private static string TagTypeNameOf(Document doc, Element tag)
        {
            try
            {
                if (doc.GetElement(tag.GetTypeId()) is ElementType type)
                {
                    string family = type.FamilyName;
                    return string.IsNullOrWhiteSpace(family) ? type.Name : $"{family} : {type.Name}";
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            catch (InvalidOperationException) { }
            return NoValue;
        }

        /// <summary>
        /// Category name of the element the tag points at: the tagged element (host or linked) of an element
        /// tag, the room / area / space of a spatial tag, the first spotted element of a spot. "(none)" if unknown.
        /// </summary>
        private static string TaggedCategoryOf(Document doc, Element tag)
        {
            try
            {
                switch (tag)
                {
                    case IndependentTag independent:
                        foreach (LinkElementId id in independent.GetTaggedElementIds())
                        {
                            Element target = id.LinkInstanceId != ElementId.InvalidElementId
                                ? (doc.GetElement(id.LinkInstanceId) as RevitLinkInstance)?.GetLinkDocument()?.GetElement(id.LinkedElementId)
                                : doc.GetElement(id.HostElementId);
                            if (target?.Category != null) return target.Category.Name;
                        }
                        break;
                    case Autodesk.Revit.DB.Architecture.RoomTag room:
                        return room.Room?.Category?.Name ?? NoValue;
                    case AreaTag area:
                        return area.Area?.Category?.Name ?? NoValue;
                    case Autodesk.Revit.DB.Mechanical.SpaceTag space:
                        return space.Space?.Category?.Name ?? NoValue;
                    case SpotDimension spot:
                        foreach (Reference r in spot.References)
                        {
                            Element target = doc.GetElement(r.ElementId);
                            if (target is RevitLinkInstance link && r.LinkedElementId != ElementId.InvalidElementId)
                                target = link.GetLinkDocument()?.GetElement(r.LinkedElementId);
                            if (target?.Category != null) return target.Category.Name;
                        }
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            catch (InvalidOperationException) { }
            return NoValue;
        }

        private static TagSnapshot ReadSpot(Document doc, View view, SpotDimension spot, TagKind kind,
            string label, bool inGroup, string owner)
        {
            string[] lines = TextRules.SplitLines(TryString(() => spot.ValueString));
            Element type = doc.GetElement(spot.GetTypeId());

            // Revit 2026 can return an empty ValueString for spot elevations and coordinates. Build the
            // text from the spot's own value parameters instead (already prefixed and suffixed).
            if (lines.Length == 0 && kind != TagKind.SpotSlope)
            {
                lines = SpotLinesFromParameters(doc, spot, type, kind);
            }
            else if (kind == TagKind.SpotElevation && lines.Length > 0)
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
            XYZ end = null, elbow = null, start = null;

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
                    // Spot leader: spotted point (Origin) → bend (shoulder position) → leader end, where
                    // the shoulder meets the text. The note copies the same three points.
                    end = origin;
                    start = OnViewPlane(view, TryPoint(() => spot.LeaderEndPosition));
                    if (spot.LeaderHasShoulder)
                        elbow = OnViewPlane(view, TryPoint(() => spot.LeaderShoulderPosition));
                }
            }

            return new TagSnapshot
            {
                TagId = spot.Id, Kind = kind, TypeLabel = label, Lines = lines,
                TextPoint = textPoint, LeaderEnd = end, LeaderElbow = elbow, LeaderStart = start, Rotation = 0,
                InGroup = inGroup, OwnedBy = owner,
                Problem = null
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
                Problem = null
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
                Problem = null
            };
        }

        // Text of a spot from its value parameters, formatted with the spot type's units:
        // elevation = "Single/Upper Value" (+ "Lower Value" when it differs), coordinate =
        // "Top Coordinate" and "Bottom Coordinate". Prefixes, suffixes and indicators are added.
        private static string[] SpotLinesFromParameters(Document doc, SpotDimension spot, Element type, TagKind kind)
        {
            var lines = new List<string>();
            if (kind == TagKind.SpotElevation)
            {
                double? upper = LengthValue(spot, BuiltInParameter.SPOT_ELEV_SINGLE_OR_UPPER_VALUE);
                double? lower = LengthValue(spot, BuiltInParameter.SPOT_ELEV_LOWER_VALUE);
                bool two = upper != null && lower != null && Math.Abs(upper.Value - lower.Value) > 1e-9;
                if (upper != null)
                    lines.Add(Compose(
                        AnyString(spot, type, BuiltInParameter.SPOT_ELEV_SINGLE_OR_UPPER_PREFIX),
                        TypeString(type, two ? BuiltInParameter.SPOT_ELEV_IND_TOP : BuiltInParameter.SPOT_ELEV_IND_ELEVATION),
                        Format(doc, type, upper.Value),
                        AnyString(spot, type, BuiltInParameter.SPOT_ELEV_SINGLE_OR_UPPER_SUFFIX)));
                if (two)
                    lines.Add(Compose(
                        AnyString(spot, type, BuiltInParameter.SPOT_ELEV_LOWER_PREFIX),
                        TypeString(type, BuiltInParameter.SPOT_ELEV_IND_BOTTOM),
                        Format(doc, type, lower.Value),
                        AnyString(spot, type, BuiltInParameter.SPOT_ELEV_LOWER_SUFFIX)));
            }
            else if (kind == TagKind.SpotCoordinate)
            {
                double? top = LengthValue(spot, BuiltInParameter.SPOT_ELEV_TOP_VALUE);
                double? bottom = LengthValue(spot, BuiltInParameter.SPOT_ELEV_BOT_VALUE);
                if (top != null)
                    lines.Add(Compose(
                        AnyString(spot, type, BuiltInParameter.SPOT_COORDINATE_TOP_PREFIX),
                        TypeString(type, BuiltInParameter.SPOT_ELEV_IND_NS),
                        Format(doc, type, top.Value),
                        AnyString(spot, type, BuiltInParameter.SPOT_COORDINATE_TOP_SUFFIX)));
                if (bottom != null)
                    lines.Add(Compose(
                        AnyString(spot, type, BuiltInParameter.SPOT_COORDINATE_BOTTOM_PREFIX),
                        TypeString(type, BuiltInParameter.SPOT_ELEV_IND_EW),
                        Format(doc, type, bottom.Value),
                        AnyString(spot, type, BuiltInParameter.SPOT_COORDINATE_BOTTOM_SUFFIX)));
            }
            return lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        }

        private static string Compose(string prefix, string indicator, string value, string suffix)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return TextRules.SuffixOnce(TextRules.PrefixOnce(prefix, TextRules.PrefixOnce(indicator, value)), suffix);
        }

        private static double? LengthValue(Element e, BuiltInParameter bip)
        {
            try
            {
                Parameter p = e.get_Parameter(bip);
                return p != null && p.HasValue && p.StorageType == StorageType.Double ? p.AsDouble() : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        // Formats a length like the spot shows it: the spot type's own units if it has them, else the project's.
        private static string Format(Document doc, Element type, double value)
        {
            try
            {
                var options = new FormatValueOptions();
                if (type is DimensionType dt)
                {
                    FormatOptions fo = dt.GetUnitsFormatOptions();
                    if (fo != null && !fo.UseDefault) options.SetFormatOptions(fo);
                }
                return UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, value, false, options);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, value, false);
            }
        }

        // A text parameter from the instance, or from its type when the instance doesn't have it.
        private static string AnyString(Element instance, Element type, BuiltInParameter bip)
        {
            string v = null;
            try { v = instance.get_Parameter(bip)?.AsString(); } catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            return string.IsNullOrEmpty(v) ? TypeString(type, bip) : v;
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
