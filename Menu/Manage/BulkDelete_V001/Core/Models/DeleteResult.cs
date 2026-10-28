using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Models
{
    /// <summary>What happened to one requested deletion.</summary>
    public sealed class DeleteOutcome
    {
        /// <summary>Name the item had before the run.</summary>
        public string Name { get; }

        /// <summary>True when the item was deleted; false when it was skipped and left in the model.</summary>
        public bool Deleted { get; }

        /// <summary>Why the item was skipped; empty when it was deleted.</summary>
        public string Reason { get; }

        private DeleteOutcome(string name, bool deleted, string reason)
        {
            Name = name ?? string.Empty;
            Deleted = deleted;
            Reason = reason ?? string.Empty;
        }

        /// <summary>The item <paramref name="name"/> was deleted.</summary>
        public static DeleteOutcome Done(string name) => new DeleteOutcome(name, true, null);

        /// <summary>The item <paramref name="name"/> was left in the model because of <paramref name="reason"/>.</summary>
        public static DeleteOutcome Skip(string name, string reason) => new DeleteOutcome(name, false, reason);
    }

    /// <summary>
    /// Result of one Bulk Delete run: one outcome per requested item. Deleted outcomes are final only
    /// once the transaction has committed, which is when the run returns.
    /// </summary>
    public sealed class DeleteResult
    {
        /// <summary>One outcome per requested item.</summary>
        public IReadOnlyList<DeleteOutcome> Outcomes { get; }

        /// <summary>Items deleted.</summary>
        public int Deleted { get; }

        /// <summary>Items skipped (changed since listing, now in use or owned by another user, or Revit refused).</summary>
        public int Skipped => Outcomes.Count - Deleted;

        /// <summary>Creates a result from the outcomes of a run.</summary>
        public DeleteResult(IReadOnlyList<DeleteOutcome> outcomes)
        {
            Outcomes = outcomes ?? Array.Empty<DeleteOutcome>();
            Deleted = Outcomes.Count(o => o.Deleted);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Deleted} deleted, {Skipped} skipped.";
    }
}
