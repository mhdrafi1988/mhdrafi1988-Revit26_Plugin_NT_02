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

        /// <summary>
        /// The document of a model entry together with the transform from its coordinates to the active
        /// model's. Throws <see cref="InvalidOperationException"/> with a message for the user when the
        /// entry no longer matches the active project (project switched, link removed or unloaded).
        /// </summary>
        public static ModelLocation Locate(Document host, ModelRef model)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (model == null) throw new ArgumentNullException(nameof(model));

            if (model.IsHost)
            {
                if (!string.Equals(KeyOf(host), model.ModelKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("The active project changed. Go back to the project the models were chosen in, or press Refresh and choose them again.");
                return new ModelLocation(host, Transform.Identity, null, model.DisplayName);
            }

            var instance = string.IsNullOrEmpty(model.LinkInstanceUniqueId) ? null : host.GetElement(model.LinkInstanceUniqueId) as RevitLinkInstance;
            if (instance == null)
                throw new InvalidOperationException($"The link {model.DisplayName} is not in the active project any more. Press Refresh and choose the models again.");
            Document linked = instance.GetLinkDocument();
            if (linked == null)
                throw new InvalidOperationException($"The link {model.DisplayName} is not loaded. Reload it in Manage Links, then press Refresh.");
            return new ModelLocation(linked, instance.GetTotalTransform(), instance, model.DisplayName);
        }

        private static string KeyOf(Document document)
        {
            ModelIdentity identity = ModelIdentity.FromDocument(document);
            return identity?.Key ?? "unsaved:" + document.Title.ToLowerInvariant();
        }
    }

    /// <summary>A model of a run, found again in the open project.</summary>
    public sealed class ModelLocation
    {
        /// <summary>The model's document (the host, or a linked document).</summary>
        public Document Document { get; }

        /// <summary>Transform from the model's coordinates to the active model's (identity for the host).</summary>
        public Transform ToHost { get; }

        /// <summary>The link instance; null for the host.</summary>
        public RevitLinkInstance Link { get; }

        /// <summary>Name shown to the user.</summary>
        public string Name { get; }

        /// <summary>Creates a location.</summary>
        public ModelLocation(Document document, Transform toHost, RevitLinkInstance link, string name)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            ToHost = toHost ?? Transform.Identity;
            Link = link;
            Name = name ?? string.Empty;
        }
    }
}
