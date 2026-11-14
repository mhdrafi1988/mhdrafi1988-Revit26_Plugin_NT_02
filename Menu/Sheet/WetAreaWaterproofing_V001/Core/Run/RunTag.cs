using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run
{
    /// <summary>
    /// The text written to Comments on every element a run creates:
    /// <c>WP|&lt;RunId&gt;|&lt;RoomKey&gt;|&lt;Number&gt;|&lt;Name&gt;</c>. It links the model to the
    /// Excel report and lets a later run find, update or remove what an earlier run made.
    /// </summary>
    public static class RunTag
    {
        /// <summary>Start of every tag.</summary>
        public const string Prefix = "WP";

        /// <summary>Run ID for a run that starts at <paramref name="now"/>: yyyyMMdd-HHmmss.</summary>
        public static string NewRunId(DateTime now) => now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        /// <summary>Builds the Comments text. '|' in the number or name is replaced by '/'.</summary>
        public static string Make(string runId, string roomKey, string number, string name)
            => string.Join("|", Prefix, runId, roomKey, Clean(number), Clean(name));

        /// <summary>Reads the run ID and room key back; false for anything that is not a tag.</summary>
        public static bool TryParse(string comments, out string runId, out string roomKey)
        {
            runId = roomKey = null;
            if (string.IsNullOrEmpty(comments)) return false;
            string[] parts = comments.Split('|');
            if (parts.Length < 3 || parts[0] != Prefix || parts[1].Length == 0 || parts[2].Length == 0) return false;
            runId = parts[1];
            roomKey = parts[2];
            return true;
        }

        private static string Clean(string text) => (text ?? string.Empty).Replace('|', '/').Trim();
    }
}
