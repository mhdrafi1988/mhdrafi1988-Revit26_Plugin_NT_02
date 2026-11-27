using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Infrastructure
{
    /// <summary>Exports the panel schedule as a UTF-8 CSV file.</summary>
    public static class CsvExportService
    {
        /// <summary>Writes the panel schedule CSV. Throws on I/O failure.</summary>
        public static void Export(string path, LayoutResult layout, InsulationSettings settings)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            using (var sw = new StreamWriter(tmp, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                sw.WriteLine("Serial,Layer,Row,Column,CentreX_m,CentreY_m,DrainDist_m,Height_mm,Trimmed,Area_m2");

                double boardArea = settings.BoardSizeM * settings.BoardSizeM;

                foreach (InsulationCell cell in layout.Cells)
                {
                    // Base layer row
                    sw.Write(Quote(cell.Serial + "_B")); sw.Write(',');
                    sw.Write("Base (100 mm)"); sw.Write(',');
                    sw.Write(cell.Row); sw.Write(',');
                    sw.Write(cell.Column); sw.Write(',');
                    sw.Write(cell.Centre.X.ToString("F3")); sw.Write(',');
                    sw.Write(cell.Centre.Y.ToString("F3")); sw.Write(',');
                    sw.Write(cell.DrainDistanceM.ToString("F3")); sw.Write(',');
                    sw.Write(settings.BaseThicknessMm.ToString("F0")); sw.Write(',');
                    sw.Write(cell.IsTrimmed ? "Yes" : "No"); sw.Write(',');
                    sw.Write(boardArea.ToString("F4"));
                    sw.WriteLine();

                    // Step layers
                    for (int s = 1; s <= cell.StepLayerCount; s++)
                    {
                        double h = settings.BaseThicknessMm + s * settings.StepThicknessMm;
                        sw.Write(Quote($"{cell.Serial}_S{s:D2}")); sw.Write(',');
                        sw.Write($"Step {s} (35 mm)"); sw.Write(',');
                        sw.Write(cell.Row); sw.Write(',');
                        sw.Write(cell.Column); sw.Write(',');
                        sw.Write(cell.Centre.X.ToString("F3")); sw.Write(',');
                        sw.Write(cell.Centre.Y.ToString("F3")); sw.Write(',');
                        sw.Write(cell.DrainDistanceM.ToString("F3")); sw.Write(',');
                        sw.Write(h.ToString("F0")); sw.Write(',');
                        sw.Write(cell.IsTrimmed ? "Yes" : "No"); sw.Write(',');
                        sw.Write(boardArea.ToString("F4"));
                        sw.WriteLine();
                    }
                }
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
