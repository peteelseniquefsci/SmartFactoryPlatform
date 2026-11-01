using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartFactory.Core.Definitions;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Messaging;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Export;
using SmartFactory.Infrastructure.Seed;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Tests;

/// <summary>Section 11 cases 19–22 on the real services (SQLite in memory stands in for MySQL).</summary>
public class WorkflowTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private SmartFactoryDbContext _db = null!;
    private readonly SwitchableUser _actor = new();
    private readonly FixedClock _clock = new(new DateOnly(2026, 11, 11));
    private KraService _kras = null!;
    private WeeklyEntryService _entry = null!;
    private EvaluationService _eval = null!;
    private OrganizationService _org = null!;
    private FiscalHalf _half = null!;
    private CurrentUser _manager = null!, _head = null!, _engineer = null!;

    private class SwitchableUser : ICurrentUserAccessor
    {
        public CurrentUser? User { get; set; }
        public string ActorName => User?.Name ?? "system";
        public int? ActorPersonId => User?.PersonId;
    }
    private class NullEmail : IEmailSender { public Task SendAsync(EmailMessage m, CancellationToken ct = default) => Task.CompletedTask; }

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new SmartFactoryDbContext(new DbContextOptionsBuilder<SmartFactoryDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
        var audit = new AuditTrail(_db, _actor, _clock);
        _org = new OrganizationService(_db);
        var notify = new NotificationService(_db, new NullEmail(), _org);
        _kras = new KraService(_db, audit, _clock, _org, notify);
        _entry = new WeeklyEntryService(_db, audit, _clock);
        _eval = new EvaluationService(_db, _kras, _org);
        var seeder = new Seeder(_db, new TemplateImporter(_db, audit), _kras, NullLogger<Seeder>.Instance, _clock);
        await seeder.SeedAsync(new SeedOptions { DemoPeople = true, DemoActuals = false });
        _half = await new PeriodService(_db, _clock).CurrentHalfAsync();
        _manager = await UserAsync("DEMO-001");      // Smart Factory Manager
        _head = await UserAsync("DEMO-003");         // Sr. System Development Engineer (head, D&P)
        _engineer = await UserAsync("DEMO-004");     // System Development Engineer
    }

    private async Task<CurrentUser> UserAsync(string employeeId)
    {
        var p = await _org.LoadPersonByEmployeeIdAsync(employeeId) ?? throw new Exception(employeeId);
        return new CurrentUser { Person = p, Roles = RoleResolver.Resolve(p) };
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); await _conn.DisposeAsync(); }

    private async Task<Kra> EngineerKraAsync()
    {
        _actor.User = _engineer;
        return await _kras.GetOrCreateAsync(_engineer.Person, _half);
    }

    private async Task<(int line, int basisHi, int basisZero)> IdsAsync()
        => ((await _db.TopGoalLines.FirstAsync(t => t.Measure == "Smart Factory Score")).Id,
            (await _db.RatingBases.FirstAsync(b => b.Key == "normal_hi_cl")).Id,
            (await _db.RatingBases.FirstAsync(b => b.Key == "zero_norm")).Id);

    private static SlotInput Input(int n, string measure, decimal weight, int line, int basis, Aggregation agg, WeeklyRule rule, decimal? plan)
        => new() { SlotNumber = n, Measure = measure, Weight = weight, TopGoalLineId = line, RatingBasisId = basis, Aggregation = agg, WeeklyRule = rule, Plans = Enumerable.Repeat(plan, 6).ToArray() };

    [Fact(DisplayName = "19. KRA with active weights 115% cannot be locked; weight check shows the excess")]
    public async Task WeightsMustBe100()
    {
        var kra = await EngineerKraAsync();
        var (line, hi, zero) = await IdsAsync();
        await _kras.SaveSlotsAsync(kra, new[]
        {
            Input(1, "% closure of committed sub-items", 70, line, hi, Aggregation.LAST, WeeklyRule.RAMP, 1.0m),
            Input(2, "Design defects", 45, line, zero, Aggregation.SUM, WeeklyRule.ZERO, 0),
        }, _engineer);
        var (total, ok) = KraService.WeightCheck(kra);
        Assert.Equal(115m, total); Assert.False(ok);
        var ex = await Assert.ThrowsAsync<RuleViolationException>(() => _kras.SubmitAsync(kra, _engineer));
        Assert.Contains("115", ex.Message);
        _actor.User = _head;
        await Assert.ThrowsAsync<RuleViolationException>(() => _kras.ApproveAndLockAsync(kra, _head));
        Assert.Equal(DefinitionMode.Setup, kra.Mode);
        Assert.Equal(0, kra.Version);
    }

    [Fact(DisplayName = "20. Add, remove, lock, enter actuals and a remark: views follow without re-mapping; removed slot keeps history")]
    public async Task SlotLifecycle()
    {
        var kra = await EngineerKraAsync();
        var (line, hi, zero) = await IdsAsync();
        await _kras.SaveSlotsAsync(kra, new[]
        {
            Input(1, "% closure of committed sub-items", 60, line, hi, Aggregation.LAST, WeeklyRule.RAMP, 1.0m),
            Input(2, "Design defects", 40, line, zero, Aggregation.SUM, WeeklyRule.ZERO, 0),
        }, _engineer);
        var ww41 = FiscalCalendar.AllWeeks(_half).First(w => w.Label == "WW41");
        await _entry.SaveWeekAsync(kra, ww41, new[] { new WeekEntry(2, 1, null, "One design defect found in review") }, _engineer);

        // Remove slot 2 (retire), add slot 3, bring weights back to 100 and lock.
        await _kras.SaveSlotsAsync(kra, new[]
        {
            Input(1, "% closure of committed sub-items", 60, line, hi, Aggregation.LAST, WeeklyRule.RAMP, 1.0m),
            new SlotInput { SlotNumber = 2, Measure = "Design defects", Weight = 40, TopGoalLineId = line, RatingBasisId = zero, Aggregation = Aggregation.SUM, WeeklyRule = WeeklyRule.ZERO, Plans = Enumerable.Repeat<decimal?>(0, 6).ToArray(), Retired = true },
            Input(3, "Post-release defects", 40, line, zero, Aggregation.SUM, WeeklyRule.ZERO, 0),
        }, _engineer);
        await _kras.SubmitAsync(kra, _engineer);
        Assert.Equal(ApprovalStatus.Submitted, kra.Status);

        // The engineer is not their own superior; the head is.
        await Assert.ThrowsAsync<RuleViolationException>(() => _kras.ApproveAndLockAsync(kra, _engineer));
        _actor.User = _head;
        await _kras.ApproveAndLockAsync(kra, _head, "Reviewed against the D&P lines");
        Assert.Equal(DefinitionMode.Locked, kra.Mode);
        Assert.Equal(1, kra.Version);
        Assert.Equal(_head.Name, kra.ApprovedByName);
        Assert.Single(kra.Versions);

        // LOCKED rejects definition edits.
        _actor.User = _engineer;
        await Assert.ThrowsAsync<RuleViolationException>(() => _kras.SaveSlotsAsync(kra, new[] { Input(1, "Changed", 60, line, hi, Aggregation.LAST, WeeklyRule.RAMP, 1.0m) }, _engineer));

        // Weekly entry and a remark on the new slot still work while LOCKED.
        var ww42 = FiscalCalendar.AllWeeks(_half).First(w => w.Label == "WW42");
        await _entry.SaveWeekAsync(kra, ww42, new[] { new WeekEntry(1, 0.95m, null, null), new WeekEntry(3, 0, null, "Clean release") }, _engineer);
        _actor.User = _head;
        var slot3 = kra.Slots.First(s => s.SlotNumber == 3);
        await _entry.AddRemarkAsync(slot3, ww42, null, "Good week", _head);

        var team = await _eval.ForVisiblePeopleAsync(_head, EvaluationContext.WeeklyAt(_half, ww42), includeSelf: false);
        var row = team.Single(e => e.Kra.PersonId == _engineer.PersonId);
        Assert.Equal(new[] { 1, 3 }, row.ActiveSlots.Select(s => s.Slot.SlotNumber).ToArray());
        Assert.Equal(Performance.Rules.Judgement.Met, row.ActiveSlots.First(s => s.Slot.SlotNumber == 3).Week("WW42")!.Judgement);
        Assert.Equal(2, slot3.Remarks.Count);
        var retired = row.Slots.First(s => s.Slot.SlotNumber == 2);
        Assert.True(retired.Slot.Retired);
        Assert.Equal(1m, retired.Week("WW41")!.Actual);                   // history kept
        Assert.Single(retired.Slot.Remarks);
        Assert.Equal(100m, row.WeightTotal);

        var trail = await _kras.ChangeTrailAsync(kra);
        Assert.Contains(trail, a => a.Action == "Lock" && a.NewValue == "LOCKED");
        Assert.Contains(trail, a => a.Entity == "KraSlot" && a.Field == "Retired" && a.NewValue == "True");
        Assert.Contains(trail, a => a.Entity == "WeeklyResult" && a.EntityId.EndsWith("/3/WW42"));
    }

    [Fact(DisplayName = "21. Switch a slot from MANUAL to LAST after manual values exist")]
    public async Task ManualToLast()
    {
        var kra = await EngineerKraAsync();
        var (line, hi, _) = await IdsAsync();
        await _kras.SaveSlotsAsync(kra, new[] { Input(1, "Competency", 100, line, hi, Aggregation.MANUAL, WeeklyRule.MANUAL, 0.80m) }, _engineer);
        var oct = _half.Months[0];
        await _entry.SaveMonthAsync(kra, oct, new[] { new MonthEntry(1, 0.85m, null, null) }, _engineer);
        var ww41 = FiscalCalendar.AllWeeks(_half).First(w => w.Label == "WW41");
        await _entry.SaveWeekAsync(kra, ww41, new[] { new WeekEntry(1, 0.70m, null, null) }, _engineer); // weekly cells unused while MANUAL

        var before = _eval.Evaluate(kra, EvaluationContext.Monthly(_half)).Slots[0].Month(1)!;
        Assert.Equal(0.85m, before.Actual); Assert.Equal(4, before.Rating);

        await _kras.SaveSlotsAsync(kra, new[] { Input(1, "Competency", 100, line, hi, Aggregation.LAST, WeeklyRule.FLAT, 0.80m) }, _engineer);
        var after = _eval.Evaluate(kra, EvaluationContext.Monthly(_half)).Slots[0].Month(1)!;
        Assert.Equal(0.70m, after.Actual);          // now from weeks
        Assert.Equal(1, after.Rating);              // 0.70 / 0.80 = 87.5%
        Assert.Equal(0.85m, after.ManualActual);    // manual value ignored, not deleted
        Assert.Equal(1, await _db.MonthlyManualValues.CountAsync());
    }

    [Fact(DisplayName = "22. Export after 2H: Target Indices form per person equals what the platform shows")]
    public async Task ExportRoundTrip()
    {
        var kra = await EngineerKraAsync();
        var (line, hi, zero) = await IdsAsync();
        await _kras.SaveSlotsAsync(kra, new[]
        {
            Input(1, "% closure of committed sub-items", 60, line, hi, Aggregation.LAST, WeeklyRule.RAMP, 1.0m),
            Input(2, "Design defects", 40, line, zero, Aggregation.SUM, WeeklyRule.ZERO, 0),
        }, _engineer);
        await _kras.SubmitAsync(kra, _engineer);
        _actor.User = _head;
        await _kras.ApproveAndLockAsync(kra, _head);
        _actor.User = _engineer;
        foreach (var w in FiscalCalendar.AllWeeks(_half))
            await _entry.SaveWeekAsync(kra, w, new[] { new WeekEntry(1, w.K == w.N ? 1.0m : 0.9m, null, null), new WeekEntry(2, w.Label == "WW50" ? 1 : 0, null, null) }, _engineer);

        var eval = _eval.Evaluate(kra, EvaluationContext.Monthly(_half));
        Assert.All(eval.MonthTotals, t => Assert.True(t.Complete && t.Final && !t.Provisional));
        var excel = new ExcelExportService();
        using var wb = new XLWorkbook(new MemoryStream(excel.PersonWorkbook(eval, "Smart Factory")));
        var ws = wb.Worksheet("Target Indices");
        var header = 7; int ratingCol = Performance.Export.TargetIndicesLayout.Headers.Length + 1;
        Assert.Equal("Rating Oct 2026", ws.Cell(header, ratingCol).GetString());
        int r = header + 1;
        foreach (var s in eval.ActiveSlots)
        {
            Assert.Equal(s.Slot.SlotNumber, (int)ws.Cell(r, 1).GetDouble());
            Assert.Equal("Smart Factory Score", ws.Cell(r, 4).GetString());
            Assert.Equal(s.Slot.Measure, ws.Cell(r, 9).GetString());
            Assert.Equal((double)s.Slot.Weight, ws.Cell(r, 11).GetDouble());
            for (int m = 0; m < 6; m++) Assert.Equal(s.Month(m + 1)!.Rating!.Value, (int)ws.Cell(r, ratingCol + m).GetDouble());
            Assert.Equal(Math.Round((double)s.HalfRating!.Value, 2), ws.Cell(r, ratingCol + 6).GetDouble());
            Assert.Equal(Math.Round((double)s.HalfWeightedScore!.Value, 4), ws.Cell(r, ratingCol + 7).GetDouble());
            r++;
        }
        Assert.Equal("TOTAL", ws.Cell(r, 9).GetString());
        for (int m = 0; m < 6; m++) Assert.Equal(Math.Round((double)eval.MonthTotals[m].Total!.Value, 2), ws.Cell(r, ratingCol + m).GetDouble());
        Assert.Equal(Math.Round((double)eval.HalfPerformance!.Value, 2), ws.Cell(r, ratingCol + 7).GetDouble());
        // Closure at exactly plan rates 3 (normal_hi_cl); Dec has the one defect: zero_norm → 1. 3 × 60% + 1 × 40% = 2.20; other months 3 × 60% + 5 × 40% = 3.80.
        Assert.Equal(2.20m, eval.MonthTotals[2].Total);
        Assert.Equal(3.80m, eval.MonthTotals[0].Total);

        // Consolidated file carries one sheet per person plus the summary.
        var people = await _eval.ForVisiblePeopleAsync(_head, EvaluationContext.Monthly(_half));
        using var cons = new XLWorkbook(new MemoryStream(excel.ConsolidatedWorkbook(people, "Smart Factory")));
        Assert.Equal(people.Count + 1, cons.Worksheets.Count);
    }

    [Fact(DisplayName = "Workflow 6: a joiner inherits the position template as a SETUP draft; a leaver's KRA is frozen")]
    public async Task JoinerAndLeaver()
    {
        _actor.User = _manager;
        var kra = await _kras.GetOrCreateAsync(_manager.Person, _half);
        Assert.Equal(DefinitionMode.Setup, kra.Mode);
        Assert.Equal(12, kra.Slots.Count(s => s.IsActive));
        Assert.Equal(100m, KraService.WeightCheck(kra).total);
        await _kras.FreezeAsync(kra, "test leaver");
        var ww = FiscalCalendar.AllWeeks(_half).First();
        await Assert.ThrowsAsync<RuleViolationException>(() => _entry.SaveWeekAsync(kra, ww, new[] { new WeekEntry(1, 1, null, null) }, _manager));
    }

    [Fact(DisplayName = "Permissions: a person cannot see another person's KRA; a head sees their section")]
    public async Task Visibility()
    {
        var other = await UserAsync("DEMO-005");
        Assert.False(await _org.CanViewAsync(_engineer, other.Person));
        Assert.True(await _org.CanViewAsync(_head, other.Person));
        Assert.True(await _org.CanViewAsync(_manager, other.Person));
        Assert.False(await _org.IsSuperiorOfAsync(_head, _manager.Person));
    }
}
