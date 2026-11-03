using System;
using System.Globalization;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Converts PlannedSectionStatus to a bool for the grid checkbox's IsEnabled:
    /// only "Ready" rows are checkable; "ClusterCapped" rows are disabled (shown for
    /// transparency only — never created). Copied from FloorEdgeAroundSections_V004's
    /// StatusToEnabledConverter.
    /// </summary>
    public class StatusToEnabledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is PlannedSectionStatus status && status == PlannedSectionStatus.Ready;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
