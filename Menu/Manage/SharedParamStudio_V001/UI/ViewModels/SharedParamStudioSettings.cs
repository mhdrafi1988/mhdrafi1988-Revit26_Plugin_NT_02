namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    /// <summary>Remembered between sessions. <see cref="Version"/> lets later releases migrate the file.</summary>
    public class SharedParamStudioSettings
    {
        /// <summary>Settings format version.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Last open tab.</summary>
        public int LastTab { get; set; }

        /// <summary>Create tab: shared parameter file.</summary>
        public string CreateFilePath { get; set; } = string.Empty;

        /// <summary>Create tab: group inside the file.</summary>
        public string CreateFileGroup { get; set; } = string.Empty;

        /// <summary>Merge tab: first file.</summary>
        public string MergeFirstPath { get; set; } = string.Empty;

        /// <summary>Merge tab: second file.</summary>
        public string MergeSecondPath { get; set; } = string.Empty;

        /// <summary>Merge tab: output file.</summary>
        public string MergeOutputPath { get; set; } = string.Empty;

        /// <summary>Duplicate tab: file.</summary>
        public string DuplicatePath { get; set; } = string.Empty;

        /// <summary>Retrieve tab: target file.</summary>
        public string RetrieveFilePath { get; set; } = string.Empty;

        /// <summary>Retrieve tab: group inside the target file.</summary>
        public string RetrieveFileGroup { get; set; } = "Project parameters";
    }
}
