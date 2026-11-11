using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting
{
    /// <summary>Turns calculated openings into placements and opening packages.</summary>
    public static class OpeningPackageBuilder
    {
        /// <summary>
        /// One placement for an opening. The opening is in host coordinates;
        /// <paramref name="structuralToHost"/> is used to express it in the structural model's coordinates.
        /// </summary>
        public static VoidPlacement ToPlacement(OpeningData opening, Transform3 structuralToHost)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));
            Frame3 frame = structuralToHost.Inverse().Apply(opening.Orientation);
            ClashData clash = opening.Clash;
            return new VoidPlacement
            {
                ClashKey = clash.Key.ToString(),
                ClashId = clash.ClashId,
                Shape = opening.Shape.ToString(),
                CenterX = frame.Origin.X, CenterY = frame.Origin.Y, CenterZ = frame.Origin.Z,
                AxisX = frame.AxisZ.X, AxisY = frame.AxisZ.Y, AxisZ = frame.AxisZ.Z,
                WidthDirX = frame.AxisX.X, WidthDirY = frame.AxisX.Y, WidthDirZ = frame.AxisX.Z,
                WidthMm = opening.WidthMm, HeightMm = opening.HeightMm, DiameterMm = opening.DiameterMm,
                DepthMm = opening.DepthMm, ClearanceMm = opening.ClearanceMm,
                StructuralModelKey = clash.StructuralModel.ModelKey,
                StructuralModelName = clash.StructuralModel.DisplayName,
                StructuralUniqueId = clash.Structural.UniqueId,
                StructuralElementId = clash.Structural.ElementId,
                StructuralCategory = clash.Structural.Category.ToString(),
                MepModelKey = clash.MepModel.ModelKey,
                MepModelName = clash.MepModel.DisplayName,
                MepUniqueId = clash.Mep.UniqueId,
                MepElementId = clash.Mep.ElementId,
                MepCategory = clash.Mep.Category.ToString(),
            };
        }

        /// <summary>A package of openings for a (linked) structural model.</summary>
        public static OpeningPackage Build(ModelRef structural, ModelRef mep, IEnumerable<OpeningData> openings, Transform3 structuralToHost)
            => new OpeningPackage
            {
                StructuralModelKey = structural.ModelKey,
                StructuralModelName = structural.DisplayName,
                MepModelKey = mep.ModelKey,
                MepModelName = mep.DisplayName,
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Openings = openings.Select(o => ToPlacement(o, structuralToHost)).ToList(),
            };
    }

    /// <summary>Opening packages to and from JSON text.</summary>
    public static class PackageSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>Writes a package as JSON.</summary>
        public static string ToJson(OpeningPackage package) => JsonSerializer.Serialize(package, Options);

        /// <summary>Reads a package. Throws <see cref="InvalidOperationException"/> with a message for the user when the text is not a package this version can read.</summary>
        public static OpeningPackage FromJson(string json)
        {
            OpeningPackage package;
            try
            {
                package = JsonSerializer.Deserialize<OpeningPackage>(json ?? string.Empty, Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("The file is not a Void Openings package: " + ex.Message, ex);
            }
            if (package?.Openings == null || string.IsNullOrEmpty(package.StructuralModelKey))
                throw new InvalidOperationException("The file is not a Void Openings package.");
            if (package.Version > OpeningPackage.CurrentVersion)
                throw new InvalidOperationException(
                    $"The package was written by a newer version of the tool (format {package.Version}). Update the plugin to apply it.");
            return package;
        }
    }

    /// <summary>One line of the results table and of the exports.</summary>
    public sealed class ExportRow
    {
        /// <summary>Column headers: the thirteen of the results table first, in their fixed order, then the appended ones.</summary>
        public static IReadOnlyList<string> Headers { get; } = new[]
        {
            "Clash ID", "Job ID", "Structural Model", "Structural Category", "Structural Element", "MEP Model",
            "MEP Category", "MEP Element", "Opening Width", "Opening Height", "Diameter", "Clearance", "Status",
            "Tracking", "Depth", "X", "Y", "Z", "Note",
        };

        /// <summary>Clash id.</summary>
        public string ClashId { get; set; }
        /// <summary>Job ID, empty until generated.</summary>
        public string JobId { get; set; }
        /// <summary>Structural model name.</summary>
        public string StructuralModel { get; set; }
        /// <summary>Structural category name.</summary>
        public string StructuralCategory { get; set; }
        /// <summary>Structural element id.</summary>
        public long StructuralElementId { get; set; }
        /// <summary>MEP model name.</summary>
        public string MepModel { get; set; }
        /// <summary>MEP category name.</summary>
        public string MepCategory { get; set; }
        /// <summary>MEP element id.</summary>
        public long MepElementId { get; set; }
        /// <summary>Width, millimetres (0 for circular).</summary>
        public double WidthMm { get; set; }
        /// <summary>Height, millimetres (0 for circular).</summary>
        public double HeightMm { get; set; }
        /// <summary>Diameter, millimetres (0 for rectangular).</summary>
        public double DiameterMm { get; set; }
        /// <summary>Clearance, millimetres.</summary>
        public double ClearanceMm { get; set; }
        /// <summary>Status text.</summary>
        public string Status { get; set; }
        /// <summary>Tracking text.</summary>
        public string Tracking { get; set; }
        /// <summary>Depth, millimetres.</summary>
        public double DepthMm { get; set; }
        /// <summary>Centre X in host millimetres.</summary>
        public double X { get; set; }
        /// <summary>Centre Y in host millimetres.</summary>
        public double Y { get; set; }
        /// <summary>Centre Z in host millimetres.</summary>
        public double Z { get; set; }
        /// <summary>Review or result note.</summary>
        public string Note { get; set; }

        /// <summary>The cell values in header order.</summary>
        public object[] ToValues() => new object[]
        {
            ClashId, JobId, StructuralModel, StructuralCategory, StructuralElementId, MepModel, MepCategory, MepElementId,
            Math.Round(WidthMm, 1), Math.Round(HeightMm, 1), Math.Round(DiameterMm, 1), Math.Round(ClearanceMm, 1), Status,
            Tracking, Math.Round(DepthMm, 1), Math.Round(X, 1), Math.Round(Y, 1), Math.Round(Z, 1), Note,
        };

        /// <summary>Row for an opening of this run.</summary>
        public static ExportRow From(OpeningData opening)
        {
            ClashData clash = opening.Clash;
            return new ExportRow
            {
                ClashId = clash.ClashId,
                JobId = opening.JobId ?? string.Empty,
                StructuralModel = clash.StructuralModel.DisplayName,
                StructuralCategory = CategoryCatalog.NameOf(clash.Structural.Category),
                StructuralElementId = clash.Structural.ElementId,
                MepModel = clash.MepModel.DisplayName,
                MepCategory = CategoryCatalog.NameOf(clash.Mep.Category),
                MepElementId = clash.Mep.ElementId,
                WidthMm = opening.WidthMm, HeightMm = opening.HeightMm, DiameterMm = opening.DiameterMm,
                ClearanceMm = opening.ClearanceMm,
                Status = opening.Status.ToString(),
                Tracking = TrackingText(opening.Tracking),
                DepthMm = opening.DepthMm,
                X = opening.Center.X, Y = opening.Center.Y, Z = opening.Center.Z,
                Note = string.Join(" ", new[] { opening.ReviewNote, opening.ResultNote }.Where(n => !string.IsNullOrWhiteSpace(n))),
            };
        }

        /// <summary>Row for a stored opening whose clash no longer exists.</summary>
        public static ExportRow FromOrphan(StoredOpening record) => new ExportRow
        {
            ClashId = record.ClashId, JobId = record.JobId,
            StructuralModel = record.StructuralModelName,
            StructuralCategory = Name<StructuralCategory>(record.StructuralCategory, CategoryCatalog.NameOf),
            StructuralElementId = record.StructuralElementId,
            MepModel = record.MepModelName,
            MepCategory = Name<MepCategory>(record.MepCategory, CategoryCatalog.NameOf),
            MepElementId = record.MepElementId,
            WidthMm = record.WidthMm, HeightMm = record.HeightMm, DiameterMm = record.DiameterMm,
            ClearanceMm = record.ClearanceMm, Status = record.Status,
            Tracking = TrackingText(TrackingState.SourceDeleted), DepthMm = record.DepthMm,
            Note = "The clash this opening was made for no longer exists.",
        };

        /// <summary>Tracking state as shown to the user.</summary>
        public static string TrackingText(TrackingState state)
        {
            switch (state)
            {
                case TrackingState.ManuallyModified: return "Manually modified";
                case TrackingState.SourceDeleted: return "Source deleted";
                default: return state.ToString();
            }
        }

        private static string Name<TEnum>(string value, Func<TEnum, string> nameOf) where TEnum : struct
            => Enum.TryParse(value, true, out TEnum parsed) ? nameOf(parsed) : (value ?? string.Empty);
    }
}
