using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>
    /// Marks the views and section markers the tool creates, in Extensible Storage (views have no
    /// Comments parameter). Fields: RunId, RoomKey (for a drafting view: the name it was asked for),
    /// Kind, OwnerUniqueId. Schema version 1: never change this GUID or its fields; a new layout
    /// gets a new schema and a migration.
    /// </summary>
    public static class ElementTag
    {
        /// <summary>Schema GUID of version 1.</summary>
        public static readonly Guid SchemaGuid = new("6f0c1c1e-7b1d-4d59-9a53-2f2f0e6a4b71");

        /// <summary>Kind text for a room plan.</summary>
        public const string Plan = "Plan";
        /// <summary>Kind text for a reference section marker.</summary>
        public const string Section = "Section";
        /// <summary>Kind text for a drafting view the tool created.</summary>
        public const string Drafting = "Drafting";

        private const string SchemaName = "WetAreaWaterproofing_Tag_V1";
        private const string VendorId = "RAFI_Revit26_Plugin";
        private const string FieldRunId = "RunId";
        private const string FieldRoomKey = "RoomKey";
        private const string FieldKind = "Kind";
        private const string FieldOwner = "OwnerUniqueId";

        private static Schema GetOrCreate()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldRunId, typeof(string));
            builder.AddSimpleField(FieldRoomKey, typeof(string));
            builder.AddSimpleField(FieldKind, typeof(string));
            builder.AddSimpleField(FieldOwner, typeof(string));
            return builder.Finish();
        }

        /// <summary>Writes the tag. Needs an open transaction.</summary>
        public static void Set(Element element, string runId, string roomKey, string kind)
        {
            if (element == null) return;
            Schema schema = GetOrCreate();
            var entity = new Entity(schema);
            entity.Set(FieldRunId, runId ?? string.Empty);
            entity.Set(FieldRoomKey, roomKey ?? string.Empty);
            entity.Set(FieldKind, kind ?? string.Empty);
            entity.Set(FieldOwner, element.UniqueId); // a copy of the element keeps this value and is then ignored
            element.SetEntity(entity);
        }

        /// <summary>
        /// Reads the tag; false when the element has none, or when the tag was written for another
        /// element (Revit copies the tag when a view is duplicated or a marker is copied: such copies
        /// are the user's, not the tool's).
        /// </summary>
        public static bool TryGet(Element element, out string runId, out string roomKey, out string kind)
        {
            runId = roomKey = kind = null;
            Schema schema = Schema.Lookup(SchemaGuid);
            if (element == null || schema == null) return false;
            Entity entity = element.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return false;
            if (entity.Get<string>(FieldOwner) != element.UniqueId) return false;
            runId = entity.Get<string>(FieldRunId);
            roomKey = entity.Get<string>(FieldRoomKey);
            kind = entity.Get<string>(FieldKind);
            return true;
        }

        /// <summary>Every element in <paramref name="doc"/> that carries the tag.</summary>
        public static FilteredElementCollector Tagged(Document doc)
            => new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(SchemaGuid));
    }
}
