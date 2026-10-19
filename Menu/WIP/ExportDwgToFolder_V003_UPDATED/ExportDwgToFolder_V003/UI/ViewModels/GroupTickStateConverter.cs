using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Revit26_Plugin.ExportDwgToFolder.V003.UI.ViewModels
{
    /// <summary>
    /// Flattens a (possibly nested) <see cref="CollectionViewGroup"/> down to its leaf
    /// <see cref="DwgRowViewModel"/> rows. Shared by <see cref="GroupTickStateConverter"/> and the
    /// window's group-header tick-box click handler, so both agree on exactly which rows a group
    /// header checkbox covers.
    /// </summary>
    public static class GroupRows
    {
        public static void Collect(CollectionViewGroup group, List<DwgRowViewModel> rows)
        {
            if (group == null) return;
            foreach (var item in group.Items)
            {
                if (item is CollectionViewGroup sub) Collect(sub, rows);
                else if (item is DwgRowViewModel row) rows.Add(row);
            }
        }
    }

    /// <summary>
    /// Tri-state for a group header's tick box: <c>true</c> when every row in the group (including
    /// nested view-type sub-groups under a source group) is ticked, <c>false</c> when none are,
    /// <c>null</c> (indeterminate) when some are.
    /// Takes two inputs: the <see cref="CollectionViewGroup"/> itself (the GroupItem's DataContext)
    /// and the view model's <c>SelectedCount</c>, bound only so the converter re-runs whenever any
    /// row's tick changes — its value isn't otherwise used.
    /// </summary>
    public class GroupTickStateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0 || values[0] is not CollectionViewGroup group)
                return false;

            var rows = new List<DwgRowViewModel>();
            GroupRows.Collect(group, rows);
            if (rows.Count == 0) return false;

            int ticked = 0;
            foreach (var row in rows)
                if (row.IsSelected) ticked++;

            if (ticked == 0) return false;
            return ticked == rows.Count ? true : (object)null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
