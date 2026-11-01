using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;

namespace SmartFactory.Infrastructure.Services;

public class PeriodService
{
    private readonly SmartFactoryDbContext _db;
    private readonly IClock _clock;
    public PeriodService(SmartFactoryDbContext db, IClock clock) { _db = db; _clock = clock; }

    public async Task<FiscalHalf> CurrentHalfAsync()
    {
        var half = await _db.FiscalHalves.Include(h => h.Months).ThenInclude(m => m.Weeks).AsSplitQuery().FirstOrDefaultAsync(h => h.IsCurrent)
                   ?? await _db.FiscalHalves.Include(h => h.Months).ThenInclude(m => m.Weeks).AsSplitQuery().OrderByDescending(h => h.StartDate).FirstAsync();
        Sort(half);
        return half;
    }

    public async Task<FiscalHalf?> HalfAsync(int id)
    {
        var half = await _db.FiscalHalves.Include(h => h.Months).ThenInclude(m => m.Weeks).AsSplitQuery().FirstOrDefaultAsync(h => h.Id == id);
        if (half is not null) Sort(half);
        return half;
    }

    public Task<List<FiscalHalf>> HalvesAsync() => _db.FiscalHalves.Include(h => h.Months).OrderByDescending(h => h.StartDate).ToListAsync();

    /// <summary>This week: the work week holding today, or the nearest week of the half when today is outside it.</summary>
    public WorkWeek ThisWeek(FiscalHalf half)
    {
        var today = _clock.Today;
        var weeks = FiscalCalendar.AllWeeks(half).ToList();
        return FiscalCalendar.WeekOf(half, today) ?? (today < weeks[0].StartDate ? weeks[0] : weeks[^1]);
    }

    public DateOnly Today => _clock.Today;

    /// <summary>Creates a half and its generated calendar if it does not exist yet (administrator).</summary>
    public async Task<FiscalHalf> EnsureHalfAsync(int fiscalYear, int halfNo, bool makeCurrent)
    {
        var existing = await _db.FiscalHalves.FirstOrDefaultAsync(h => h.FiscalYear == fiscalYear && h.Half == halfNo);
        if (existing is null)
        {
            existing = FiscalCalendar.Build(fiscalYear, halfNo);
            _db.FiscalHalves.Add(existing);
        }
        if (makeCurrent)
        {
            foreach (var h in await _db.FiscalHalves.ToListAsync()) h.IsCurrent = false;
            existing.IsCurrent = true;
        }
        await _db.SaveChangesAsync();
        return existing;
    }

    private static void Sort(FiscalHalf half)
    {
        half.Months = half.Months.OrderBy(m => m.Index).ToList();
        foreach (var m in half.Months) { m.Weeks = m.Weeks.OrderBy(w => w.K).ToList(); m.FiscalHalf = half; foreach (var w in m.Weeks) w.FiscalMonth = m; }
    }
}
