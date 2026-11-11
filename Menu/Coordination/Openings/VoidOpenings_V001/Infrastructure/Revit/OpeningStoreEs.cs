using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Keeps the tool's data in the model with Extensible Storage: one record on each void instance, and
    /// one Job ID counter per model. Schema GUIDs and fields are permanent; a format change gets a new schema.
    /// </summary>
    public static class OpeningStoreEs
    {
        private static readonly Guid RecordSchemaId = new Guid("6f1b0c4e-3a52-4d0e-9c1a-7d5e2b8a4f10");
        private static readonly Guid CounterSchemaId = new Guid("b2d94a77-5e61-4c3f-8a0b-1f6c3e9d2a45");
        private const string DataField = "Data";
        private const string JobIdField = "JobId";
        private const string YearField = "Year";
        private const string LastIssuedField = "LastIssued";

        /// <summary>The records of all void instances in <paramref name="document"/>, each with the instance's current geometry.</summary>
        public static List<StoredOpening> ReadAll(Document document)
        {
            var records = new List<StoredOpening>();
            Schema schema = Schema.Lookup(RecordSchemaId);
            if (schema == null)
                return records;

            foreach (FamilyInstance instance in new FilteredElementCollector(document)
                .OfClass(typeof(FamilyInstance)).WherePasses(new ExtensibleStorageFilter(RecordSchemaId)).Cast<FamilyInstance>())
            {
                Entity entity = instance.GetEntity(schema);
                if (!entity.IsValid())
                    continue;

                StoredOpening record;
                try { record = JsonSerializer.Deserialize<StoredOpening>(entity.Get<string>(DataField)); }
                catch (JsonException) { continue; }
                if (record == null || string.IsNullOrEmpty(record.ClashKey))
                    continue;

                record.InstanceUniqueId = instance.UniqueId;
                record.CurrentSignature = CurrentSignature(instance, record);
                records.Add(record);
            }
            return records;
        }

        /// <summary>Writes the record onto a void instance. Call inside a transaction.</summary>
        public static void Write(FamilyInstance instance, StoredOpening record)
        {
            Schema schema = Schema.Lookup(RecordSchemaId) ?? BuildRecordSchema();
            var entity = new Entity(schema);
            entity.Set(DataField, JsonSerializer.Serialize(record));
            entity.Set(JobIdField, record.JobId ?? string.Empty);
            instance.SetEntity(entity);
        }

        /// <summary>The last Job ID number issued in <paramref name="year"/> according to the model's counter; 0 when there is none.</summary>
        public static int ReadCounter(Document document, int year)
        {
            Schema schema = Schema.Lookup(CounterSchemaId);
            DataStorage storage = schema == null ? null : FindCounter(document, schema);
            if (storage == null)
                return 0;
            Entity entity = storage.GetEntity(schema);
            return entity.Get<int>(YearField) == year ? entity.Get<int>(LastIssuedField) : 0;
        }

        /// <summary>Stores the last issued Job ID number. Call inside a transaction.</summary>
        public static void WriteCounter(Document document, int year, int lastIssued)
        {
            Schema schema = Schema.Lookup(CounterSchemaId) ?? BuildCounterSchema();
            DataStorage storage = FindCounter(document, schema) ?? DataStorage.Create(document);
            var entity = new Entity(schema);
            entity.Set(YearField, year);
            entity.Set(LastIssuedField, lastIssued);
            storage.SetEntity(entity);
        }

        /// <summary>The counter element, so the caller can check it is editable in a workshared model; null when not created yet.</summary>
        public static Element CounterElement(Document document)
        {
            Schema schema = Schema.Lookup(CounterSchemaId);
            return schema == null ? null : FindCounter(document, schema);
        }

        /// <summary>
        /// Geometry of a void instance as it is now, in its model's coordinates: position and axes from
        /// the instance, sizes from its parameters (or from the record for a fixed-size family).
        /// </summary>
        public static string CurrentSignature(FamilyInstance instance, StoredOpening record)
        {
            Transform transform = instance.GetTransform();
            bool circular = string.Equals(record.Shape, nameof(OpeningShape.Circular), StringComparison.OrdinalIgnoreCase);
            return new OpeningSignature(
                UnitsMm.Point(transform.Origin, null),
                UnitsMm.Direction(transform.BasisZ, null),
                UnitsMm.Direction(transform.BasisX, null),
                circular ? OpeningShape.Circular : OpeningShape.Rectangular,
                circular ? 0 : Size(instance, VoidFamilySpec.Width, record.WidthMm),
                circular ? 0 : Size(instance, VoidFamilySpec.Height, record.HeightMm),
                circular ? Size(instance, VoidFamilySpec.Diameter, record.DiameterMm) : 0,
                Size(instance, VoidFamilySpec.Depth, record.DepthMm)).ToString();
        }

        private static double Size(FamilyInstance instance, string parameterName, double fallbackMm)
        {
            Parameter parameter = instance.LookupParameter(parameterName);
            return parameter != null && parameter.StorageType == StorageType.Double ? UnitsMm.ToMm(parameter.AsDouble()) : fallbackMm;
        }

        private static DataStorage FindCounter(Document document, Schema schema)
            => new FilteredElementCollector(document).OfClass(typeof(DataStorage))
                .WherePasses(new ExtensibleStorageFilter(schema.GUID)).Cast<DataStorage>().FirstOrDefault();

        private static Schema BuildRecordSchema()
        {
            var builder = new SchemaBuilder(RecordSchemaId);
            builder.SetSchemaName("VoidOpeningsRecordV1");
            builder.SetDocumentation("Void Openings: the record of one generated opening (JSON).");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(DataField, typeof(string));
            builder.AddSimpleField(JobIdField, typeof(string));
            return builder.Finish();
        }

        private static Schema BuildCounterSchema()
        {
            var builder = new SchemaBuilder(CounterSchemaId);
            builder.SetSchemaName("VoidOpeningsCounterV1");
            builder.SetDocumentation("Void Openings: last issued Job ID number of a year.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(YearField, typeof(int));
            builder.AddSimpleField(LastIssuedField, typeof(int));
            return builder.Finish();
        }
    }
}
