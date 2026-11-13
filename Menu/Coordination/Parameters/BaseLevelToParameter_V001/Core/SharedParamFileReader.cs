using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core
{
    /// <summary>One PARAM line of a shared parameter file.</summary>
    public sealed class SharedParamFileEntry
    {
        /// <summary>Parameter GUID.</summary>
        public Guid Guid { get; set; }

        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Data type as written in the file, for example "TEXT".</summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>Name of the group the parameter is in.</summary>
        public string GroupName { get; set; } = string.Empty;

        /// <summary>True when the data type is Text.</summary>
        public bool IsText =>
            string.Equals(DataType, "TEXT", StringComparison.OrdinalIgnoreCase) ||
            DataType.IndexOf("spec.string", StringComparison.OrdinalIgnoreCase) >= 0;
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
                string line = raw.TrimEnd('\r');
                string[] cols = line.Split('\t');
                if (cols.Length >= 3 && cols[0] == "GROUP")
                {
                    groups[cols[1]] = cols[2];
                }
                else if (cols.Length >= 6 && cols[0] == "PARAM" && Guid.TryParse(cols[1], out Guid guid))
                {
                    result.Add(new SharedParamFileEntry
                    {
                        Guid = guid,
                        Name = cols[2],
                        DataType = cols[3],
                        GroupName = cols[5]
                    });
                }
            }
            foreach (var e in result)
                if (groups.TryGetValue(e.GroupName, out string name)) e.GroupName = name;
            return result;
        }

        /// <summary>Finds the entry named <paramref name="name"/> (case-insensitive), or null.</summary>
        public static SharedParamFileEntry Find(IEnumerable<SharedParamFileEntry> entries, string name)
        {
            foreach (var e in entries)
                if (string.Equals(e.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>Group names in the file, in file order.</summary>
        public static List<string> GroupNames(string text)
        {
            var list = new List<string>();
            foreach (string raw in (text ?? string.Empty).Split('\n'))
            {
                string[] cols = raw.TrimEnd('\r').Split('\t');
                if (cols.Length >= 3 && cols[0] == "GROUP" && !list.Contains(cols[2])) list.Add(cols[2]);
            }
            return list;
        }

        /// <summary>True when <paramref name="path"/> can be opened for writing (it is not read-only or locked).</summary>
        public static bool IsWritable(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
