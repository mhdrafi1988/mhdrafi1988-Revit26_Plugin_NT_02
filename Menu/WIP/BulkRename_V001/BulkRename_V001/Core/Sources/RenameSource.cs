using Autodesk.Revit.DB;
using Revit26_Plugin.BulkRename.V001.Core.Models;
using Revit26_Plugin.BulkRename.V001.Core.Services;
using Revit26_Plugin.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.BulkRename.V001.Core.Sources
{
    /// <summary>
    /// One kind of project item Bulk Rename can rename. A source only finds its elements (<see cref="Collect"/>);
    /// listing them is the same for every kind (<see cref="Load"/>), and so is the rename itself (set the
    /// element's <c>Name</c>). Adding a new kind means one small subclass plus one line in
    /// <see cref="RenameSourceCatalog"/>.
    /// </summary>
    public abstract class RenameSource
    {
        /// <summary>Stable id, used to cache the loaded rows.</summary>
        public abstract string Key { get; }

        /// <summary>Name shown in the "What to rename" list.</summary>
        public abstract string DisplayName { get; }

        /// <summary>One-line explanation shown under the list.</summary>
        public abstract string Description { get; }

        /// <summary>
        /// Lists every item of this kind in <paramref name="doc"/>, sorted by name, with built-in and
        /// other-user-owned items marked locked, and logs how many were found. Items with a blank name are
        /// left out. Must run in a Revit API context.
        /// </summary>
        public IReadOnlyList<RenameItem> Load(Document doc, Action<LogEntry> log)
        {
            log ??= _ => { };
            var items = new List<RenameItem>();

            foreach (var found in Collect(doc, log))
            {
                if (found.Element == null) continue;

                string name = ReadName(found.Element);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string lockReason = !string.IsNullOrEmpty(found.BuiltInLock)
                    ? found.BuiltInLock
                    : WorksharingGuard.OwnerLock(doc, found.Element.Id);

                items.Add(new RenameItem(found.Element.Id, name, found.Details, found.UsedBy, lockReason));
            }

            items.Sort((a, b) => string.Compare(a.CurrentName, b.CurrentName, StringComparison.CurrentCultureIgnoreCase));

            log(new LogEntry(LogLevel.Info,
                $"{DisplayName}: {items.Count} found, {items.Count(i => !i.IsLocked)} can be renamed."));
            if (items.Count == 0)
                log(new LogEntry(LogLevel.Warning, $"No {DisplayName.ToLowerInvariant()} were found in this model."));

            return items;
        }

        /// <summary>
        /// Name of <paramref name="element"/> as the user knows it. Used when listing and again just before
        /// renaming, to skip an item that was renamed in the meantime. By default the element's <c>Name</c>.
        /// </summary>
        public virtual string ReadName(Element element) => element?.Name;

        /// <inheritdoc/>
        public override string ToString() => DisplayName;

        /// <summary>
        /// Finds the elements of this kind in <paramref name="doc"/>, in any order. Report anything unusual
        /// through <paramref name="log"/>; the count of items found is logged by <see cref="Load"/>.
        /// </summary>
        protected abstract IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log);

        /// <summary>One element found by <see cref="Collect"/>, with what the grid shows about it.</summary>
        protected readonly struct FoundItem
        {
            /// <summary>Element whose <c>Name</c> is changed.</summary>
            public Element Element { get; }

            /// <summary>Text for the Details column; may be empty.</summary>
            public string Details { get; }

            /// <summary>Text for the "Used by" column, for example "12 lines"; empty when not known.</summary>
            public string UsedBy { get; }

            /// <summary>Why the item is built-in and can never be renamed; empty when it can.</summary>
            public string BuiltInLock { get; }

            /// <summary>Creates a found item.</summary>
            public FoundItem(Element element, string details = null, string usedBy = null, string builtInLock = null)
            {
                Element = element;
                Details = details ?? string.Empty;
                UsedBy = usedBy ?? string.Empty;
                BuiltInLock = builtInLock ?? string.Empty;
            }
        }
    }
}
