using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Models;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources
{
    /// <summary>
    /// One kind of project item Bulk Delete can delete. A source finds its elements (<see cref="Collect"/>)
    /// and says what deleting one means (<see cref="Plan"/>); listing them is the same for every kind
    /// (<see cref="Load"/>), and so is the deletion itself (<see cref="DeleteService"/>). Adding a new kind
    /// means one small subclass plus one line in <see cref="DeleteSourceCatalog"/>.
    /// </summary>
    public abstract class DeleteSource
    {
        /// <summary>Stable id, used to cache the loaded rows.</summary>
        public abstract string Key { get; }

        /// <summary>Name shown in the "What to delete" list.</summary>
        public abstract string DisplayName { get; }

        /// <summary>One-line explanation shown under the list.</summary>
        public abstract string Description { get; }

        /// <summary>
        /// Lists every item of this kind in <paramref name="doc"/>, sorted by name, with built-in, in-use and
        /// other-user-owned items marked locked, and logs how many were found. Items with a blank name are
        /// left out. Must run in a Revit API context.
        /// </summary>
        public IReadOnlyList<DeleteItem> Load(Document doc, Action<LogEntry> log)
        {
            log ??= _ => { };
            var items = new List<DeleteItem>();

            foreach (var found in Collect(doc, log))
            {
                if (found.Element == null) continue;

                string name = ReadName(found.Element);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string lockReason = !string.IsNullOrEmpty(found.LockReason)
                    ? found.LockReason
                    : WorksharingGuard.OwnerLock(doc, found.Element.Id);

                items.Add(new DeleteItem(found.Element.Id, name, found.Details, found.UsedBy, found.UseCount, lockReason));
            }

            items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            log(new LogEntry(LogLevel.Info,
                $"{DisplayName}: {items.Count} found, {items.Count(i => !i.IsLocked)} can be deleted."));
            if (items.Count == 0)
                log(new LogEntry(LogLevel.Warning, $"No {DisplayName.ToLowerInvariant()} were found in this model."));

            return items;
        }

        /// <summary>
        /// Name of <paramref name="element"/> as the user knows it. Used when listing and again just before
        /// deleting, to skip an item that was renamed in the meantime. By default the element's <c>Name</c>.
        /// </summary>
        public virtual string ReadName(Element element) => element?.Name;

        /// <summary>
        /// What deleting <paramref name="element"/> means: the id handed to <see cref="Document.Delete(ElementId)"/>
        /// and every id that is expected to go with it. Read before anything is deleted. By default just the
        /// element itself. Returns null when the element cannot be deleted at all.
        /// </summary>
        public virtual DeletePlan Plan(Document doc, Element element)
            => element == null ? null : new DeletePlan(element.Id, new[] { element.Id });

        /// <summary>
        /// Prepares a check run again just before each item is deleted, so an item that became built-in-like
        /// or in use since it was listed is not deleted. The returned function gives the reason an element
        /// must be left alone, or null when it can go. By default nothing is blocked.
        /// </summary>
        public virtual Func<Element, string> CreateRecheck(Document doc) => _ => null;

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
            /// <summary>Element that is listed.</summary>
            public Element Element { get; }

            /// <summary>Text for the Details column; may be empty.</summary>
            public string Details { get; }

            /// <summary>Text for the "Used by" column, for example "12 lines"; empty when not known.</summary>
            public string UsedBy { get; }

            /// <summary>Number of uses when reliably known; null when not known.</summary>
            public int? UseCount { get; }

            /// <summary>Why the item can never be deleted here; empty when it can.</summary>
            public string LockReason { get; }

            /// <summary>Creates a found item.</summary>
            public FoundItem(Element element, string details = null, string usedBy = null, int? useCount = null, string lockReason = null)
            {
                Element = element;
                Details = details ?? string.Empty;
                UsedBy = usedBy ?? string.Empty;
                UseCount = useCount;
                LockReason = lockReason ?? string.Empty;
            }
        }
    }

    /// <summary>What deleting one item means; see <see cref="DeleteSource.Plan"/>.</summary>
    public sealed class DeletePlan
    {
        /// <summary>Id handed to <see cref="Document.Delete(ElementId)"/>.</summary>
        public ElementId Target { get; }

        /// <summary>Ids that are expected to be deleted together with the target.</summary>
        public ISet<ElementId> Expected { get; }

        /// <summary>Creates a plan; the target is always part of <paramref name="expected"/>.</summary>
        public DeletePlan(ElementId target, IEnumerable<ElementId> expected)
        {
            Target = target;
            Expected = new HashSet<ElementId>(expected ?? Array.Empty<ElementId>()) { target };
        }
    }
}
