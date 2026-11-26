namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels
{
    /// <summary>
    /// One entry in a line-style picker. <see cref="Name"/> is what is saved and drawn with;
    /// <see cref="Label"/> is what the picker shows and what typing searches.
    /// </summary>
    public sealed class LineStyleOption
    {
        /// <summary>The text shown for the view's own default line style.</summary>
        public const string DefaultLabel = "(the view's default line style)";

        /// <summary>Creates an option for <paramref name="name"/>; empty means the view's default style.</summary>
        public LineStyleOption(string name) => Name = name ?? string.Empty;

        /// <summary>The line style's name, or empty for the view's default.</summary>
        public string Name { get; }

        /// <summary>The name, or <see cref="DefaultLabel"/> for the view's default.</summary>
        public string Label => Name.Length == 0 ? DefaultLabel : Name;

        /// <summary>Returns <see cref="Label"/>; the editable picker shows and filters on this.</summary>
        public override string ToString() => Label;
    }
}
