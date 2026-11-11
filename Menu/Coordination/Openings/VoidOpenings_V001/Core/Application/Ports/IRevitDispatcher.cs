using System;
using System.Threading.Tasks;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>
    /// Runs work in the Revit API context on behalf of the window, which may not call the Revit API
    /// directly. The returned task completes when Revit has run the work.
    /// </summary>
    public interface IRevitDispatcher
    {
        /// <summary>Queues <paramref name="work"/> for the Revit API context and returns its result.</summary>
        /// <exception cref="InvalidOperationException">Revit refused the request, for example while a dialog is open.</exception>
        Task<T> RunAsync<T>(Func<T> work);
    }
}
