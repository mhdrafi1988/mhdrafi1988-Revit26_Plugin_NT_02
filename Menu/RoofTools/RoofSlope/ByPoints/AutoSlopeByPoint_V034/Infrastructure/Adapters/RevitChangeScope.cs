// =======================================================
// File: RevitChangeScope.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
// New in V034. IChangeScope on a Revit Transaction. Dispose without Commit
// disposes the open Transaction, which rolls it back, exactly like V033's
// "using (var tx = new Transaction(...))" block.
// =======================================================

using System;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
{
    /// <summary>One open Revit Transaction.</summary>
    internal sealed class RevitChangeScope : IChangeScope
    {
        private readonly Transaction _tx;

        /// <summary>Starts a Transaction with the given name.</summary>
        public RevitChangeScope(Document doc, string name)
        {
            _tx = new Transaction(doc, name);
            _tx.Start();
        }

        /// <inheritdoc/>
        public void Commit() => _tx.Commit();

        /// <summary>Rolls the Transaction back if it was not committed, then frees it.</summary>
        public void Dispose() => _tx.Dispose();
    }

    /// <summary>Starts Revit Transactions on one document.</summary>
    internal sealed class RevitChangeScopeFactory : IChangeScopeFactory
    {
        private readonly Document _doc;

        /// <summary>Creates a factory for <paramref name="doc"/>.</summary>
        public RevitChangeScopeFactory(Document doc) =>
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <inheritdoc/>
        public IChangeScope Begin(string name) => new RevitChangeScope(_doc, name);
    }
}
