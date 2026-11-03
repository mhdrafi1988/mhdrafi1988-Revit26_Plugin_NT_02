using System;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>One line in the log panel.</summary>
    public sealed class LogEntryViewModel
    {
        public LogEntryViewModel(string level, string message)
        {
            Time = DateTime.Now.ToString("HH:mm:ss");
            Level = level;
            Message = message;
        }

        public string Time { get; }
        public string Level { get; }
        public string Message { get; }

        public override string ToString() => $"{Time} [{Level}] {Message}";
    }
}
