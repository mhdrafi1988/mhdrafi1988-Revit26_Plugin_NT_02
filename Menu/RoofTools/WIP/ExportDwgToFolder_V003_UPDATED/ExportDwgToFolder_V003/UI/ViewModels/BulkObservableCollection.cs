using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.UI.ViewModels
{
    /// <summary>
    /// ObservableCollection that can swap all of its items with a single Reset notification,
    /// so a grouped / sorted grid rebuilds once instead of once per row.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>Replaces every item and raises one Reset.</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                    Items.Add(item);
            }
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
