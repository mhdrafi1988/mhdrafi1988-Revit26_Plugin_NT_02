namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>A value with a display label, for combo boxes bound to enums.</summary>
    public sealed class ChoiceOption<T>
    {
        public ChoiceOption(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }
        public string Label { get; }

        public override string ToString() => Label;
    }
}
