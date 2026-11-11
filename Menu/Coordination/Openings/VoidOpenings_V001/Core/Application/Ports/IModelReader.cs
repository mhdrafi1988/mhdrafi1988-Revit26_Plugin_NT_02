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
        /// Reads the MEP elements of the given categories as centreline snapshots in host millimetres.
        /// Anything that cannot be reduced to centreline segments is described in <paramref name="notes"/>.
        /// </summary>
        MepModel ReadMep(ModelRef model, IReadOnlyList<MepCategory> categories, ICollection<string> notes);

        /// <summary>
        /// Reads the structural elements of the given categories as mesh snapshots in host millimetres.
        /// When <paramref name="hostBoundsMm"/> is given, only elements whose bounding box touches it are read.
        /// </summary>
        StructuralModel ReadStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories,
            Aabb? hostBoundsMm, ICollection<string> notes);

        /// <summary>The records of the void instances already in the structural model (host or link).</summary>
        IReadOnlyList<StoredOpening> ReadStoredOpenings(ModelRef structuralModel);
    }
}
