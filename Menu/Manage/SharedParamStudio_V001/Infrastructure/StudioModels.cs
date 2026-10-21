using System.Collections.Generic;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure
{
    /// <summary>A data type offered when creating a parameter (Revit spec plus its menu label).</summary>
    public sealed class DataTypeOption
    {
        /// <summary>Label shown in the drop-down.</summary>
        public string Name { get; }

        /// <summary>Revit spec the definition is created with.</summary>
        public ForgeTypeId Spec { get; }

        internal DataTypeOption(string name, ForgeTypeId spec) { Name = name; Spec = spec; }

        /// <inheritdoc/>
        public override string ToString() => Name;

        /// <summary>The data types offered, most common first. The only place that names Revit specs.</summary>
        public static IReadOnlyList<DataTypeOption> All { get; } = new List<DataTypeOption>
        {
            new("Text", SpecTypeId.String.Text),
            new("Multiline text", SpecTypeId.String.MultilineText),
            new("Integer", SpecTypeId.Int.Integer),
            new("Number", SpecTypeId.Number),
            new("Yes/No", SpecTypeId.Boolean.YesNo),
            new("Length", SpecTypeId.Length),
            new("Area", SpecTypeId.Area),
            new("Volume", SpecTypeId.Volume),
            new("Angle", SpecTypeId.Angle),
            new("URL", SpecTypeId.String.Url),
            new("Material", SpecTypeId.Reference.Material),
        };
    }

    /// <summary>A Properties-palette group a new parameter can be bound under.</summary>
    public sealed class GroupOption
    {
        /// <summary>Label shown in the drop-down.</summary>
        public string Name { get; }

        /// <summary>Revit group id.</summary>
        public ForgeTypeId GroupTypeId { get; }

        internal GroupOption(string name, ForgeTypeId id) { Name = name; GroupTypeId = id; }

        /// <inheritdoc/>
        public override string ToString() => Name;

        /// <summary>The groups offered; the first is the default.</summary>
        public static IReadOnlyList<GroupOption> All { get; } = new List<GroupOption>
        {
            new("Data", Autodesk.Revit.DB.GroupTypeId.Data),
            new("Identity Data", Autodesk.Revit.DB.GroupTypeId.IdentityData),
            new("Text", Autodesk.Revit.DB.GroupTypeId.Text),
            new("Dimensions", Autodesk.Revit.DB.GroupTypeId.Geometry),
            new("General", Autodesk.Revit.DB.GroupTypeId.General),
            new("Graphics", Autodesk.Revit.DB.GroupTypeId.Graphics),
            new("Materials and Finishes", Autodesk.Revit.DB.GroupTypeId.Materials),
            new("Construction", Autodesk.Revit.DB.GroupTypeId.Construction),
            new("Constraints", Autodesk.Revit.DB.GroupTypeId.Constraints),
            new("Phasing", Autodesk.Revit.DB.GroupTypeId.Phasing),
            new("Structural", Autodesk.Revit.DB.GroupTypeId.Structural),
            new("Mechanical", Autodesk.Revit.DB.GroupTypeId.Mechanical),
            new("Electrical", Autodesk.Revit.DB.GroupTypeId.Electrical),
            new("Plumbing", Autodesk.Revit.DB.GroupTypeId.Plumbing),
            new("IFC Parameters", Autodesk.Revit.DB.GroupTypeId.Ifc),
        };
    }

    /// <summary>A category a parameter can be bound to, with its tick box.</summary>
    public sealed partial class CategoryItem : ObservableObject
    {
        /// <summary>Category name.</summary>
        public string Name { get; }

        /// <summary>Category element id value.</summary>
        public long Id { get; }

        [ObservableProperty] private bool _isSelected;

        internal CategoryItem(string name, long id) { Name = name; Id = id; }
    }

    /// <summary>A shared parameter found in the open project, with its tick box.</summary>
    public sealed partial class ProjectSharedParameter : ObservableObject
    {
        /// <summary>Parameter GUID.</summary>
        public System.Guid Guid { get; set; }

        /// <summary>Parameter name.</summary>
        public string Name { get; set; }

        /// <summary>Revit spec of the parameter.</summary>
        public ForgeTypeId Spec { get; set; }

        /// <summary>Readable data type.</summary>
        public string DataTypeLabel { get; set; }

        /// <summary>Properties-palette group it is bound under.</summary>
        public string GroupLabel { get; set; }

        /// <summary>"Instance" or "Type".</summary>
        public string BindingKind { get; set; }

        /// <summary>Comma-joined bound category names.</summary>
        public string Categories { get; set; }

        [ObservableProperty] private bool _isSelected = true;
    }

    /// <summary>One message a Revit-side job reports back to the window's log.</summary>
    public sealed class JobMessage
    {
        /// <summary>Severity.</summary>
        public LogLevel Level { get; }

        /// <summary>Text.</summary>
        public string Text { get; }

        /// <summary>Creates a message.</summary>
        public JobMessage(LogLevel level, string text) { Level = level; Text = text; }
    }

    /// <summary>Outcome of a Revit-side job: success flag and the messages to log.</summary>
    public sealed class JobResult
    {
        /// <summary>False when nothing (or not everything) was done.</summary>
        public bool Success { get; set; }

        /// <summary>Messages in order.</summary>
        public List<JobMessage> Messages { get; } = new List<JobMessage>();

        internal void Add(LogLevel level, string text) => Messages.Add(new JobMessage(level, text));

        internal static JobResult Fail(string text)
        {
            var r = new JobResult();
            r.Add(LogLevel.Error, text);
            return r;
        }
    }

    /// <summary>What to create and bind.</summary>
    public sealed class CreateParameterRequest
    {
        /// <summary>Shared parameter file the definition is written to (created when missing).</summary>
        public string FilePath { get; set; }

        /// <summary>Group inside that file.</summary>
        public string FileGroup { get; set; }

        /// <summary>Parameter name.</summary>
        public string Name { get; set; }

        /// <summary>Data type.</summary>
        public DataTypeOption DataType { get; set; }

        /// <summary>Description (tooltip in Revit).</summary>
        public string Description { get; set; }

        /// <summary>User can edit the value.</summary>
        public bool UserModifiable { get; set; } = true;

        /// <summary>Shown in the Properties palette.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>True for an instance parameter, false for a type parameter.</summary>
        public bool IsInstance { get; set; } = true;

        /// <summary>Properties-palette group.</summary>
        public GroupOption PaletteGroup { get; set; }

        /// <summary>Category ids to bind to.</summary>
        public List<long> CategoryIds { get; set; } = new List<long>();
    }
}
