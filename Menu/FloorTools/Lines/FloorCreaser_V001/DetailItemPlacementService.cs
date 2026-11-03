// ==================================
// File: DetailItemPlacementService.cs
// Namespace: Revit26_Plugin_Revised.FloorCreaser.V001.Services
// ==================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Services
{
    /// <summary>
    /// Places a line-based detail component along each supplied plan line.
    /// Must be called inside an active <see cref="Transaction"/>.
    /// Returns a (placed, failed) tuple for the run summary.
    /// </summary>
    public class DetailItemPlacementService
    {
        private const double FtToMm = 304.8;

        /// <summary>Per-line failure detail is logged individually up to this many; the rest are summarised.</summary>
        private const int MaxDetailedFailures = 20;

        private readonly Document _doc;
        private readonly ViewPlan _view;

        public DetailItemPlacementService(Document doc, ViewPlan view)
        {
            _doc  = doc  ?? throw new ArgumentNullException(nameof(doc));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        /// <summary>
        /// Places detail items along <paramref name="lines"/>.
        /// </summary>
        /// <returns>(placed count, failed count)</returns>
        public (int Placed, int Failed) PlaceAlongLines(
            IList<Line>    lines,
            FamilySymbol   symbol,
            LoggingService log)
        {
            if (lines == null || lines.Count == 0)
            {
                log.Warning("No lines supplied for placement.");
                return (0, 0);
            }

            if (symbol == null)
            {
                log.Warning("No detail symbol supplied for placement.");
                return (0, 0);
            }

            log.Info($"Placing '{symbol.FamilyName} : {symbol.Name}' on {lines.Count} line(s) in view '{_view.Name}'.");

            if (!symbol.IsActive)
            {
                symbol.Activate();
                // Activation only takes effect after a regenerate; without it the
                // first NewFamilyInstance calls can fail on a freshly activated type.
                _doc.Regenerate();
                log.Debug("  Detail symbol was inactive — activated and regenerated.");
            }

            double tol    = _doc.Application.ShortCurveTolerance;
            int    placed = 0;
            int    failed = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];

                if (line == null)
                {
                    failed++;
                    LogFailure(log, failed, $"Line #{i + 1}: null — skipped.");
                    continue;
                }

                if (line.Length < tol)
                {
                    failed++;
                    LogFailure(log, failed,
                        $"Line #{i + 1}: {Describe(line)} — shorter than Revit's short-curve tolerance " +
                        $"({tol * FtToMm:F2} mm), skipped.");
                    continue;
                }

                try
                {
                    FamilyInstance instance = _doc.Create.NewFamilyInstance(line, symbol, _view);
                    if (instance != null)
                    {
                        placed++;
                        log.Debug($"  Line #{i + 1}: placed (id {instance.Id.Value})  {Describe(line)}");
                    }
                    else
                    {
                        failed++;
                        LogFailure(log, failed,
                            $"Line #{i + 1}: Revit returned no instance for {Describe(line)}.");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    LogFailure(log, failed,
                        $"Line #{i + 1}: placement threw {ex.GetType().Name} — {ex.Message}  {Describe(line)}  " +
                        $"(view origin Z {_view.Origin.Z * FtToMm:F1} mm)");
                }
            }

            if (failed > MaxDetailedFailures)
                log.Warning($"  …and {failed - MaxDetailedFailures} more placement failure(s) not itemised above.");

            if (placed == 0)
                log.Error("No detail items were placed. The lines were valid but Revit rejected every placement — see the failure detail above.");

            log.Info($"Detail items placed: {placed}  |  failed: {failed}");
            return (placed, failed);
        }

        // --------------------------------------------------
        // Helpers
        // --------------------------------------------------

        /// <param name="failedSoFar">Failure count including the one being logged.</param>
        private static void LogFailure(LoggingService log, int failedSoFar, string message)
        {
            if (failedSoFar <= MaxDetailedFailures)
                log.Warning(message);
        }

        private static string Describe(Line line)
        {
            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            return $"({a.X * FtToMm:F0}, {a.Y * FtToMm:F0}, {a.Z * FtToMm:F0}) → " +
                   $"({b.X * FtToMm:F0}, {b.Y * FtToMm:F0}, {b.Z * FtToMm:F0}) mm, length {line.Length * FtToMm:F0} mm";
        }
    }
}
