using Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.TagToText.V001.UI.Views
{
    /// <summary>
    /// Tick state of a group heading in the tag table (any level): true when every copyable row under it is
    /// ticked, false when none is, null when only some are. Bound to the group and to
    /// <see cref="TagToTextViewModel.TicksVersion"/>, so it re-reads whenever a tick changes.
    /// </summary>
    public sealed class GroupTickStateConverter : IMultiValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var rows = TagToTextViewModel.LeafRows(values.FirstOrDefault() as CollectionViewGroup).ToList();
            return TagToTextViewModel.GroupTickState(rows);
        }

        /// <inheritdoc/>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>"3 of 5 ticked" for a group heading (any level). Same bindings as <see cref="GroupTickStateConverter"/>.</summary>
    public sealed class GroupTickTextConverter : IMultiValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var rows = TagToTextViewModel.LeafRows(values.FirstOrDefault() as CollectionViewGroup).ToList();
            return $"{rows.Count(r => r.IsSelected)} of {rows.Count} ticked";
        }

        /// <inheritdoc/>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
