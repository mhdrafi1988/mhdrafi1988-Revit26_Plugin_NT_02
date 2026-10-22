using Autodesk.Revit.DB;
using System;
using System.IO;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Stable identity of a Revit project model, used to file per-model data (saved sets, settings)
    /// so that every local copy of one central model shares the same entry.
    /// Plain properties so it serialises to JSON as is.
    /// </summary>
    public sealed class ModelIdentity
    {
        /// <summary>
        /// Lookup key: <c>cloud:&lt;project&gt;/&lt;model&gt;</c> for cloud models, otherwise
        /// <c>file:&lt;central or file path&gt;</c> in lower case.
        /// </summary>
        public string Key { get; set; }

        /// <summary>Display name: the central (or file) name without extension.</summary>
        public string Name { get; set; }

        /// <summary>User-visible path to open this model again: the local copy or the file itself. Empty for cloud models.</summary>
        public string OpenPath { get; set; }

        /// <summary>User-visible central model path, when the model is workshared on a file server.</summary>
        public string CentralPath { get; set; }

        /// <summary>True for an Autodesk Docs / BIM 360 cloud model.</summary>
        public bool IsCloud { get; set; }

        /// <summary>Cloud region of a cloud model.</summary>
        public string CloudRegion { get; set; }

        /// <summary>Cloud project GUID of a cloud model.</summary>
        public string ProjectGuid { get; set; }

        /// <summary>Cloud model GUID of a cloud model.</summary>
        public string ModelGuid { get; set; }

        /// <summary>
        /// Builds the identity of <paramref name="doc"/>, or returns null when the model has never
        /// been saved (no path) or is a family.
        /// </summary>
        public static ModelIdentity FromDocument(Document doc)
        {
            if (doc == null || !doc.IsValidObject || doc.IsFamilyDocument) return null;

            if (doc.IsModelInCloud)
            {
                ModelPath cloud = doc.GetCloudModelPath();
                Guid project = cloud.GetProjectGUID();
                Guid model = cloud.GetModelGUID();
                return new ModelIdentity
                {
                    Key = $"cloud:{project:D}/{model:D}".ToLowerInvariant(),
                    Name = doc.Title,
                    IsCloud = true,
                    CloudRegion = cloud.Region,
                    ProjectGuid = project.ToString("D"),
                    ModelGuid = model.ToString("D")
                };
            }

            if (string.IsNullOrWhiteSpace(doc.PathName)) return null;

            string central = null;
            if (doc.IsWorkshared)
            {
                try
                {
                    ModelPath centralPath = doc.GetWorksharingCentralModelPath();
                    if (centralPath != null && !centralPath.Empty)
                        central = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Detached or broken central reference: fall back to the file itself.
                }
            }

            string keyPath = string.IsNullOrWhiteSpace(central) ? doc.PathName : central;
            return new ModelIdentity
            {
                Key = "file:" + keyPath.ToLowerInvariant(),
                Name = Path.GetFileNameWithoutExtension(keyPath),
                OpenPath = doc.PathName,
                CentralPath = central
            };
        }

        /// <summary>True when <paramref name="doc"/> is this model (any local copy of the same central counts).</summary>
        public bool Matches(Document doc)
            => string.Equals(FromDocument(doc)?.Key, Key, StringComparison.OrdinalIgnoreCase);

        /// <summary>Path Revit opens this model from, or null when there is none.</summary>
        public ModelPath ToModelPath()
        {
            if (IsCloud)
            {
                if (!Guid.TryParse(ProjectGuid, out Guid project) || !Guid.TryParse(ModelGuid, out Guid model))
                    return null;
                return ModelPathUtils.ConvertCloudGUIDsToCloudPath(CloudRegion, project, model);
            }

            string path = !string.IsNullOrWhiteSpace(OpenPath) ? OpenPath : CentralPath;
            return string.IsNullOrWhiteSpace(path) ? null : ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        }

        /// <summary>Where the model lives, for display.</summary>
        public string Location => IsCloud ? "Autodesk Docs (cloud)" : (CentralPath ?? OpenPath);
    }
}
