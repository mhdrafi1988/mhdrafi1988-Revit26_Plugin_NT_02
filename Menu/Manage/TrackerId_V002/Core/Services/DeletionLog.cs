using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Services
{
    /// <summary>One element with a Tracker_ID that was deleted.</summary>
    public sealed class DeletedTrackerId
    {
        /// <summary>The deleted element's Tracker_ID.</summary>
        public string TrackerId { get; set; } = string.Empty;

        /// <summary>The deleted element's id.</summary>
        public long ElementId { get; set; }

        /// <summary>When it was deleted (local time).</summary>
        public DateTime DeletedAt { get; set; }

        /// <summary>Revit user name of the person who deleted it.</summary>
        public string User { get; set; } = string.Empty;
    }

    /// <summary>
    /// The list of deleted Tracker_IDs, one CSV file per model:
    /// <c>Tracker_ID,Element_Id,Deleted_At,User</c>. Lines are only ever appended.
    /// </summary>
    public static class DeletionLog
    {
        private const string Header = "Tracker_ID,Element_Id,Deleted_At,User";

        /// <summary>
        /// The log file for the model titled <paramref name="modelTitle"/> in <paramref name="folder"/>,
        /// with characters that are not allowed in file names replaced.
        /// </summary>
        public static string PathFor(string folder, string modelTitle)
        {
            string name = string.Concat((modelTitle ?? "Model").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(folder, $"{name}_DeletedTrackerIds.csv");
        }

        /// <summary>Appends <paramref name="entries"/>, creating the file (with its header) when missing.</summary>
        public static void Append(string path, IEnumerable<DeletedTrackerId> entries)
        {
            var list = entries?.ToList() ?? new List<DeletedTrackerId>();
            if (list.Count == 0) return;

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var sb = new StringBuilder();
            if (!File.Exists(path)) sb.AppendLine(Header);
            foreach (var e in list)
                sb.AppendLine(string.Join(",", Quote(e.TrackerId), e.ElementId.ToString(CultureInfo.InvariantCulture),
                    e.DeletedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Quote(e.User)));
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>Every entry in the file; an empty list when it does not exist. Bad lines are skipped.</summary>
        public static List<DeletedTrackerId> Read(string path)
        {
            var result = new List<DeletedTrackerId>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                var cells = Split(line);
                if (cells.Count < 4 || cells[0].Length == 0) continue;
                long.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId);
                DateTime.TryParseExact(cells[2], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at);
                result.Add(new DeletedTrackerId { TrackerId = cells[0], ElementId = elementId, DeletedAt = at, User = cells[3] });
            }
            return result;
        }

        /// <summary>
        /// The latest deletion of each Tracker_ID that no element has any more (an undone delete brings
        /// the ID back, so it drops out).
        /// </summary>
        public static List<DeletedTrackerId> StillMissing(IEnumerable<DeletedTrackerId> entries, IEnumerable<string> idsInModel)
        {
            var present = new HashSet<string>(idsInModel ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (entries ?? Enumerable.Empty<DeletedTrackerId>())
                .Where(e => !present.Contains(e.TrackerId))
                .GroupBy(e => e.TrackerId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(e => e.DeletedAt).Last())
                .OrderByDescending(e => e.DeletedAt)
                .ToList();
        }

        private static string Quote(string v)
        {
            v ??= string.Empty;
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        private static List<string> Split(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }
}
