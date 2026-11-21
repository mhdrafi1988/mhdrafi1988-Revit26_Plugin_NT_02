using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>
    /// Reads what the tool needs to know about the open project. Implemented on the Revit side; every
    /// method must be called in the Revit API context (through <see cref="IRevitDispatcher"/>).
    /// </summary>
    public interface IModelReader
    {
        /// <summary>The active model and every Revit link instance placed in it, loaded or not.</summary>
        IReadOnlyList<ModelRef> GetModels();

        /// <summary>Number of elements per structural category in a model. Categories with none may be left out.</summary>
        IReadOnlyDictionary<StructuralCategory, int> CountStructural(ModelRef model);

        /// <summary>Number of elements per MEP category in a model. Categories with none may be left out.</summary>
        IReadOnlyDictionary<MepCategory, int> CountMep(ModelRef model);

        /// <summary>
        /// Transform from a model's own coordinates to the active model's, both in millimetres; the
        /// identity for the active model itself. Used to express openings in a linked structural
        /// model's coordinates for an opening package.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// The model is no longer in the project, its link is not loaded, or the link is placed with
        /// a scale, which the tool cannot express.
        /// </exception>
        Transform3 GetToHostTransform(ModelRef model);
    }
}
