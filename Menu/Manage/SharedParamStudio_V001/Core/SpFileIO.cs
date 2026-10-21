using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Core
{
    /// <summary>
    /// Reads a Revit shared parameter file keeping every column, group and the file's encoding
    /// (the older ParaManager parser drops columns, so it cannot be used to write a file back).
    /// </summary>
    public static class SpFileReader
    {
        /// <summary>Reads the file at <paramref name="path"/>. Throws on missing or unreadable files.</summary>
        public static SpFile Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Shared parameter file path is empty.", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Shared parameter file not found.", path);

            using var reader = new StreamReader(path, new UTF8Encoding(false), true);
            string text = reader.ReadToEnd();
            var file = Parse(text);
            file.Encoding = reader.CurrentEncoding;
            return file;
        }

        /// <summary>Parses file text. Header rows are skipped; parameters in an unknown group go to "Other".</summary>
        public static SpFile Parse(string text)
        {
            var file = new SpFile();
            var groupIdToName = new System.Collections.Generic.Dictionary<string, string>();
            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                string[] c = line.Split('\t');
                switch (c[0])
                {
                    case "META":
                        file.MetaVersion = Col(c, 1, file.MetaVersion);
                        file.MetaMinVersion = Col(c, 2, file.MetaMinVersion);
                        break;

                    case "GROUP":
                        if (c.Length > 2)
                        {
                            groupIdToName[c[1]] = c[2];
                            file.EnsureGroup(c[2]);
                        }
                        break;

                    case "PARAM":
                        if (c.Length < 6) break;
                        string group = groupIdToName.TryGetValue(c[5], out var gn) ? gn : "Other";
                        file.EnsureGroup(group);
                        file.Params.Add(new SpParam
                        {
                            Guid = c[1],
                            Name = Col(c, 2, string.Empty),
                            DataType = Col(c, 3, "TEXT"),
                            DataCategory = Col(c, 4, string.Empty),
                            GroupName = group,
                            Visible = Col(c, 6, "1"),
                            Description = Col(c, 7, string.Empty),
                            UserModifiable = Col(c, 8, "1"),
                            HideWhenNoValue = Col(c, 9, "0"),
                            LineNumber = i + 1
                        });
                        break;
                }
            }
            return file;
        }

        private static string Col(string[] cols, int index, string fallback)
            => index < cols.Length ? cols[index] : fallback;
    }

    /// <summary>Writes an <see cref="SpFile"/> in Revit's tab-delimited format.</summary>
    public static class SpFileWriter
    {
        /// <summary>The file text. Group ids are numbered 1..n in group order.</summary>
        public static string ToText(SpFile file)
        {
            var groups = file.Groups.ToList();
            foreach (var p in file.Params)
                if (!groups.Any(g => string.Equals(g, p.GroupName, StringComparison.OrdinalIgnoreCase)))
                    groups.Add(p.GroupName);

            var sb = new StringBuilder();
            sb.Append("# This is a Revit shared parameter file.\r\n");
            sb.Append("# Do not edit manually.\r\n");
            sb.Append("*META\tVERSION\tMINVERSION\r\n");
            sb.Append("META\t").Append(file.MetaVersion).Append('\t').Append(file.MetaMinVersion).Append("\r\n");
            sb.Append("*GROUP\tID\tNAME\r\n");
            for (int i = 0; i < groups.Count; i++)
                sb.Append("GROUP\t").Append(i + 1).Append('\t').Append(Clean(groups[i])).Append("\r\n");
            sb.Append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOKEY\r\n");
            foreach (var p in file.Params)
            {
                int groupId = groups.FindIndex(g => string.Equals(g, p.GroupName, StringComparison.OrdinalIgnoreCase)) + 1;
                sb.Append("PARAM\t").Append(Clean(p.Guid))
                  .Append('\t').Append(Clean(p.Name))
                  .Append('\t').Append(Clean(p.DataType))
                  .Append('\t').Append(Clean(p.DataCategory))
                  .Append('\t').Append(groupId)
                  .Append('\t').Append(Clean(p.Visible))
                  .Append('\t').Append(Clean(p.Description))
                  .Append('\t').Append(Clean(p.UserModifiable))
                  .Append('\t').Append(Clean(p.HideWhenNoValue))
                  .Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes the file via a temporary file. When <paramref name="path"/> already exists and
        /// <paramref name="backup"/> is set, the old file is first copied to "&lt;path&gt;.bak".
        /// </summary>
        public static void Write(SpFile file, string path, bool backup = true)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToText(file), file.Encoding);
            if (backup && File.Exists(path)) File.Copy(path, path + ".bak", true);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static string Clean(string s)
            => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
