// =======================================================
// File: IChangeScope.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
// New in V034. A group of model changes that is kept together or not at all.
// In Revit it is a Transaction: Dispose without Commit rolls it back, so a
// "using" block still undoes everything when a step throws.
// =======================================================

using System;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
{
    /// <summary>An open model change. Dispose without <see cref="Commit"/> rolls it back.</summary>
    public interface IChangeScope : IDisposable
    {
        /// <summary>Keeps the changes made since the scope began.</summary>
        void Commit();
    }

    /// <summary>Starts model changes (in Revit: a named Transaction, one undo step).</summary>
    public interface IChangeScopeFactory
    {
        /// <summary>Starts a change with the given name, as shown in Revit's undo list.</summary>
        IChangeScope Begin(string name);
    }
}
