using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// <see cref="IVoidWriter"/> for the active Revit model: places void instances, cuts the structural
    /// element where Revit allows it, checks that the cut really removed material, assigns Job IDs and
    /// stores the records. Each opening has its own transaction, so one failure rolls back alone; the
    /// whole run is one undo step.
    /// </summary>
    public sealed class RevitVoidWriter : IVoidWriter
    {
        private readonly RevitContext _context;

        /// <summary>Creates the writer.</summary>
        public RevitVoidWriter(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IReadOnlyList<PlacementOutcome> Place(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings, ICollection<string> notes)
        {
            Document document = _context.RequireProject();
            if (document.IsReadOnly)
                throw new InvalidOperationException("The active model is read-only, so no voids can be placed.");
            if (document.IsModifiable)
                throw new InvalidOperationException("Another Revit command is still running. Finish it, then try again.");

            Dictionary<string, StoredOpening> existing = OpeningStoreEs.ReadAll(document)
                .GroupBy(r => r.ClashKey).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            int year = DateTime.Now.Year;
            int lastIssued = HighestIssued(document, existing.Values, year);

            // Openings that already have an unchanged void need no family and no transaction.
            var outcomes = new PlacementOutcome[placements.Count];
            var toPlace = new List<int>();
            for (int i = 0; i < placements.Count; i++)
            {
                if (existing.TryGetValue(placements[i].ClashKey ?? string.Empty, out StoredOpening record) && IsUnchanged(placements[i], record))
                    outcomes[i] = new PlacementOutcome
                    {
                        ClashKey = placements[i].ClashKey, JobId = record.JobId, InstanceUniqueId = record.InstanceUniqueId,
                        Status = "Existing", Message = "An unchanged void already exists for this clash.",
                    };
                else
                    toPlace.Add(i);
            }
            if (toPlace.Count == 0)
                return outcomes;

            var families = new VoidFamilyProvider(document, settings, notes);
            families.Prepare(toPlace.Select(i => placements[i]));

            using (var group = new TransactionGroup(document, "Void Openings: generate voids"))
            {
                group.Start();
                foreach (int i in toPlace)
                {
                    VoidPlacement placement = placements[i];
                    existing.TryGetValue(placement.ClashKey ?? string.Empty, out StoredOpening previous);
                    string jobId = previous?.JobId ?? JobIdGenerator.Format(year, lastIssued + 1);
                    try
                    {
                        outcomes[i] = PlaceOne(document, families, placement, previous, jobId);
                    }
                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
                    {
                        outcomes[i] = Failed(placement, ex.Message);
                    }
                    if (previous == null && outcomes[i].Status != "Failed")
                        lastIssued++;
                }

                using (var transaction = new Transaction(document, "Void Openings: Job ID counter"))
                {
                    transaction.Start();
                    OpeningStoreEs.WriteCounter(document, year, lastIssued);
                    transaction.Commit();
                }
                group.Assimilate();
            }
            return outcomes;
        }

        private static int HighestIssued(Document document, IEnumerable<StoredOpening> records, int year)
        {
            Element counter = OpeningStoreEs.CounterElement(document);
            if (counter != null && document.IsWorkshared
                && WorksharingUtils.GetCheckoutStatus(document, counter.Id) == CheckoutStatus.OwnedByOtherUser)
                throw new InvalidOperationException(
                    "The Job ID counter of this model is being edited by another user. Ask them to synchronise, reload latest, then try again.");

            string next = JobIdGenerator.Next(records.Select(r => r.JobId), year, OpeningStoreEs.ReadCounter(document, year));
            JobIdGenerator.TryParse(next, out _, out int number);
            return number - 1;
        }

        private static bool IsUnchanged(VoidPlacement placement, StoredOpening record)
            => OpeningSignature.TryParse(record.Signature, out OpeningSignature generatedWith)
            && OpeningSignature.TryParse(record.CurrentSignature, out OpeningSignature current)
            && generatedWith.Matches(SignatureOf(placement))
            && current.Matches(generatedWith);

        private static OpeningSignature SignatureOf(VoidPlacement p) => new OpeningSignature(
            new Vec3(p.CenterX, p.CenterY, p.CenterZ), new Vec3(p.AxisX, p.AxisY, p.AxisZ),
            new Vec3(p.WidthDirX, p.WidthDirY, p.WidthDirZ),
            IsRound(p) ? OpeningShape.Circular : OpeningShape.Rectangular, p.WidthMm, p.HeightMm, p.DiameterMm, p.DepthMm);

        private static bool IsRound(VoidPlacement p)
            => string.Equals(p.Shape, nameof(OpeningShape.Circular), StringComparison.OrdinalIgnoreCase);

        private static PlacementOutcome Failed(VoidPlacement placement, string message)
            => new PlacementOutcome { ClashKey = placement.ClashKey, Status = "Failed", Message = message };

        private static PlacementOutcome PlaceOne(Document document, VoidFamilyProvider families, VoidPlacement placement,
            StoredOpening previous, string jobId)
        {
            Element structural = string.IsNullOrEmpty(placement.StructuralUniqueId) ? null : document.GetElement(placement.StructuralUniqueId);
            if (structural == null)
                return Failed(placement, "The structural element is not in this model (deleted, or the package is for another file).");

            if (document.IsWorkshared && WorksharingUtils.GetCheckoutStatus(document, structural.Id) == CheckoutStatus.OwnedByOtherUser)
                return Failed(placement, "The structural element is owned by another user.");

            string cutNote = string.Empty;
            bool tryCut = true;
            if (structural.GroupId != ElementId.InvalidElementId)
            {
                tryCut = false;
                cutNote = "The structural element is in a group, so it was not cut.";
            }
            else if (!InstanceVoidCutUtils.CanBeCutWithVoid(structural))
            {
                tryCut = false;
                cutNote = "Revit cannot cut this element with a void family; the void was placed as a marker.";
            }

            var failures = new FailureCollector();
            using (var transaction = new Transaction(document, "Void Openings: " + jobId))
            {
                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(failures);
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);
                transaction.Start();

                if (previous != null && !string.IsNullOrEmpty(previous.InstanceUniqueId)
                    && document.GetElement(previous.InstanceUniqueId) is Element old)
                    document.Delete(old.Id);

                FamilySymbol symbol = families.SymbolFor(placement);
                if (!symbol.IsActive)
                    symbol.Activate();

                double volumeBefore = tryCut ? Volume(structural) : 0;
                FamilyInstance instance = PlaceInstance(document, symbol, placement);
                SetSizes(instance, placement);
                document.Regenerate();

                bool cut = false;
                if (tryCut)
                {
                    InstanceVoidCutUtils.AddInstanceVoidCut(document, structural, instance);
                    document.Regenerate();
                    cut = InstanceVoidCutUtils.InstanceVoidCutExists(structural, instance)
                          && Volume(structural) < volumeBefore - 1e-9;
                    if (!cut)
                        cutNote = "The void was placed and the cut was added, but the element's volume did not go down. Check that the void reaches the element.";
                }

                string status = cut ? "Cut" : "Generated";
                StoredOpening record = RecordOf(placement, jobId, status);
                SetData(instance, placement, jobId, status);
                OpeningStoreEs.Write(instance, record);

                if (transaction.Commit() != TransactionStatus.Committed)
                    return Failed(placement, failures.Messages.Count > 0
                        ? string.Join(" ", failures.Messages.Distinct())
                        : "Revit rolled the change back.");

                return new PlacementOutcome
                {
                    ClashKey = placement.ClashKey, JobId = jobId, InstanceUniqueId = instance.UniqueId, Status = status, Message = cutNote,
                };
            }
        }

        private static FamilyInstance PlaceInstance(Document document, FamilySymbol symbol, VoidPlacement p)
        {
            XYZ origin = UnitsMm.ToXyz(new Vec3(p.CenterX, p.CenterY, p.CenterZ));
            XYZ axis = new XYZ(p.AxisX, p.AxisY, p.AxisZ).Normalize();
            XYZ widthDirection = new XYZ(p.WidthDirX, p.WidthDirY, p.WidthDirZ);
            widthDirection = (widthDirection - axis * widthDirection.DotProduct(axis)).Normalize();
            XYZ heightDirection = axis.CrossProduct(widthDirection).Normalize();

            FamilyInstance instance;
            if (symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
            {
                // A work plane whose normal is the cut direction lets the void point any way, including sloped.
                SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByOriginAndBasis(origin, widthDirection, heightDirection));
                instance = document.Create.NewFamilyInstance(sketchPlane.GetPlaneReference(), origin, widthDirection, symbol);
            }
            else
            {
                if (Math.Abs(axis.DotProduct(XYZ.BasisZ)) < 0.999)
                    throw new InvalidOperationException(
                        $"The family {symbol.Family.Name} is not work-plane-based, so it can only be placed for vertical openings. Use a work-plane-based family.");
                instance = document.Create.NewFamilyInstance(origin, symbol, StructuralType.NonStructural);
            }
            document.Regenerate();

            // Turn the instance about the cut direction until its width points the right way, then fix any drift.
            XYZ currentX = instance.GetTransform().BasisX;
            double angle = Math.Atan2(currentX.CrossProduct(widthDirection).DotProduct(axis), currentX.DotProduct(widthDirection));
            if (Math.Abs(angle) > 1e-6)
                ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateUnbound(origin, axis), angle);
            document.Regenerate();

            XYZ drift = origin - instance.GetTransform().Origin;
            if (drift.GetLength() > 1e-6)
                ElementTransformUtils.MoveElement(document, instance.Id, drift);
            return instance;
        }

        private static void SetSizes(FamilyInstance instance, VoidPlacement p)
        {
            SetLength(instance, VoidFamilySpec.Depth, p.DepthMm);
            if (IsRound(p))
            {
                SetLength(instance, VoidFamilySpec.Diameter, p.DiameterMm);
            }
            else
            {
                SetLength(instance, VoidFamilySpec.Width, p.WidthMm);
                SetLength(instance, VoidFamilySpec.Height, p.HeightMm);
            }
        }

        private static void SetLength(Element element, string name, double mm)
        {
            Parameter parameter = element.LookupParameter(name);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                parameter.Set(UnitsMm.ToFeet(mm));
        }

        private static void SetData(FamilyInstance instance, VoidPlacement p, string jobId, string status)
        {
            SetText(instance, VoidFamilySpec.JobId, jobId);
            SetText(instance, VoidFamilySpec.ClashId, p.ClashId);
            SetText(instance, VoidFamilySpec.MepElementId, p.MepElementId.ToString(CultureInfo.InvariantCulture));
            SetText(instance, VoidFamilySpec.StructuralElementId, p.StructuralElementId.ToString(CultureInfo.InvariantCulture));
            SetText(instance, VoidFamilySpec.MepCategory, DisplayName<MepCategory>(p.MepCategory, CategoryCatalog.NameOf));
            SetText(instance, VoidFamilySpec.StructuralCategory, DisplayName<StructuralCategory>(p.StructuralCategory, CategoryCatalog.NameOf));
            SetText(instance, VoidFamilySpec.CreatedOn, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            SetText(instance, VoidFamilySpec.Status, status);
        }

        private static string DisplayName<TEnum>(string value, Func<TEnum, string> nameOf) where TEnum : struct
            => Enum.TryParse(value, true, out TEnum parsed) ? nameOf(parsed) : (value ?? string.Empty);

        private static void SetText(Element element, string name, string value)
        {
            Parameter parameter = element.LookupParameter(name);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                parameter.Set(value ?? string.Empty);
        }

        private static StoredOpening RecordOf(VoidPlacement p, string jobId, string status) => new StoredOpening
        {
            ClashKey = p.ClashKey, ClashId = p.ClashId, JobId = jobId, Status = status,
            Signature = SignatureOf(p).ToString(),
            StructuralModelKey = p.StructuralModelKey, StructuralModelName = p.StructuralModelName,
            StructuralUniqueId = p.StructuralUniqueId, StructuralElementId = p.StructuralElementId, StructuralCategory = p.StructuralCategory,
            MepModelKey = p.MepModelKey, MepModelName = p.MepModelName,
            MepUniqueId = p.MepUniqueId, MepElementId = p.MepElementId, MepCategory = p.MepCategory,
            Shape = p.Shape, WidthMm = p.WidthMm, HeightMm = p.HeightMm, DiameterMm = p.DiameterMm,
            DepthMm = p.DepthMm, ClearanceMm = p.ClearanceMm,
            CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };

        private static double Volume(Element element)
        {
            double volume = 0;
            GeometryElement geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            if (geometry != null)
                AddVolume(geometry, ref volume);
            return volume;
        }

        private static void AddVolume(GeometryElement geometry, ref double volume)
        {
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid)
                    volume += solid.Volume;
                else if (item is GeometryInstance instance)
                    AddVolume(instance.GetInstanceGeometry(), ref volume);
            }
        }

        /// <summary>Collects Revit's failure messages for one opening: warnings are dismissed, errors roll the opening back.</summary>
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            public List<string> Messages { get; } = new List<string>();

            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                bool hasError = false;
                foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        continue;
                    }
                    Messages.Add(failure.GetDescriptionText());
                    hasError = true;
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
