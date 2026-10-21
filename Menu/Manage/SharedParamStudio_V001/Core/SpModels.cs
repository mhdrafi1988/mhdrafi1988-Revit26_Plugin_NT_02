using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Core
{
    /// <summary>One PARAM line of a Revit shared parameter file. Plain data, no Revit types.</summary>
    public sealed class SpParam
    {
        /// <summary>GUID text as stored in the file.</summary>
        public string Guid { get; set; } = string.Empty;

        /// <summary>Parameter name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Data type token as stored in the file (TEXT, LENGTH, YESNO ...).</summary>
        public string DataType { get; set; } = "TEXT";

        /// <summary>DATACATEGORY column (normally blank).</summary>
        public string DataCategory { get; set; } = string.Empty;

        /// <summary>Name of the file group the parameter sits in.</summary>
        public string GroupName { get; set; } = "Other";

        /// <summary>VISIBLE column ("1" / "0").</summary>
        public string Visible { get; set; } = "1";

        /// <summary>DESCRIPTION column.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>USERMODIFIABLE column ("1" / "0").</summary>
        public string UserModifiable { get; set; } = "1";

        /// <summary>HIDEWHENNOKEY column ("1" / "0").</summary>
        public string HideWhenNoValue { get; set; } = "0";

        /// <summary>1-based line number in the file it was read from (0 for new parameters).</summary>
        public int LineNumber { get; set; }

        /// <summary>Copy of this parameter.</summary>
        public SpParam Clone() => (SpParam)MemberwiseClone();
    }

    /// <summary>A shared parameter file held in memory: groups in file order and every parameter.</summary>
    public sealed class SpFile
    {
        /// <summary>META VERSION value.</summary>
        public string MetaVersion { get; set; } = "2";

        /// <summary>META MINVERSION value.</summary>
        public string MetaMinVersion { get; set; } = "1";

        /// <summary>Encoding the file was read with; new files default to UTF-16 LE with BOM, as Revit writes them.</summary>
        public Encoding Encoding { get; set; } = new UnicodeEncoding(false, true);

        /// <summary>Group names in file order.</summary>
        public List<string> Groups { get; } = new List<string>();

        /// <summary>Parameters in file order.</summary>
        public List<SpParam> Params { get; } = new List<SpParam>();

        /// <summary>Adds the group when no group of that name (case-insensitive) exists.</summary>
        public void EnsureGroup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!Groups.Any(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase)))
                Groups.Add(name);
        }
    }

    /// <summary>Helpers shared by the analysers: name and GUID keys, name validation, data type tokens.</summary>
    public static class SpKeys
    {
        /// <summary>Characters Revit refuses in a parameter name.</summary>
        public static readonly char[] IllegalNameChars = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\t', '\r', '\n' };

        /// <summary>Data type tokens offered when a parameter is written straight into a file.</summary>
        public static readonly IReadOnlyList<string> DataTypeTokens = new[]
        {
            "TEXT", "MULTILINETEXT", "INTEGER", "NUMBER", "YESNO", "LENGTH", "AREA", "VOLUME", "ANGLE", "URL", "MATERIAL"
        };

        /// <summary>Case- and whitespace-insensitive key for comparing names.</summary>
        public static string NameKey(string name) => (name ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>Canonical lower-case key for a GUID string (braces and case ignored).</summary>
        public static string GuidKey(string guid)
        {
            string g = (guid ?? string.Empty).Trim();
            return System.Guid.TryParse(g, out var parsed) ? parsed.ToString("D") : g.ToLowerInvariant();
        }

        /// <summary>Returns null when the name is usable, otherwise the reason it is not.</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Name is empty.";
            if (name.Trim().Length != name.Length) return "Name starts or ends with a space.";
            int bad = name.IndexOfAny(IllegalNameChars);
            if (bad >= 0) return $"Name contains the character '{name[bad]}', which Revit does not allow.";
            return null;
        }
    }
}
