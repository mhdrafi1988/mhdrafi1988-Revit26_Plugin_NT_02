using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core
{
    /// <summary>One PARAM line of a shared parameter file.</summary>
    public sealed class SharedParamFileEntry
    {
        /// <summary>Parameter GUID.</summary>
        public Guid Guid { get; set; }

        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Data type as written in the file, for example "TEXT" or "LENGTH".</summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>Name of the group the parameter is in.</summary>
        public string GroupName { get; set; } = string.Empty;

        /// <summary>True when the data type is Text.</summary>
        public bool IsText =>
            string.Equals(DataType, "TEXT", StringComparison.OrdinalIgnoreCase) ||
            DataType.IndexOf("spec.string", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when the data type is Length.</summary>
        public bool IsLength =>
            string.Equals(DataType, "LENGTH", StringComparison.OrdinalIgnoreCase) ||
            DataType.IndexOf("spec.length", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when the entry's data type is the one <paramref name="slot"/> needs.</summary>
        public bool Fits(TargetSlot slot) => TargetSlots.IsText(slot) ? IsText : IsLength;
    }

    /// <summary>
    /// Reads the PARAM and GROUP lines of a Revit shared parameter file without the Revit API, so the
    /// New Parameter dialog can check the file from the WPF thread.
    /// </summary>
    public static class SharedParamFileReader
    {
        /// <summary>Reads every parameter in <paramref name="path"/>. Throws when the file cannot be read.</summary>
        public static List<SharedParamFileEntry> Read(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Parses the text of a shared parameter file.</summary>
        public static List<SharedParamFileEntry> Parse(string text)
        {
            var groups = new Dictionary<string, string>();
            var result = new List<SharedParamFileEntry>();
            foreach (string raw in (text ?? string.Empty).Split('\n'))
            {
                string[] cols = raw.TrimEnd('\r').Split('\t');
                if (cols.Length >= 3 && cols[0] == "GROUP")
                {
                    groups[cols[1]] = cols[2];
                }
                else if (cols.Length >= 6 && cols[0] == "PARAM" && Guid.TryParse(cols[1], out Guid guid))
                {
                    string groupId = cols[5];
                    result.Add(new SharedParamFileEntry
                    {
                        Guid = guid,
                        Name = cols[2],
                        DataType = cols[3],
                        GroupName = groups.TryGetValue(groupId, out string g) ? g : groupId
                    });
                }
            }
            return result;
        }

        /// <summary>Every entry of <paramref name="entries"/> named <paramref name="name"/>, ignoring case.</summary>
        public static List<SharedParamFileEntry> FindAll(IEnumerable<SharedParamFileEntry> entries, string name)
        {
            if (entries == null || string.IsNullOrWhiteSpace(name)) return new List<SharedParamFileEntry>();
            string wanted = name.Trim();
            return entries.Where(e => string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Group names in <paramref name="path"/>, sorted; "Data" is always offered.</summary>
        public static List<string> ReadGroups(string path)
        {
            var names = new List<string> { "Data" };
            try
            {
                foreach (SharedParamFileEntry e in Read(path))
                    if (!string.IsNullOrWhiteSpace(e.GroupName)) names.Add(e.GroupName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                // The caller reports an unreadable file through FileFacts.ReadError.
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>True when the file can be opened for writing.</summary>
        public static bool IsWritable(string path)
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
