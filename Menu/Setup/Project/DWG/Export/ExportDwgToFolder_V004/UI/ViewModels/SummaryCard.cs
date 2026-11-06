using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels
{
    /// <summary>Colour of a metric tile's number.</summary>
    public enum CardTone { Neutral, Accent, Success, Warning, Danger }

    /// <summary>One metric tile (big number + label) in the header row. Value and tone update live.</summary>
    public partial class SummaryCard : ObservableObject
    {
        /// <summary>Creates a tile.</summary>
        public SummaryCard(string label, int value, CardTone tone = CardTone.Neutral, string hint = "")
        {
            Label = label;
            this.value = value.ToString("N0");
            this.tone = tone;
            Hint = hint;
        }

        /// <summary>Caption under the number.</summary>
        public string Label { get; }

        /// <summary>Tooltip.</summary>
        public string Hint { get; }

        /// <summary>Formatted number.</summary>
        [ObservableProperty] private string value;

        /// <summary>Number colour.</summary>
        [ObservableProperty] private CardTone tone;

        /// <summary>Sets the number and colour.</summary>
        public void Set(int number, CardTone newTone)
        {
            Value = number.ToString("N0");
            Tone = newTone;
        }
    }
}
