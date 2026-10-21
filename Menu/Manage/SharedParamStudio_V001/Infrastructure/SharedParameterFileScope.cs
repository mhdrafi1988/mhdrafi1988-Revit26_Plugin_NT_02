using System;
using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure
{
    /// <summary>
    /// Points Revit's shared parameter file setting at a chosen file for the lifetime of the scope and
    /// puts the user's own setting back on dispose, so the tool never leaves Revit's global setting changed.
    /// </summary>
    internal sealed class SharedParameterFileScope : IDisposable
    {
        private readonly RevitApplication _app;
        private readonly string _previous;

        /// <summary>The opened definition file.</summary>
        public DefinitionFile File { get; }

        /// <summary>Opens <paramref name="path"/> as the current shared parameter file.</summary>
        public SharedParameterFileScope(RevitApplication app, string path)
        {
            _app = app;
            _previous = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = path;
                File = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException($"Revit could not open the shared parameter file '{path}'. It may be locked or not a shared parameter file.");
            }
            catch
            {
                Restore();
                throw;
            }
        }

        /// <inheritdoc/>
        public void Dispose() => Restore();

        private void Restore()
        {
            try { _app.SharedParametersFilename = _previous; }
            catch { /* best effort; a failed restore must not hide the real result */ }
        }
    }
}
