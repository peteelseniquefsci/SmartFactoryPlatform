using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Import;

public record ImportSummary(int Rows, int Created, int Updated, int Unchanged, List<string> Errors);

/// <summary>
/// Imports weekly actuals from a register extract or a personal file's Export block as CSV:
/// employee_id, slot, week (WW44), actual, source_row_id[, manual_target]. Idempotent on (slot, week): a row whose
/// value is unchanged is logged as unchanged; a typed value that overrides a register value is kept unless the import row carries a newer source id.
/// </summary>
public class WeeklyResultImporter
{
    private readonly SmartFactoryDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    public WeeklyResultImporter(SmartFactoryDbContext db, IAuditTrail audit, IClock clock) { _db = db; _audit = audit; _clock = clock; }

    public async Task<ImportSummary> ImportCsvAsync(TextReader reader, int halfId, ResultSource source = ResultSource.Imported, bool overwriteTyped = false)
    {
        int rows = 0, created = 0, updated = 0, unchanged = 0;
        var errors = new List<string>();
        var weeks = await _db.WorkWeeks.Where(w => w.FiscalMonth!.FiscalHalfId == halfId).ToDictionaryAsync(w => w.Label);
        var kras = await _db.Kras.Include(k => k.Person).Include(k => k.Slots).ThenInclude(s => s.WeeklyResults).Where(k => k.FiscalHalfId == halfId).ToListAsync();
        var byEmployee = kras.Where(k => k.Person is not null).ToDictionary(k => k.Person!.EmployeeId, StringComparer.OrdinalIgnoreCase);

        string? line; int lineNo = 0;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (lineNo == 1 && cols[0].Equals("employee_id", StringComparison.OrdinalIgnoreCase)) continue;
            rows++;
            if (cols.Length < 5) { errors.Add($"Line {lineNo}: expected at least 5 columns."); continue; }
            if (!byEmployee.TryGetValue(cols[0], out var kra)) { errors.Add($"Line {lineNo}: no KRA for employee {cols[0]}."); continue; }
            if (!int.TryParse(cols[1], out var slotNo)) { errors.Add($"Line {lineNo}: slot '{cols[1]}' is not a number."); continue; }
            var slot = kra.Slots.FirstOrDefault(s => s.SlotNumber == slotNo && s.IsActive);
            if (slot is null) { errors.Add($"Line {lineNo}: slot {slotNo} is not active for {cols[0]}."); continue; }
            if (!weeks.TryGetValue(cols[2].ToUpperInvariant(), out var week)) { errors.Add($"Line {lineNo}: week '{cols[2]}' is not in this half."); continue; }
            decimal? actual = null;
            if (!string.IsNullOrWhiteSpace(cols[3])) { if (decimal.TryParse(cols[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var a)) actual = a; else { errors.Add($"Line {lineNo}: actual '{cols[3]}' is not a number."); continue; } }
            decimal? manualTarget = null;
            if (cols.Length > 5 && decimal.TryParse(cols[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var mt)) manualTarget = mt;
            var rowId = string.IsNullOrWhiteSpace(cols[4]) ? $"{cols[0]}/{slotNo}/{week.Label}" : cols[4];

            var existing = slot.WeeklyResults.FirstOrDefault(r => r.WorkWeekId == week.Id);
            if (existing is null)
            {
                slot.WeeklyResults.Add(new WeeklyResult { KraSlotId = slot.Id, WorkWeekId = week.Id, Actual = actual, ManualWeeklyTarget = manualTarget, Source = source, SourceRowId = rowId, UpdatedAt = _clock.UtcNow });
                _audit.Record("WeeklyResult", $"{kra.Id}/{slot.SlotNumber}/{week.Label}", "Import", "Actual", null, actual?.ToString(), $"source {source}, row {rowId}");
                created++;
                continue;
            }
            if (existing.Actual == actual && existing.ManualWeeklyTarget == manualTarget && existing.SourceRowId == rowId) { unchanged++; continue; }
            if (existing.Source == ResultSource.Typed && !overwriteTyped && existing.Actual is not null)
            {
                unchanged++;
                errors.Add($"Line {lineNo}: {cols[0]} slot {slotNo} {week.Label} has a typed value; left as typed (use overwrite to replace).");
                continue;
            }
            _audit.Record("WeeklyResult", $"{kra.Id}/{slot.SlotNumber}/{week.Label}", "Import", "Actual", existing.Actual?.ToString(), actual?.ToString(), $"source {source}, row {rowId}");
            existing.Actual = actual; existing.ManualWeeklyTarget = manualTarget; existing.Source = source; existing.SourceRowId = rowId; existing.UpdatedAt = _clock.UtcNow;
            updated++;
        }
        await _db.SaveChangesAsync();
        return new ImportSummary(rows, created, updated, unchanged, errors);
    }
}
