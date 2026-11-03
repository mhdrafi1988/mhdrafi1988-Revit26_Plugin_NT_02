using System;
using System.Globalization;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// FLAGGED — NEW CONVERTER, NOT YET IN SHARED Converters.cs.
    /// Converts PlannedSectionStatus to a bool for the grid checkbox's IsEnabled:
    /// only "Ready" rows are checkable; NoEdgeFound / MergedOut are disabled
    /// (V002 does not support force-recreate/overwrite — confirmed assumption).
    ///
    /// This is tool-local (in UI/Views, not Shared/Converters.cs) because it is
    /// specific to PlannedSectionStatus, which is a FloorEdgeSections-only type.
    /// If a similar status-gating pattern shows up in another tool, promote this
    /// to Shared/Converters.cs at that point — flag for Rafi's review either way.
    /// </summary>
    public class StatusToEnabledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is PlannedSectionStatus status && status == PlannedSectionStatus.Ready;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
