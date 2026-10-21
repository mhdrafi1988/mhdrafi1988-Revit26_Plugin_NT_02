namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models
{
    /// <summary>One issue a rule found on a link.</summary>
    public sealed class Finding
    {
        public string RuleId { get; }
        public Severity Severity { get; }
        public string Title { get; }        // short — shown in the grid pill
        public string Message { get; }      // full recommendation
        public int EstSeconds { get; }      // rough extra seconds per host open

        public Finding(string ruleId, Severity severity, string title, string message, int estSeconds = 0)
        {
            RuleId = ruleId;
            Severity = severity;
            Title = title;
            Message = message;
            EstSeconds = estSeconds;
        }

        public override string ToString() => $"[{Severity}] {Title}: {Message}";
    }
}
