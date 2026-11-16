using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports
{
    /// <summary>What <see cref="ISettingsStore.Load"/> found.</summary>
    public sealed class SettingsLoadResult
    {
        /// <summary>The saved setup, or the default setup when nothing usable was saved.</summary>
        public ClearanceSetup Setup { get; }

        /// <summary>What the user should know about the load (unreadable file, skipped rules); null when nothing.</summary>
        public string Warning { get; }

        /// <summary>Creates a result.</summary>
        public SettingsLoadResult(ClearanceSetup setup, string warning = null)
        {
            Setup = setup ?? ClearanceSetup.CreateDefault();
            Warning = string.IsNullOrWhiteSpace(warning) ? null : warning;
        }
    }

    /// <summary>Keeps the user's clearance setup between Revit sessions.</summary>
    public interface ISettingsStore
    {
        /// <summary>Where the setup is kept, for messages.</summary>
        string Location { get; }

        /// <summary>
        /// Reads the saved setup. Never throws for a missing, unreadable or damaged file: the result
        /// then holds the default setup and a warning.
        /// </summary>
        SettingsLoadResult Load();

        /// <summary>Saves the setup. Throws (I/O or access error) when it cannot be written.</summary>
        void Save(ClearanceSetup setup);
    }
}
