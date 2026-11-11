using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Lists the models a run can use (the active model and its Revit link instances) and finds the
    /// Revit document behind one of them again.
    /// </summary>
    public static class LinkCatalog
    {
        /// <summary>Prefix of the active model's entry in the model lists.</summary>
        public const string ActiveModelPrefix = "(Active model) ";

        /// <summary>
        /// The active model plus one entry per top-level Revit link instance, loaded or not. Nested
        /// links are not listed yet.
        /// </summary>
        public static IReadOnlyList<ModelRef> GetModels(Document host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            var models = new List<ModelRef>
            {
                new ModelRef(KeyOf(host), ActiveModelPrefix + host.Title) { IsHost = true, IsLoaded = true },
            };

            var instances = new FilteredElementCollector(host)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(instance => new { Instance = instance, Type = host.GetElement(instance.GetTypeId()) as RevitLinkType })
                .Where(x => x.Type != null && !x.Type.IsNestedLink)
                .ToList();

            foreach (var group in instances.GroupBy(x => x.Type.Id))
            {
                bool several = group.Count() > 1;
                foreach (var item in group)
                {
                    Document linkDocument = item.Instance.GetLinkDocument();
                    string key = linkDocument != null ? KeyOf(linkDocument) : "link:" + item.Type.Name.ToLowerInvariant();

                    // One instance: the file name is enough. Several: Revit's instance name tells them apart.
                    string name = several ? item.Instance.Name : item.Type.Name;
                    models.Add(new ModelRef(key, name)
                    {
                        IsHost = false,
                        IsLoaded = linkDocument != null,
                        LinkInstanceUniqueId = item.Instance.UniqueId,
                    });
                }
            }

            return models;
        }

        /// <summary>
        /// The document a model entry stands for: the host itself, or the linked document of the link
        /// instance. Returns null when the link is no longer there or is not loaded.
        /// </summary>
        public static Document Resolve(Document host, ModelRef model)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (model == null) throw new ArgumentNullException(nameof(model));

            if (model.IsHost)
                return host;
            if (string.IsNullOrEmpty(model.LinkInstanceUniqueId))
                return null;

            var instance = host.GetElement(model.LinkInstanceUniqueId) as RevitLinkInstance;
            return instance?.GetLinkDocument();
        }

        private static string KeyOf(Document document)
        {
            ModelIdentity identity = ModelIdentity.FromDocument(document);
            return identity?.Key ?? "unsaved:" + document.Title.ToLowerInvariant();
        }
    }
}
