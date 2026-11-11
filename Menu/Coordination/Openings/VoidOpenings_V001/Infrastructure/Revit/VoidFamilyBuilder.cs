using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Builds the void families through the family API and saves them as .rfa files. A family is a
    /// work-plane-based Generic Model with one void extrusion centred on its origin, "Cut with Voids
    /// When Loaded" on. The parametric build checks itself: it changes each size parameter and measures
    /// the void, and reports failure rather than saving a family that does not flex.
    /// </summary>
    public static class VoidFamilyBuilder
    {
        /// <summary>Sizes of the void to build, in millimetres. Width and height are ignored for a round void.</summary>
        public readonly struct Size
        {
            /// <summary>Width (rectangular).</summary>
            public double WidthMm { get; }
            /// <summary>Height (rectangular).</summary>
            public double HeightMm { get; }
            /// <summary>Diameter (circular).</summary>
            public double DiameterMm { get; }
            /// <summary>Depth.</summary>
            public double DepthMm { get; }

            /// <summary>Creates a size.</summary>
            public Size(double widthMm, double heightMm, double diameterMm, double depthMm)
            {
                WidthMm = widthMm;
                HeightMm = heightMm;
                DiameterMm = diameterMm;
                DepthMm = depthMm;
            }
        }

        /// <summary>
        /// Builds one family and saves it to <paramref name="path"/>. Call with no transaction open.
        /// </summary>
        /// <param name="application">The Revit application.</param>
        /// <param name="round">True for a circular void, false for a rectangular one.</param>
        /// <param name="parametric">True to add the size parameters and constraints; false for a fixed-size family.</param>
        /// <param name="size">Initial (parametric) or fixed sizes.</param>
        /// <param name="path">Where to save the .rfa.</param>
        /// <param name="problem">Why the build failed; empty on success.</param>
        public static bool TryBuild(RevitApplication application, bool round, bool parametric, Size size, string path, out string problem)
        {
            problem = string.Empty;
            Document family = null;
            try
            {
                family = application.NewFamilyDocument(FindTemplate(application));
                if (family == null)
                {
                    problem = "Revit could not create a family document from the Generic Model template.";
                    return false;
                }

                Extrusion extrusion;
                using (var transaction = new Transaction(family, "Build void family"))
                {
                    transaction.Start();
                    extrusion = Build(application, family, round, parametric, size);
                    transaction.Commit();
                }

                if (parametric && !Flexes(family, extrusion, round, size, out problem))
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                family.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                return true;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException || ex is IOException)
            {
                problem = ex.Message;
                return false;
            }
            finally
            {
                try { family?.Close(false); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* already closed */ }
            }
        }

        private static string FindTemplate(RevitApplication application)
        {
            string folder = application.FamilyTemplatePath;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new InvalidOperationException(
                    "The Revit family template folder was not found. Install the Revit family templates, or choose a custom void family.");

            List<string> templates = Directory.EnumerateFiles(folder, "*.rft", SearchOption.AllDirectories).ToList();
            string[] exact = { "Metric Generic Model.rft", "Generic Model.rft" };
            string template = templates.FirstOrDefault(t => exact.Contains(Path.GetFileName(t), StringComparer.OrdinalIgnoreCase));
            if (template == null)
                throw new InvalidOperationException(
                    $"No 'Metric Generic Model.rft' template was found under {folder}. Install the Revit family templates, or choose a custom void family.");
            return template;
        }

        private static Extrusion Build(RevitApplication application, Document family, bool round, bool parametric, Size size)
        {
            Family owner = family.OwnerFamily;
            SetInteger(owner, BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS, 1);
            SetInteger(owner, BuiltInParameter.FAMILY_WORK_PLANE_BASED, 1);
            SetInteger(owner, BuiltInParameter.FAMILY_ALWAYS_VERTICAL, 0);

            FamilyManager manager = family.FamilyManager;
            if (manager.CurrentType == null)
                manager.NewType("Void");

            double depth = UnitsMm.ToFeet(size.DepthMm);
            double halfWidth = UnitsMm.ToFeet(round ? size.DiameterMm : size.WidthMm) / 2;
            double halfHeight = UnitsMm.ToFeet(round ? size.DiameterMm : size.HeightMm) / 2;

            SketchPlane sketchPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
            var loop = new CurveArray();
            if (round)
            {
                // Two half circles: a sketch loop made of one closed arc is not accepted everywhere.
                loop.Append(Arc.Create(XYZ.Zero, halfWidth, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                loop.Append(Arc.Create(XYZ.Zero, halfWidth, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
            }
            else
            {
                XYZ p1 = new XYZ(-halfWidth, -halfHeight, 0), p2 = new XYZ(halfWidth, -halfHeight, 0);
                XYZ p3 = new XYZ(halfWidth, halfHeight, 0), p4 = new XYZ(-halfWidth, halfHeight, 0);
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p4));
                loop.Append(Line.CreateBound(p4, p1));
            }
            var profile = new CurveArrArray();
            profile.Append(loop);

            Extrusion extrusion = family.FamilyCreate.NewExtrusion(false, profile, sketchPlane, depth);
            extrusion.StartOffset = -depth / 2;
            extrusion.EndOffset = depth / 2;
            family.Regenerate();

            if (parametric)
            {
                AddDepthParameters(manager, extrusion, depth);
                View view = new FilteredElementCollector(family).OfClass(typeof(ViewPlan)).Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                    ?? throw new InvalidOperationException("The family template has no plan view to place dimensions in.");
                if (round)
                    ConstrainCircle(family, manager, extrusion, view, halfWidth);
                else
                    ConstrainRectangle(family, manager, extrusion, view, halfWidth, halfHeight);
            }

            AddDataParameters(application, manager);
            return extrusion;
        }

        private static void AddDepthParameters(FamilyManager manager, Extrusion extrusion, double depth)
        {
            FamilyParameter depthParameter = AddLength(manager, VoidFamilySpec.Depth);
            FamilyParameter start = AddLength(manager, "Void_DepthStart");
            FamilyParameter end = AddLength(manager, "Void_DepthEnd");
            manager.Set(depthParameter, depth);
            manager.SetFormula(end, VoidFamilySpec.Depth + " / 2");
            manager.SetFormula(start, "-" + VoidFamilySpec.Depth + " / 2");
            manager.AssociateElementParameterToFamilyParameter(extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), start);
            manager.AssociateElementParameterToFamilyParameter(extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), end);
        }

        private static void ConstrainCircle(Document family, FamilyManager manager, Extrusion extrusion, View view, double radius)
        {
            FamilyParameter diameter = AddLength(manager, VoidFamilySpec.Diameter);
            manager.Set(diameter, radius * 2);

            Reference arc = extrusion.Sketch.GetAllElements()
                .Select(id => family.GetElement(id)).OfType<ModelArc>()
                .Select(a => a.GeometryCurve.Reference).FirstOrDefault(r => r != null)
                ?? throw new InvalidOperationException("The circle of the void sketch has no reference to dimension.");

            Dimension dimension = family.FamilyCreate.NewDiameterDimension(view, arc, new XYZ(radius, 0, 0));
            dimension.FamilyLabel = diameter;
        }

        private static void ConstrainRectangle(Document family, FamilyManager manager, Extrusion extrusion, View view,
            double halfWidth, double halfHeight)
        {
            FamilyParameter width = AddLength(manager, VoidFamilySpec.Width);
            FamilyParameter height = AddLength(manager, VoidFamilySpec.Height);
            manager.Set(width, halfWidth * 2);
            manager.Set(height, halfHeight * 2);

            double reach = Math.Max(halfWidth, halfHeight) * 3 + 1;
            ReferencePlane left = family.FamilyCreate.NewReferencePlane(new XYZ(-halfWidth, -reach, 0), new XYZ(-halfWidth, reach, 0), XYZ.BasisZ, view);
            ReferencePlane right = family.FamilyCreate.NewReferencePlane(new XYZ(halfWidth, -reach, 0), new XYZ(halfWidth, reach, 0), XYZ.BasisZ, view);
            ReferencePlane front = family.FamilyCreate.NewReferencePlane(new XYZ(-reach, -halfHeight, 0), new XYZ(reach, -halfHeight, 0), XYZ.BasisZ, view);
            ReferencePlane back = family.FamilyCreate.NewReferencePlane(new XYZ(-reach, halfHeight, 0), new XYZ(reach, halfHeight, 0), XYZ.BasisZ, view);
            family.Regenerate();

            // Lock each side face of the void to its reference plane.
            family.FamilyCreate.NewAlignment(view, left.GetReference(), SideFace(extrusion, -XYZ.BasisX).Reference);
            family.FamilyCreate.NewAlignment(view, right.GetReference(), SideFace(extrusion, XYZ.BasisX).Reference);
            family.FamilyCreate.NewAlignment(view, front.GetReference(), SideFace(extrusion, -XYZ.BasisY).Reference);
            family.FamilyCreate.NewAlignment(view, back.GetReference(), SideFace(extrusion, XYZ.BasisY).Reference);

            ReferencePlane centreX = CentrePlane(family, XYZ.BasisX, left, right);
            ReferencePlane centreY = CentrePlane(family, XYZ.BasisY, front, back);

            Line acrossX = Line.CreateBound(new XYZ(-reach, reach * 0.8, 0), new XYZ(reach, reach * 0.8, 0));
            Line acrossXEq = Line.CreateBound(new XYZ(-reach, reach * 0.6, 0), new XYZ(reach, reach * 0.6, 0));
            Line acrossY = Line.CreateBound(new XYZ(reach * 0.8, -reach, 0), new XYZ(reach * 0.8, reach, 0));
            Line acrossYEq = Line.CreateBound(new XYZ(reach * 0.6, -reach, 0), new XYZ(reach * 0.6, reach, 0));

            // Keep the void centred on the origin, then drive the overall size with the parameter.
            family.FamilyCreate.NewLinearDimension(view, acrossXEq, References(left, centreX, right)).AreSegmentsEqual = true;
            family.FamilyCreate.NewLinearDimension(view, acrossYEq, References(front, centreY, back)).AreSegmentsEqual = true;
            family.FamilyCreate.NewLinearDimension(view, acrossX, References(left, right)).FamilyLabel = width;
            family.FamilyCreate.NewLinearDimension(view, acrossY, References(front, back)).FamilyLabel = height;
        }

        private static ReferenceArray References(params ReferencePlane[] planes)
        {
            var references = new ReferenceArray();
            foreach (ReferencePlane plane in planes)
                references.Append(plane.GetReference());
            return references;
        }

        /// <summary>The template's own reference plane through the origin with the given normal (found by geometry, not by its localised name).</summary>
        private static ReferencePlane CentrePlane(Document family, XYZ normal, params ReferencePlane[] exclude)
        {
            var excluded = new HashSet<ElementId>(exclude.Select(p => p.Id));
            return new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                .FirstOrDefault(p => !excluded.Contains(p.Id)
                    && Math.Abs(Math.Abs(p.Normal.DotProduct(normal)) - 1) < 1e-6
                    && Math.Abs(p.BubbleEnd.DotProduct(normal)) < 1e-6)
                ?? throw new InvalidOperationException("The family template has no centre reference plane to keep the void centred.");
        }

        private static PlanarFace SideFace(Extrusion extrusion, XYZ normal)
        {
            var options = new Options { ComputeReferences = true };
            foreach (GeometryObject item in extrusion.get_Geometry(options))
                if (item is Solid solid)
                    foreach (Face face in solid.Faces)
                        if (face is PlanarFace planar && planar.FaceNormal.IsAlmostEqualTo(normal))
                            return planar;
            throw new InvalidOperationException("A side face of the void was not found.");
        }

        /// <summary>Changes each size parameter and measures the void. False means the family does not flex correctly.</summary>
        private static bool Flexes(Document family, Extrusion extrusion, bool round, Size size, out string problem)
        {
            problem = string.Empty;
            FamilyManager manager = family.FamilyManager;
            var checks = new List<(string Name, double Target, Func<BoundingBoxXYZ, double> Measure)>
            {
                (VoidFamilySpec.Depth, UnitsMm.ToFeet(size.DepthMm + 150), b => b.Max.Z - b.Min.Z),
            };
            if (round)
            {
                checks.Add((VoidFamilySpec.Diameter, UnitsMm.ToFeet(size.DiameterMm + 120), b => b.Max.X - b.Min.X));
            }
            else
            {
                checks.Add((VoidFamilySpec.Width, UnitsMm.ToFeet(size.WidthMm + 120), b => b.Max.X - b.Min.X));
                checks.Add((VoidFamilySpec.Height, UnitsMm.ToFeet(size.HeightMm + 80), b => b.Max.Y - b.Min.Y));
            }

            using (var transaction = new Transaction(family, "Check void family"))
            {
                transaction.Start();
                foreach (var check in checks)
                {
                    FamilyParameter parameter = manager.get_Parameter(check.Name);
                    manager.Set(parameter, check.Target);
                    family.Regenerate();
                    BoundingBoxXYZ box = extrusion.get_BoundingBox(null);
                    double tolerance = UnitsMm.ToFeet(0.5);
                    bool sized = box != null && Math.Abs(check.Measure(box) - check.Target) < tolerance;
                    bool centred = box != null && Math.Abs(box.Max.X + box.Min.X) < tolerance
                        && Math.Abs(box.Max.Y + box.Min.Y) < tolerance && Math.Abs(box.Max.Z + box.Min.Z) < tolerance;
                    if (!sized || !centred)
                    {
                        problem = $"the void does not follow the {check.Name} parameter";
                        transaction.RollBack();
                        return false;
                    }
                }
                transaction.RollBack();
            }
            return true;
        }

        /// <summary>
        /// Adds the text parameters as shared parameters (so they can be scheduled and tagged), through a
        /// temporary shared-parameter file. If that is not possible they are added as family parameters.
        /// </summary>
        private static void AddDataParameters(RevitApplication application, FamilyManager manager)
        {
            string previousFile = application.SharedParametersFilename;
            string tempFile = Path.Combine(Path.GetTempPath(), "VoidOpenings_SharedParameters_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(tempFile, string.Empty);
                application.SharedParametersFilename = tempFile;
                DefinitionGroup group = application.OpenSharedParameterFile().Groups.Create("Void Openings");
                foreach (KeyValuePair<string, Guid> item in VoidFamilySpec.DataParameters)
                {
                    var options = new ExternalDefinitionCreationOptions(item.Key, SpecTypeId.String.Text) { GUID = item.Value };
                    var definition = (ExternalDefinition)group.Definitions.Create(options);
                    manager.AddParameter(definition, GroupTypeId.IdentityData, true);
                }
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                foreach (KeyValuePair<string, Guid> item in VoidFamilySpec.DataParameters)
                    if (manager.get_Parameter(item.Key) == null)
                        manager.AddParameter(item.Key, GroupTypeId.IdentityData, SpecTypeId.String.Text, true);
            }
            finally
            {
                try { application.SharedParametersFilename = previousFile ?? string.Empty; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* leave Revit's setting as it is */ }
                try { File.Delete(tempFile); }
                catch (IOException) { /* temp file; harmless */ }
            }
        }

        private static FamilyParameter AddLength(FamilyManager manager, string name)
            => manager.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, true);

        private static void SetInteger(Element element, BuiltInParameter id, int value)
        {
            Parameter parameter = element.get_Parameter(id);
            if (parameter != null && !parameter.IsReadOnly)
                parameter.Set(value);
        }
    }
}
