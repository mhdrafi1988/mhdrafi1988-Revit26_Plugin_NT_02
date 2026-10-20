using Autodesk.Revit.DB;

namespace BatchDwgFamilyLinker.Services
{
    public static class FamilySaveService
    {
        public static void Save(Document famDoc)
        {
            if (!famDoc.IsReadOnly)
                famDoc.Save();
        }
    }
}
