using System;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>Use case behind the OPENING CLEARANCE card: load, check and save the clearance setup.</summary>
    public sealed class ClearanceConfigurationService
    {
        private readonly ISettingsStore _store;

        /// <summary>Creates the service.</summary>
        public ClearanceConfigurationService(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Where the setup is kept, for messages.</summary>
        public string Location => _store.Location;

        /// <summary>The saved setup, or the default setup with a warning when it could not be read.</summary>
        public SettingsLoadResult Load() => _store.Load() ?? new SettingsLoadResult(null);

        /// <summary>Checks the card without saving.</summary>
        public ClearanceValidation Validate(ClearanceInput input) => ClearanceValidator.Validate(input);

        /// <summary>
        /// Checks the card and saves it when it has no error. Nothing is written when
        /// <see cref="ClearanceValidation.HasErrors"/> is true. Throws when the file cannot be written.
        /// </summary>
        public ClearanceValidation Save(ClearanceInput input)
        {
            ClearanceValidation validation = ClearanceValidator.Validate(input);
            if (!validation.HasErrors)
                _store.Save(validation.Setup);
            return validation;
        }
    }
}
