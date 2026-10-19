using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Models
{
    /// <summary>
    /// Bindable row for the "Linked Files" checkbox list (mirrors PlanViewRow's
    /// pattern). Multiple links can now be checked at once — elements are
    /// collected from every checked link, kept separate per link (never merged),
    /// since two links can each have a type named e.g. "Floor Drain 100mm" that
    /// are NOT the same element set.
    /// </summary>
    public partial class LinkRow : ObservableObject
    {
        [ObservableProperty]
        private bool isSelected;

        /// <summary>Display name — the link instance's name, which Revit keeps
        /// unique even if the same file is linked in more than once.</summary>
        public string Name { get; }

        public RevitLinkInstance Instance { get; }
        public Document LinkDocument { get; }

        /// <summary>ACC Docs status of the link, drawn as the green radio dot in the ACC column.</summary>
        public AccLinkState AccState { get; }

        /// <summary>Sort key for the ACC column — ACC problems first, non-ACC links last.</summary>
        public int AccSortOrder => AccLinkStatus.SortOrder(AccState);

        public LinkRow(RevitLinkInstance instance)
        {
            Instance = instance;
            LinkDocument = instance.GetLinkDocument();
            Name = instance.Name;
            AccState = AccLinkStatus.Read(instance);
        }
    }
}
