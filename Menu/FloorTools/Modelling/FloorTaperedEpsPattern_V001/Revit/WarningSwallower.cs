using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Removes plain warnings (e.g. "Line is slightly off axis", which a nearly-orthogonal floor
    /// can raise once per grid line) and counts them. Errors are left alone so they roll back.
    /// </summary>
    public sealed class WarningSwallower : IFailuresPreprocessor
    {
        private readonly List<string> _messages = new List<string>();

        public IReadOnlyList<string> Messages => _messages;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    string text = failure.GetDescriptionText();
                    if (!_messages.Contains(text))
                    {
                        _messages.Add(text);
                    }

                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
