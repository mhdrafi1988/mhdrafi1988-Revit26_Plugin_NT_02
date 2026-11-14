using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ScheduleExportImport.V007.Core.Models;
using Revit26_Plugin_Revised.ScheduleExportImport.V007.Core.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ScheduleExportImport.V007.Infrastructure.ExternalEvents
{
    public enum ScheduleImportRequest
    {
        LoadSchedules,
        ExportSchedule,
        AnalyzeImport,
        ApplyChanges
    }

    /// <summary>
    /// All Revit-API work for this tool. AnalyzeImport is read-only (no transaction) and
    /// builds the preview; ApplyChanges writes only the rows the user left ticked.
    /// </summary>
    public class ScheduleImportEventHandler : IExternalEventHandler
    {
        private readonly Document _doc;

        public ScheduleImportRequest Request { get; set; }

        // ---- Inputs ----
        public ElementId TargetScheduleId { get; set; }
        public ExcelImportFile ImportFile { get; set; }
        /// <summary>Export: also export the schedule's hidden fields (after the visible ones).</summary>
        public bool IncludeHiddenFields { get; set; }
        public List<ImportChange> ChangesToApply { get; set; } = new List<ImportChange>();

        // ---- Outputs ----
        public List<ScheduleViewInfo> LoadedSchedules { get; private set; } = new List<ScheduleViewInfo>();
        public List<string> ExportedHeaders { get; private set; } = new List<string>();
        public List<ScheduleRow> ExportedRows { get; private set; } = new List<ScheduleRow>();
        public HashSet<string> EditableHeaders { get; private set; } = new HashSet<string>();
        /// <summary>Export: dropdown lists and type-parameter columns for the workbook.</summary>
        public ExportExtras ExportExtras { get; private set; } = new ExportExtras();
        /// <summary>Warnings from the last export (e.g. table values that could not be read).</summary>
        public List<string> ExportNotes { get; private set; } = new List<string>();
        public ImportAnalysis Analysis { get; private set; } = new ImportAnalysis();
        public int AppliedElementCount { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;
        public bool LastRunSucceeded { get; private set; }

        public event Action RequestCompleted;

        public ScheduleImportEventHandler(Document doc)
        {
            _doc = doc;
        }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            ErrorMessage = string.Empty;
            // Esc / Cancel with a Yes-No confirm while schedules are read, exported, analysed or imported.
            using ToolCancellation cancel = Request == ScheduleImportRequest.LoadSchedules
                ? null
                : ToolCancellation.BeginFor(ToolCatalog.ScheduleExportImport, app);
            try
            {
                switch (Request)
                {
                    case ScheduleImportRequest.LoadSchedules: ExecuteLoadSchedules(); break;
                    case ScheduleImportRequest.ExportSchedule: ExecuteExportSchedule(); break;
                    case ScheduleImportRequest.AnalyzeImport: ExecuteAnalyzeImport(); break;
                    case ScheduleImportRequest.ApplyChanges: ExecuteApplyChanges(); break;
                }
                LastRunSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                // Every group / transaction was rolled back as the exception unwound.
                LastRunSucceeded = false;
                ErrorMessage = "Cancelled by user. Nothing was changed.";
                throw; // ToolGuard treats it as a user cancel; finally still completes the request
            }
            catch (Exception ex)
            {
                LastRunSucceeded = false;
                ErrorMessage = ex.Message;
            }
            finally
            {
                RequestCompleted?.Invoke();
            }
        }

        public string GetName() => "Schedule Export/Import V005";

        // ── Load all schedule views ────────────────────────────────────────────
        private void ExecuteLoadSchedules()
        {
            LoadedSchedules = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate && !vs.IsTitleblockRevisionSchedule)
                .OrderBy(vs => vs.Name)
                .Select(vs =>
                {
                    var def = vs.Definition;
                    int visibleFields = Enumerable.Range(0, def.GetFieldCount()).Count(i => !def.GetField(i).IsHidden);
                    int rowCount = 0;
                    try { rowCount = new FilteredElementCollector(_doc, vs.Id).WhereElementIsNotElementType().GetElementCount(); }
                    catch { }

                    return new ScheduleViewInfo
                    {
                        ViewId = vs.Id,
                        Name = vs.Name,
                        CategoryName = def.CategoryId != ElementId.InvalidElementId
                            ? Category.GetCategory(_doc, def.CategoryId)?.Name ?? "Unknown"
                            : "Multi-Category",
                        RowCount = rowCount,
                        FieldCount = visibleFields
                    };
                })
                .ToList();
        }

        // ── Export ─────────────────────────────────────────────────────────────
        // The API has no "table row → element" lookup, so to read formula / room / combined
        // columns we tag each element's Comments with this marker inside a transaction that
        // is always rolled back, and match the schedule's table rows by it.
        private const string RowMarker = "§RBX§";

        private enum ColumnSource { Parameter, Count, Table }

        private sealed class ExportColumn
        {
            public string Header;
            public int FieldId;
            public ScheduleFieldType FieldType;
            public ElementId ParameterId;
            public ColumnSource Source;
        }

        private void ExecuteExportSchedule()
        {
            var schedule = GetSchedule();
            var def = schedule.Definition;
            var notes = new List<string>();

            var columns = new List<ExportColumn>();
            foreach (var fieldId in def.GetFieldOrder())
            {
                ToolCancellation.CheckCurrent();
                var field = def.GetField(fieldId);
                if (field.IsHidden && !IncludeHiddenFields) continue;

                // Parameter name, not ColumnHeading: import matches Excel headers back to
                // parameters by name, so a renamed heading ("W" for "Width") would never match.
                var header = field.GetName();
                var unique = header;
                int dup = 1;
                while (columns.Any(c => c.Header == unique))
                    unique = $"{header} ({dup++})";

                columns.Add(new ExportColumn
                {
                    Header = unique,
                    FieldId = fieldId.IntegerValue,
                    FieldType = field.FieldType,
                    ParameterId = field.ParameterId,
                    Source = GetColumnSource(field)
                });
            }

            var elements = CollectScheduleElements(schedule);

            // Formula / room / combined fields have no parameter on the element itself —
            // take the text Revit shows in the schedule table instead.
            List<long> tableOrder = null;
            Dictionary<long, Dictionary<int, string>> tableValues = null;
            var tableColumns = columns.Where(c => c.Source == ColumnSource.Table).ToList();
            if (tableColumns.Count > 0)
            {
                try
                {
                    tableValues = ReadScheduleTable(schedule, elements, tableColumns.Select(c => c.FieldId).ToList(),
                        out tableOrder, out var discarded);
                    if (discarded.Count > 0)
                        notes.Add($"Revit raised {discarded.Count} warning(s) while reading the calculated columns; they were " +
                                  $"discarded and the model is unchanged. First: {discarded[0]}");
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    notes.Add($"Could not read calculated columns from the schedule table ({ex.Message}) — they are left blank: " +
                              string.Join(", ", tableColumns.Select(c => c.Header)));
                }
            }

            var editable = new HashSet<string>();
            var extras = new ExportExtras();
            foreach (var col in columns.Where(c => c.Source == ColumnSource.Parameter && c.FieldType == ScheduleFieldType.ElementType))
                extras.TypeHeaders.Add(col.Header);
            var rowsByElement = new List<(Element Elem, ScheduleRow Row)>();
            int tableMisses = 0;

            foreach (var elem in elements)
            {
                ToolCancellation.CheckCurrent();
                var row = new ScheduleRow { ElementId = elem.Id.Value };
                Dictionary<int, string> elemTable = null;
                if (tableValues != null && !tableValues.TryGetValue(elem.Id.Value, out elemTable))
                    tableMisses++;

                foreach (var col in columns)
                {
                    ToolCancellation.CheckCurrent();
                    string value = string.Empty;
                    try
                    {
                        switch (col.Source)
                        {
                            case ColumnSource.Count:
                                value = "1"; // one row per element
                                break;
                            case ColumnSource.Table:
                                if (elemTable != null && elemTable.TryGetValue(col.FieldId, out var text))
                                    value = text;
                                break;
                            default:
                                var param = FindFieldParameter(elem, col.FieldType, col.ParameterId);
                                if (param != null)
                                {
                                    value = GetParameterDisplayValue(param);
                                    if (IsImportWritable(elem, col, param))
                                        editable.Add(col.Header);
                                    if (!extras.Dropdowns.ContainsKey(col.Header))
                                    {
                                        var options = GetDropdownOptions(param);
                                        if (options != null) extras.Dropdowns[col.Header] = options;
                                    }
                                }
                                break;
                        }
                    }
                    catch { }
                    row.Values[col.Header] = value ?? string.Empty;
                }
                rowsByElement.Add((elem, row));
            }

            if (tableMisses > 0)
                notes.Add($"{tableMisses} element(s) could not be matched to a schedule table row (Comments not editable for them) — " +
                          $"calculated columns are blank for those rows: {string.Join(", ", tableColumns.Select(c => c.Header))}");

            var rows = OrderLikeSchedule(def, rowsByElement, tableOrder);

            ExportedHeaders = columns.Select(c => c.Header).ToList();
            ExportedRows = rows;
            EditableHeaders = editable;
            ExportExtras = extras;
            ExportNotes = notes;
        }

        private static ColumnSource GetColumnSource(ScheduleField field)
        {
            if (field.FieldType == ScheduleFieldType.Count) return ColumnSource.Count;
            if (!field.IsCalculatedField && !field.IsCombinedParameterField
                && (field.FieldType == ScheduleFieldType.Instance || field.FieldType == ScheduleFieldType.ElementType)
                && field.ParameterId != ElementId.InvalidElementId)
                return ColumnSource.Parameter;
            return ColumnSource.Table;
        }

        /// <summary>
        /// The exact parameter the schedule field points at (by id, not by name), looking at
        /// the instance first for instance fields and the type first for type fields.
        /// </summary>
        private Parameter FindFieldParameter(Element elem, ScheduleFieldType fieldType, ElementId parameterId)
        {
            var type = _doc.GetElement(elem.GetTypeId());
            bool typeFirst = fieldType == ScheduleFieldType.ElementType;
            return FindParameter(typeFirst ? type : elem, parameterId)
                   ?? FindParameter(typeFirst ? elem : type, parameterId);
        }

        private Parameter FindParameter(Element e, ElementId parameterId)
        {
            if (e == null) return null;
            if (parameterId.Value < 0) return e.get_Parameter((BuiltInParameter)(int)parameterId.Value);
            if (_doc.GetElement(parameterId) is SharedParameterElement shared) return e.get_Parameter(shared.GuidValue);
            foreach (Parameter p in e.Parameters)
                if (p.Id == parameterId) return p;
            return null;
        }

        // Green only when import (which looks parameters up by header name) would land on
        // this same instance parameter — avoids writing to a same-named wrong parameter.
        private static bool IsImportWritable(Element elem, ExportColumn col, Parameter param)
            => col.FieldType == ScheduleFieldType.Instance
               && !param.IsReadOnly
               && param.Element?.Id == elem.Id
               && elem.LookupParameter(col.Header)?.Id == param.Id;

        /// <summary>
        /// Reads the schedule's table text for the calculated columns. It tags every element's
        /// Comments inside a transaction that is always rolled back. Changing an element makes
        /// Revit regenerate it, which can raise family warnings ("Can't create sweep", "Line is
        /// too short") on elements whose geometry is already faulty; those are collected in
        /// <paramref name="discardedFailures"/> and never shown, because nothing is kept.
        /// </summary>
        private Dictionary<long, Dictionary<int, string>> ReadScheduleTable(
            ViewSchedule schedule, List<Element> elements, List<int> wantedFieldIds, out List<long> order,
            out List<string> discardedFailures)
        {
            order = new List<long>();
            discardedFailures = new List<string>();
            var result = new Dictionary<long, Dictionary<int, string>>();
            var def = schedule.Definition;
            var commentsId = new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

            using var tx = new Transaction(_doc, "Read schedule table (rolled back)");
            // This transaction is thrown away, so the failures it raises must go with it.
            // Without ClearAfterRollback Revit keeps the warnings and shows them after export.
            var quiet = new DiscardedFailures(discardedFailures);
            var failureOptions = tx.GetFailureHandlingOptions();
            failureOptions.SetFailuresPreprocessor(quiet);
            failureOptions.SetClearAfterRollback(true);
            failureOptions.SetDelayedMiniWarnings(false);
            failureOptions.SetForcedModalHandling(false);
            tx.SetFailureHandlingOptions(failureOptions);
            tx.Start();
            try
            {
                // One body row per element, no group headers / footers / totals.
                def.IsItemized = true;
                def.ShowGrandTotal = false;
                var sortFields = def.GetSortGroupFields();
                foreach (var s in sortFields)
                {
                    ToolCancellation.CheckCurrent();
                    s.ShowHeader = false;
                    s.ShowFooter = false;
                    s.ShowBlankLine = false;
                }
                def.SetSortGroupFields(sortFields);

                // Hidden calculated fields (Include hidden fields) only have table text when shown.
                foreach (var id in def.GetFieldOrder())
                    if (wantedFieldIds.Contains(id.IntegerValue) && def.GetField(id).IsHidden)
                        def.GetField(id).IsHidden = false;

                int markerFieldId = -1;
                foreach (var id in def.GetFieldOrder())
                {
                    ToolCancellation.CheckCurrent();
                    var f = def.GetField(id);
                    if (f.FieldType == ScheduleFieldType.Instance && !f.IsCalculatedField
                        && !f.IsCombinedParameterField && f.ParameterId == commentsId)
                    {
                        f.IsHidden = false;
                        markerFieldId = id.IntegerValue;
                        break;
                    }
                }
                if (markerFieldId < 0)
                    markerFieldId = def.AddField(ScheduleFieldType.Instance, commentsId).FieldId.IntegerValue;

                foreach (var elem in elements)
                {
                    ToolCancellation.CheckCurrent();
                    var p = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p == null || p.IsReadOnly) continue;
                    try { p.Set(RowMarker + elem.Id.Value); } catch { }
                }

                _doc.Regenerate();
                schedule.RefreshData();

                var visible = def.GetFieldOrder()
                    .Where(id => !def.GetField(id).IsHidden)
                    .Select(id => id.IntegerValue)
                    .ToList();
                var body = schedule.GetTableData().GetSectionData(SectionType.Body);
                int firstCol = body.FirstColumnNumber;
                int markerCol = firstCol + visible.IndexOf(markerFieldId);
                var wantedCols = wantedFieldIds
                    .Select(id => (FieldId: id, Col: visible.IndexOf(id)))
                    .Where(x => x.Col >= 0)
                    .ToList();

                for (int r = body.FirstRowNumber; r <= body.LastRowNumber; r++)
                {
                    ToolCancellation.CheckCurrent();
                    var marker = schedule.GetCellText(SectionType.Body, r, markerCol);
                    if (string.IsNullOrEmpty(marker) || !marker.StartsWith(RowMarker, StringComparison.Ordinal)) continue;
                    if (!long.TryParse(marker.Substring(RowMarker.Length), out var elemId) || result.ContainsKey(elemId)) continue;

                    var values = new Dictionary<int, string>();
                    foreach (var (fieldId, col) in wantedCols)
                        values[fieldId] = schedule.GetCellText(SectionType.Body, r, firstCol + col) ?? string.Empty;
                    result[elemId] = values;
                    order.Add(elemId);
                }
            }
            finally
            {
                // Nothing in the model is ever kept from this.
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            }
            return result;
        }

        /// <summary>
        /// Failure handler for the rolled-back table read: removes warnings, and on errors tells
        /// Revit to roll back quietly. Collects each distinct message for the export log.
        /// </summary>
        private sealed class DiscardedFailures : IFailuresPreprocessor
        {
            private readonly List<string> _messages;

            public DiscardedFailures(List<string> messages) => _messages = messages;

            /// <inheritdoc/>
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                bool hasErrors = false;
                foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
                {
                    var text = failure.GetDescriptionText();
                    if (!string.IsNullOrEmpty(text) && !_messages.Contains(text)) _messages.Add(text);

                    if (failure.GetSeverity() == FailureSeverity.Warning)
                        failuresAccessor.DeleteWarning(failure);
                    else
                        hasErrors = true;
                }
                if (!hasErrors) return FailureProcessingResult.Continue;

                var options = failuresAccessor.GetFailureHandlingOptions();
                options.SetClearAfterRollback(true);
                failuresAccessor.SetFailureHandlingOptions(options);
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }

        /// <summary>
        /// Rows in the schedule's own order: the table order when it was read, otherwise the
        /// schedule's sorting/grouping fields applied to the parameter values, then Element ID.
        /// </summary>
        private List<ScheduleRow> OrderLikeSchedule(ScheduleDefinition def,
            List<(Element Elem, ScheduleRow Row)> rows, List<long> tableOrder)
        {
            var keys = new List<(Func<Element, object> Key, bool Descending)>();
            foreach (var s in def.GetSortGroupFields())
            {
                ToolCancellation.CheckCurrent();
                var field = def.GetField(s.FieldId);
                if (GetColumnSource(field) != ColumnSource.Parameter) continue;
                var fieldType = field.FieldType;
                var paramId = field.ParameterId;
                keys.Add((e => SortKey(FindFieldParameter(e, fieldType, paramId)), s.SortOrder == ScheduleSortOrder.Descending));
            }

            IEnumerable<(Element Elem, ScheduleRow Row)> ordered = rows;
            if (keys.Count > 0)
            {
                var cmp = new SortKeyComparer();
                IOrderedEnumerable<(Element Elem, ScheduleRow Row)> o = null;
                foreach (var (key, desc) in keys)
                {
                    ToolCancellation.CheckCurrent();
                    if (o == null)
                        o = desc ? rows.OrderByDescending(x => key(x.Elem), cmp) : rows.OrderBy(x => key(x.Elem), cmp);
                    else
                        o = desc ? o.ThenByDescending(x => key(x.Elem), cmp) : o.ThenBy(x => key(x.Elem), cmp);
                }
                ordered = o.ThenBy(x => x.Row.ElementId);
            }
            else
            {
                ordered = rows.OrderBy(x => x.Row.ElementId);
            }

            if (tableOrder != null)
            {
                var rank = new Dictionary<long, int>();
                for (int i = 0; i < tableOrder.Count; i++) rank[tableOrder[i]] = i;
                // OrderBy is stable: rows missing from the table keep the sorted order, at the end.
                ordered = ordered.OrderBy(x => rank.TryGetValue(x.Row.ElementId, out var i) ? i : int.MaxValue);
            }

            return ordered.Select(x => x.Row).ToList();
        }

        private static object SortKey(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return (double)p.AsInteger();
                case StorageType.String: return p.AsString();
                default: return p.AsValueString();
            }
        }

        /// <summary>Numbers numerically, text "naturally" (Door 2 before Door 10), blanks first.</summary>
        private sealed class SortKeyComparer : IComparer<object>
        {
            public int Compare(object a, object b)
            {
                bool aBlank = a == null || (a is string sa && sa.Length == 0);
                bool bBlank = b == null || (b is string sb && sb.Length == 0);
                if (aBlank || bBlank) return aBlank == bBlank ? 0 : aBlank ? -1 : 1;
                if (a is double da && b is double db) return da.CompareTo(db);
                return NaturalCompare(a.ToString(), b.ToString());
            }

            private static int NaturalCompare(string x, string y)
            {
                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    ToolCancellation.CheckCurrent();
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        int si = i, sj = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;
                        var nx = x.Substring(si, i - si).TrimStart('0');
                        var ny = y.Substring(sj, j - sj).TrimStart('0');
                        int c = nx.Length != ny.Length ? nx.Length.CompareTo(ny.Length) : string.CompareOrdinal(nx, ny);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (x.Length - i).CompareTo(y.Length - j);
            }
        }

        // ── Analyze (read-only) ────────────────────────────────────────────────
        private void ExecuteAnalyzeImport()
        {
            var file = ImportFile ?? throw new InvalidOperationException("No import file was loaded.");
            var analysis = new ImportAnalysis { RowsInFile = file.Rows.Count };
            var matchedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dedup = ImportRowValidator.Deduplicate(file.Rows);
            analysis.DuplicateRowsMerged = dedup.MergedDuplicateRows;
            foreach (var conflict in dedup.Conflicts)
            {
                ToolCancellation.CheckCurrent();
                var e = _doc.GetElement(new ElementId(conflict.ElementId));
                if (e != null) conflict.ElementName = GetElementName(e);
                analysis.Items.Add(conflict);
            }

            // Type edits are collected per (type, parameter) first: many rows can point at one type.
            var typeEdits = new Dictionary<(long TypeId, string Header), List<(long ElementId, string NewValue)>>();

            foreach (var row in dedup.UniqueRows)
            {
                ToolCancellation.CheckCurrent();
                var elem = _doc.GetElement(new ElementId(row.ElementId));
                if (elem == null)
                {
                    analysis.Items.Add(new ImportChange
                    {
                        ElementId = row.ElementId,
                        ElementName = "—",
                        Status = ImportChangeStatus.NotFound,
                        Message = "No element with this ID in the model (deleted, or file from another model)."
                    });
                    continue;
                }

                var elemName = GetElementName(elem);
                analysis.ElementsMatched++;
                string editBlock = null;
                bool editBlockChecked = false;
                foreach (var header in file.Headers)
                {
                    ToolCancellation.CheckCurrent();
                    if (!row.Values.TryGetValue(header, out var newValue)) continue;

                    var param = elem.LookupParameter(header);
                    if (param == null)
                    {
                        var typeParam = GetTypeParameter(elem, header);
                        if (typeParam == null) continue;
                        matchedHeaders.Add(header);
                        if (SameValue(typeParam, GetParameterDisplayValue(typeParam), newValue))
                        {
                            analysis.UnchangedValues++;
                            continue;
                        }
                        var key = (elem.GetTypeId().Value, header);
                        if (!typeEdits.TryGetValue(key, out var list)) typeEdits[key] = list = new List<(long, string)>();
                        list.Add((row.ElementId, newValue));
                        continue;
                    }

                    matchedHeaders.Add(header);
                    var oldValue = GetParameterDisplayValue(param);
                    if (SameValue(param, oldValue, newValue))
                    {
                        analysis.UnchangedValues++;
                        continue;
                    }

                    if (param.IsReadOnly)
                    {
                        analysis.Items.Add(NewItem(row.ElementId, elemName, header, param, newValue, ImportChangeStatus.ReadOnly, "Read-only in Revit — cannot be changed."));
                        continue;
                    }

                    if (!editBlockChecked) { editBlock = GetEditBlock(elem.Id); editBlockChecked = true; }
                    if (editBlock != null)
                    {
                        analysis.Items.Add(NewItem(row.ElementId, elemName, header, param, newValue, ImportChangeStatus.NotEditable, editBlock));
                        continue;
                    }

                    if (TryGetOriginal(file, row.ElementId, header, out var original) && !SameValue(param, original, oldValue))
                    {
                        analysis.Items.Add(NewItem(row.ElementId, elemName, header, param, newValue, ImportChangeStatus.Conflict,
                            $"Changed in Revit since export (was \"{original}\", now \"{oldValue}\"). Tick to overwrite it with the file value."));
                        continue;
                    }

                    analysis.Items.Add(NewItem(row.ElementId, elemName, header, param, newValue, ImportChangeStatus.Change, string.Empty));
                }
            }

            foreach (var edit in typeEdits)
                analysis.Items.Add(AnalyzeTypeEdit(file, edit.Key.TypeId, edit.Key.Header, edit.Value));

            // Elements the schedule shows today that the file doesn't mention at all.
            if (TargetScheduleId != null && _doc.GetElement(TargetScheduleId) is ViewSchedule schedule)
            {
                var inFile = new HashSet<long>(file.Rows.Select(r => r.ElementId));
                foreach (var elem in CollectScheduleElements(schedule).Where(e => !inFile.Contains(e.Id.Value)))
                {
                    ToolCancellation.CheckCurrent();
                    analysis.Items.Add(new ImportChange
                    {
                        ElementId = elem.Id.Value,
                        ElementName = GetElementName(elem),
                        Status = ImportChangeStatus.MissingFromFile,
                        Message = "In the schedule but not in the file (added after export, or row deleted). Left unchanged."
                    });
                }
            }

            if (analysis.ElementsMatched > 0)
                analysis.UnmatchedHeaders = file.Headers.Where(h => !matchedHeaders.Contains(h)).ToList();
            Analysis = analysis;
        }

        // ── Apply ticked changes ──────────────────────────────────────────────
        private void ExecuteApplyChanges()
        {
            AppliedElementCount = 0;

            // One TransactionGroup, assimilated → a single "Schedule Import" entry in Revit's Undo.
            using var group = new TransactionGroup(_doc, "Schedule Import V005");
            group.Start();

            foreach (var byElement in ChangesToApply.GroupBy(c => c.ElementId))
            {
                ToolCancellation.CheckCurrent();
                var elem = _doc.GetElement(new ElementId(byElement.Key));
                if (elem == null)
                {
                    foreach (var c in byElement) MarkFailed(c, "Element no longer exists.");
                    continue;
                }

                using var tx = new Transaction(_doc, $"Import element {byElement.Key}");
                tx.Start();
                bool anyApplied = false;
                try
                {
                    foreach (var change in byElement)
                    {
                        ToolCancellation.CheckCurrent();
                        var param = elem.LookupParameter(change.ParameterName);
                        if (param == null || param.IsReadOnly)
                        {
                            MarkFailed(change, "Parameter missing or read-only.");
                            continue;
                        }
                        try
                        {
                            if (WriteParameter(_doc, param, change.NewValue))
                            {
                                change.Status = ImportChangeStatus.Applied;
                                change.Message = string.Empty;
                                anyApplied = true;
                            }
                            else
                            {
                                MarkFailed(change, $"Revit rejected the value \"{change.NewValue}\" (check units/format).");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // user cancel: never swallowed as a failure
                        }
                        catch (Exception ex)
                        {
                            MarkFailed(change, ex.Message);
                        }
                    }
                    // Revit can roll a commit back without throwing (e.g. element borrowed by
                    // another user in a workshared model) — don't report those as applied.
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        foreach (var c in byElement.Where(c => c.Status == ImportChangeStatus.Applied))
                            MarkFailed(c, $"Revit did not commit the change ({status}). The element may be borrowed by another user.");
                        continue;
                    }
                    if (anyApplied) AppliedElementCount++;
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    foreach (var c in byElement.Where(c => c.Status == ImportChangeStatus.Applied))
                        MarkFailed(c, $"Rolled back: {ex.Message}");
                }
            }

            ToolCancellation.CheckCurrent(); // last chance to cancel before the group is kept
            group.Assimilate();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private ViewSchedule GetSchedule()
            => _doc.GetElement(TargetScheduleId) as ViewSchedule
               ?? throw new InvalidOperationException("Schedule view not found in document.");

        private List<Element> CollectScheduleElements(ViewSchedule schedule)
            => new FilteredElementCollector(_doc, schedule.Id).WhereElementIsNotElementType().ToList();

        private Parameter GetTypeParameter(Element elem, string name)
        {
            var typeId = elem.GetTypeId();
            return typeId == ElementId.InvalidElementId ? null : _doc.GetElement(typeId)?.LookupParameter(name);
        }

        private static ImportChange NewItem(long id, string elementName, string header, Parameter param, string newValue, ImportChangeStatus status, string message)
            => new ImportChange
            {
                ElementId = id,
                ElementName = elementName,
                ParameterName = header,
                OldValue = GetParameterDisplayValue(param),
                NewValue = newValue,
                Status = status,
                Message = message
            };

        private static string GetElementName(Element elem)
        {
            try { return elem.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static void MarkFailed(ImportChange change, string message)
        {
            change.Status = ImportChangeStatus.Failed;
            change.Message = message;
        }

        /// <summary>One preview line per (type, parameter): applied to the type, so every instance changes.</summary>
        private ImportChange AnalyzeTypeEdit(ExcelImportFile file, long typeIdValue, string header, List<(long ElementId, string NewValue)> rows)
        {
            var typeId = new ElementId(typeIdValue);
            var type = _doc.GetElement(typeId);
            var param = type?.LookupParameter(header);
            var typeName = type != null ? GetElementName(type) : typeIdValue.ToString();
            var item = new ImportChange
            {
                ElementId = typeIdValue,
                ElementName = $"Type: {typeName}",
                ParameterName = header,
                OldValue = param != null ? GetParameterDisplayValue(param) : string.Empty,
                NewValue = rows[0].NewValue
            };

            var distinct = rows.Select(r => r.NewValue).Distinct(StringComparer.Ordinal).ToList();
            if (param == null)
            {
                item.Status = ImportChangeStatus.NotFound;
                item.Message = "Type or parameter no longer exists.";
            }
            else if (distinct.Count > 1)
            {
                item.Status = ImportChangeStatus.Duplicate;
                item.NewValue = string.Join(" / ", distinct.Take(3)) + (distinct.Count > 3 ? " …" : "");
                item.Message = $"Rows of this type ask for different values ({distinct.Count}). A type has one value — make the rows agree in Excel.";
            }
            else if (param.IsReadOnly)
            {
                item.Status = ImportChangeStatus.ReadOnly;
                item.Message = "Read-only in Revit — cannot be changed.";
            }
            else if (GetEditBlock(typeId) is string block)
            {
                item.Status = ImportChangeStatus.NotEditable;
                item.Message = block;
            }
            else if (rows.Any(r => TryGetOriginal(file, r.ElementId, header, out var orig) && !SameValue(param, orig, item.OldValue)))
            {
                item.Status = ImportChangeStatus.Conflict;
                item.Message = "Type value changed in Revit since export. Tick to overwrite it with the file value.";
            }
            else
            {
                int instances = new FilteredElementCollector(_doc).WhereElementIsNotElementType()
                    .Count(e => e.GetTypeId() == typeId);
                item.Status = ImportChangeStatus.TypeParameter;
                item.Message = $"Type parameter — changes all {instances} instance(s) of this type. Tick to apply.";
            }
            return item;
        }

        /// <summary>
        /// Why an element can't be edited right now in a workshared model, or null when it can.
        /// Checked up front so the preview shows it, instead of the change failing at Apply.
        /// </summary>
        private string GetEditBlock(ElementId id)
        {
            if (!_doc.IsWorkshared) return null;
            try
            {
                var checkout = WorksharingUtils.GetCheckoutStatus(_doc, id, out string owner);
                if (checkout == CheckoutStatus.OwnedByOtherUser)
                    return $"Borrowed by {owner}. Ask them to synchronize and relinquish, then import again.";

                switch (WorksharingUtils.GetModelUpdatesStatus(_doc, id))
                {
                    case ModelUpdatesStatus.UpdatedInCentral:
                        return "Changed in the central model by someone else. Reload Latest, then import again.";
                    case ModelUpdatesStatus.DeletedInCentral:
                        return "Deleted in the central model. Reload Latest.";
                }
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception)
            {
                // Status unavailable (e.g. detached copy) — let Apply try and report.
            }
            return null;
        }

        private static bool TryGetOriginal(ExcelImportFile file, long elementId, string header, out string original)
        {
            original = null;
            return file.OriginalValues.TryGetValue(elementId, out var values) && values.TryGetValue(header, out original);
        }

        /// <summary>
        /// Number-aware comparison for numeric parameters: "25.83 m²" equals "25.830 m²" and "25.83".
        /// Text parameters stay exact ("01" ≠ "1").
        /// </summary>
        private static bool SameValue(Parameter param, string a, string b)
        {
            if (SameValue(a, b)) return true;
            bool numeric = param.StorageType == StorageType.Double
                           || (param.StorageType == StorageType.Integer && param.Definition.GetDataType() != SpecTypeId.Boolean.YesNo);
            if (!numeric) return false;
            if (!TryParseNumber(a, out var x, out var unitA) || !TryParseNumber(b, out var y, out var unitB)) return false;
            if (unitA.Length > 0 && unitB.Length > 0 && !string.Equals(unitA, unitB, StringComparison.OrdinalIgnoreCase)) return false;
            return Math.Abs(x - y) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        private static readonly System.Text.RegularExpressions.Regex NumberWithUnit =
            new(@"^\s*([-+]?(?:\d[\d,\.\s]*|\.\d+))\s*(\S.*)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool TryParseNumber(string text, out double value, out string unit)
        {
            value = 0; unit = string.Empty;
            var m = NumberWithUnit.Match(text ?? string.Empty);
            if (!m.Success) return false;
            var num = m.Groups[1].Value.Replace(" ", "");
            unit = m.Groups[2].Value.Trim();
            // "1,234.5" / "3,000" → thousands commas; "25,83" → decimal comma.
            bool thousands = num.Contains('.')
                             || num.Split(',').Skip(1).All(g => g.Length == 3 && g.All(char.IsDigit));
            num = thousands ? num.Replace(",", "") : num.Replace(',', '.');
            return double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool SameValue(string a, string b)
            => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.Ordinal);

        private static bool WriteParameter(Document doc, Parameter param, string value)
        {
            if (param.StorageType == StorageType.ElementId)
                return WriteElementIdParameter(doc, param, value);

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.Set(value ?? string.Empty);

                case StorageType.Integer:
                    if (param.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                    {
                        var v = value.Trim();
                        if (v.Equals("Yes", StringComparison.OrdinalIgnoreCase) || v == "1") return param.Set(1);
                        if (v.Equals("No", StringComparison.OrdinalIgnoreCase) || v == "0") return param.Set(0);
                        return false;
                    }
                    return int.TryParse(value, out int intVal) ? param.Set(intVal) : param.SetValueString(value);

                case StorageType.Double:
                    // Export wrote display units (e.g. "3000" mm); SetValueString parses them
                    // back through the project's units. Set(double) would treat them as feet.
                    return !string.IsNullOrWhiteSpace(value) && param.SetValueString(value);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Element-reference parameters (Base Level, Phase Created, …) are exported as the
        /// referenced element's name. Write them back by finding the element of the same kind
        /// with that name — e.g. "Level 2" → the Level named "Level 2".
        /// </summary>
        private static bool WriteElementIdParameter(Document doc, Parameter param, string value)
        {
            var name = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || name == "<None>" || name == "None")
                return param.Set(ElementId.InvalidElementId);

            var current = doc.GetElement(param.AsElementId());
            if (current == null) return false; // no reference today → we can't tell what kind of element it expects

            var matches = CandidateElements(doc, current)
                .Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return matches.Count == 1 && param.Set(matches[0].Id);
        }

        /// <summary>Elements a reference parameter could point at: same class (and category) as today's value.</summary>
        private static IEnumerable<Element> CandidateElements(Document doc, Element current)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(current.GetType())
                    .Where(e => current.Category == null || e.Category?.Id == current.Category.Id)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception)
            {
                return Enumerable.Empty<Element>(); // some API classes can't be used with OfClass
            }
        }

        private const int MaxDropdownOptions = 500;

        /// <summary>Values for an Excel dropdown: Yes/No, or the names an element-reference
        /// parameter (Base Level, …) can take. Null when the column is free text.</summary>
        private List<string> GetDropdownOptions(Parameter param)
        {
            if (param.IsReadOnly) return null;
            if (param.StorageType == StorageType.Integer && param.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                return new List<string> { "Yes", "No" };
            if (param.StorageType != StorageType.ElementId) return null;

            var current = _doc.GetElement(param.AsElementId());
            if (current == null) return null;
            var names = CandidateElements(_doc, current)
                .Select(e => GetElementName(e))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Ambiguous names can't be written back by name anyway; very long lists aren't usable as dropdowns.
            return names.Count is > 0 and <= MaxDropdownOptions ? names : null;
        }

        private static string GetParameterDisplayValue(Parameter param)
        {
            if (param.StorageType == StorageType.None) return string.Empty;
            if (param.StorageType == StorageType.String) return param.AsString() ?? string.Empty;
            return param.AsValueString() ?? string.Empty;
        }
    }
}
