using System;
using System.IO;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Creates or finds the EPS_Serial shared parameter and binds it to the Roofs category.</summary>
    public static class EpsParameterService
    {
        public const string ParameterName = "EPS_Serial";
        private const string GroupName = "EPS Insulation";
        private const string SharedParamFileName = "EPS_SharedParams.txt";

        /// <summary>
        /// Ensures the EPS_Serial shared parameter exists and is bound to the Roofs category.
        /// Returns the <see cref="Definition"/> for use in setting values, or null on failure.
        /// </summary>
        public static Definition EnsureParameter(Document doc)
        {
            // Check if already bound
            Definition existing = FindBoundDefinition(doc, ParameterName);
            if (existing != null) return existing;

            // Build path to our shared parameter file
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "Revit26_Plugin_Revised", "EpsSteppedInsulationLayout");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            string spFilePath = Path.Combine(folder, SharedParamFileName);

            // Save and restore original shared parameter file
            string originalFile = doc.Application.SharedParametersFilename;
            try
            {
                // Create the shared param file if missing
                if (!File.Exists(spFilePath))
                    File.WriteAllText(spFilePath, "");

                doc.Application.SharedParametersFilename = spFilePath;
                DefinitionFile defFile = doc.Application.OpenSharedParameterFile();
                if (defFile == null) return null;

                // Get or create group
                DefinitionGroup group = defFile.Groups.get_Item(GroupName)
                                        ?? defFile.Groups.Create(GroupName);

                // Get or create definition
                ExternalDefinition extDef = group.Definitions.get_Item(ParameterName) as ExternalDefinition;
                if (extDef == null)
                {
                    var options = new ExternalDefinitionCreationOptions(ParameterName, SpecTypeId.String.Text)
                    {
                        Description = "EPS Stepped Insulation serial number assigned during panel placement.",
                        UserModifiable = true,
                        Visible = true
                    };
                    extDef = group.Definitions.Create(options) as ExternalDefinition;
                }

                if (extDef == null) return null;

                // Bind to Roofs category
                var categories = doc.Application.Create.NewCategorySet();
                Category roofsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Roofs);
                categories.Insert(roofsCat);

                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categories);
                doc.ParameterBindings.Insert(extDef, binding, GroupTypeId.Data);

                return extDef;
            }
            finally
            {
                doc.Application.SharedParametersFilename = originalFile ?? "";
            }
        }

        /// <summary>Sets the EPS_Serial value on a roof element. Returns false if the parameter is not found.</summary>
        public static bool SetSerial(Element element, Definition definition, string serial)
        {
            if (definition == null) return false;
            Parameter param = element.get_Parameter(definition);
            if (param == null || param.IsReadOnly) return false;
            param.Set(serial);
            return true;
        }

        private static Definition FindBoundDefinition(Document doc, string name)
        {
            BindingMap map = doc.ParameterBindings;
            var iter = map.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key?.Name == name)
                    return iter.Key;
            }
            return null;
        }
    }
}
