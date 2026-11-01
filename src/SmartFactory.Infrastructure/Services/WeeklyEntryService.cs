using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Services;

public record WeekEntry(int SlotNumber, decimal? Actual, decimal? ManualWeeklyTarget, string? Remark);
public record MonthEntry(int SlotNumber, decimal? ManualActual, int? ManualRating, string? Remark);

/// <summary>Workflow 2: weekly entry, manual monthly values and remarks, all audited.</summary>
public class WeeklyEntryService
{
    private readonly SmartFactoryDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;

    public WeeklyEntryService(SmartFactoryDbContext db, IAuditTrail audit, IClock clock) { _db = db; _audit = audit; _clock = clock; }

    public async Task SaveWeekAsync(Kra kra, WorkWeek week, IEnumerable<WeekEntry> entries, CurrentUser actor)
    {
        if (kra.Frozen) throw new RuleViolationException("This KRA is frozen; results cannot change.");
        if (kra.PersonId != actor.PersonId && !actor.IsAdministrator) throw new RuleViolationException("Only the owner enters their weekly actuals.");
        foreach (var e in entries)
        {
            var slot = kra.Slots.FirstOrDefault(s => s.SlotNumber == e.SlotNumber && s.IsActive);
            if (slot is null) continue;
            var result = slot.WeeklyResults.FirstOrDefault(r => r.WorkWeekId == week.Id);
            var hasRemark = !string.IsNullOrWhiteSpace(e.Remark);
            if (result is null)
            {
                if (e.Actual is null && e.ManualWeeklyTarget is null) { if (hasRemark) await AddRemarkAsync(slot, week, null, e.Remark!, actor, save: false); continue; }
                result = new WeeklyResult { KraSlotId = slot.Id, WorkWeekId = week.Id, Source = ResultSource.Typed };
                slot.WeeklyResults.Add(result);
                _db.WeeklyResults.Add(result);
            }
            else if (result.Source != ResultSource.Typed && result.Actual != e.Actual && !hasRemark)
            {
                throw new RuleViolationException($"Slot {slot.SlotNumber} ({week.Label}) is fed by a register; a typed value may override it only with a remark.");
            }

            if (result.Actual != e.Actual)
            {
                _audit.Record("WeeklyResult", $"{kra.Id}/{slot.SlotNumber}/{week.Label}", "Update", "Actual", result.Actual?.ToString(), e.Actual?.ToString(), result.Source != ResultSource.Typed ? $"Override of a {result.Source} value" : null);
                result.Actual = e.Actual;
                if (result.Source != ResultSource.Typed && e.Actual is not null) result.Source = ResultSource.Typed;
            }
            if (result.ManualWeeklyTarget != e.ManualWeeklyTarget)
            {
                _audit.Record("WeeklyResult", $"{kra.Id}/{slot.SlotNumber}/{week.Label}", "Update", "ManualWeeklyTarget", result.ManualWeeklyTarget?.ToString(), e.ManualWeeklyTarget?.ToString());
                result.ManualWeeklyTarget = e.ManualWeeklyTarget;
            }
            result.UpdatedByPersonId = actor.PersonId;
            result.UpdatedAt = _clock.UtcNow;
            if (hasRemark) await AddRemarkAsync(slot, week, null, e.Remark!, actor, save: false);
        }
        await _db.SaveChangesAsync();
    }

    public async Task SaveMonthAsync(Kra kra, FiscalMonth month, IEnumerable<MonthEntry> entries, CurrentUser actor)
    {
        if (kra.Frozen) throw new RuleViolationException("This KRA is frozen; results cannot change.");
        if (kra.PersonId != actor.PersonId && !actor.IsAdministrator) throw new RuleViolationException("Only the owner enters their manual values.");
        foreach (var e in entries)
        {
            var slot = kra.Slots.FirstOrDefault(s => s.SlotNumber == e.SlotNumber && s.IsActive);
            if (slot is null) continue;
            var value = slot.MonthlyManualValues.FirstOrDefault(v => v.FiscalMonthId == month.Id);
            if (value is null)
            {
                if (e.ManualActual is null && e.ManualRating is null) { if (!string.IsNullOrWhiteSpace(e.Remark)) await AddRemarkAsync(slot, null, month, e.Remark, actor, save: false); continue; }
                value = new MonthlyManualValue { KraSlotId = slot.Id, FiscalMonthId = month.Id };
                slot.MonthlyManualValues.Add(value);
                _db.MonthlyManualValues.Add(value);
            }
            if (e.ManualRating is not null && (e.ManualRating < 1 || e.ManualRating > 5)) throw new RuleViolationException($"Slot {slot.SlotNumber}: a typed rating is 1 to 5.");
            if (value.ManualActual != e.ManualActual) { _audit.Record("MonthlyManualValue", $"{kra.Id}/{slot.SlotNumber}/{month.Label}", "Update", "ManualActual", value.ManualActual?.ToString(), e.ManualActual?.ToString()); value.ManualActual = e.ManualActual; }
            if (value.ManualRating != e.ManualRating) { _audit.Record("MonthlyManualValue", $"{kra.Id}/{slot.SlotNumber}/{month.Label}", "Update", "ManualRating", value.ManualRating?.ToString(), e.ManualRating?.ToString()); value.ManualRating = e.ManualRating; }
            value.UpdatedByPersonId = actor.PersonId; value.UpdatedAt = _clock.UtcNow;
            if (!string.IsNullOrWhiteSpace(e.Remark)) await AddRemarkAsync(slot, null, month, e.Remark, actor, save: false);
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>A remark on a slot for a week or a month. A head may write on any slot in their section.</summary>
    public async Task<Remark> AddRemarkAsync(KraSlot slot, WorkWeek? week, FiscalMonth? month, string text, CurrentUser author, bool save = true)
    {
        var r = new Remark { KraSlotId = slot.Id, WorkWeekId = week?.Id, FiscalMonthId = month?.Id, AuthorPersonId = author.PersonId, Text = text.Trim(), CreatedAt = _clock.UtcNow, Author = author.Person, WorkWeek = week, FiscalMonth = month };
        slot.Remarks.Add(r);
        _db.Remarks.Add(r);
        _audit.Record("Remark", $"{slot.KraId}/{slot.SlotNumber}/{week?.Label ?? month?.Label}", "Create", newValue: r.Text);
        if (save) await _db.SaveChangesAsync();
        return r;
    }
}
