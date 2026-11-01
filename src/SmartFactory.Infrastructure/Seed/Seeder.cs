using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;

namespace SmartFactory.Infrastructure.Seed;

public class SeedOptions
{
    /// <summary>Seed demo people (one or more per position) and demo actuals. Development only; production people come from the company API.</summary>
    public bool DemoPeople { get; set; }
    public bool DemoActuals { get; set; }
    public int FiscalYear { get; set; } = 2026;
    public int Half { get; set; } = 2;
}

/// <summary>Seeds section 6 of the specification: organization, periods, Top Goal lines, rating library, templates. Idempotent.</summary>
public class Seeder
{
    private readonly SmartFactoryDbContext _db;
    private readonly TemplateImporter _templates;
    private readonly KraService _kras;
    private readonly ILogger<Seeder> _log;
    private readonly IClock _clock;

    public Seeder(SmartFactoryDbContext db, TemplateImporter templates, KraService kras, ILogger<Seeder> log, IClock clock)
    { _db = db; _templates = templates; _kras = kras; _log = log; _clock = clock; }

    public async Task SeedAsync(SeedOptions options)
    {
        await SeedOrganizationAsync();
        await SeedPeriodsAsync(options.FiscalYear, options.Half);
        await SeedTopGoalLinesAsync();
        await SeedRatingBasesAsync();
        await SeedTemplatesAsync();
        if (options.DemoPeople) await SeedDemoPeopleAsync();
        if (options.DemoPeople && options.DemoActuals) await SeedDemoActualsAsync();
    }

    private async Task SeedOrganizationAsync()
    {
        if (await _db.Departments.AnyAsync()) return;
        var dept = new Department { Name = "Smart Factory" };
        _db.Departments.Add(dept);
        Section S(string name, string code, int order, Section? parent, string? ownership) { var s = new Section { Department = dept, Name = name, Code = code, SortOrder = order, ParentSection = parent, Ownership = ownership }; _db.Sections.Add(s); return s; }
        var sf = S("Smart Factory", "SF", 1, null, "Carries the blended company Compliance and Factory Scores.");
        var ds = S("Digital Systems", "DS", 2, sf, "Carries both Scores for Traceability, Quality Intelligence and Visualization.");
        var dp = S("Design & Project", "D&P", 3, ds, "Compliance Score (primary) and Factory Score (shared) for Traceability, Quality Intelligence and Visualization.");
        var dev = S("Development", "DEV", 4, ds, "Compliance Score for Traceability, Quality Intelligence and Visualization.");
        var ins = S("Implementation & Support", "I&S", 5, ds, "Factory Score for Traceability, Quality Intelligence and Visualization.");
        var ea = S("Equipment & Automation", "E&A", 6, sf, "Compliance and Factory Scores for Process Equipment.");

        var order = 0;
        Position P(string title, Section section, Position? reportsTo, int seats, bool head = false, bool manager = false, string? external = null)
        {
            var p = new Position { Title = title, Section = section, ReportsTo = reportsTo, Seats = seats, IsSectionHead = head, IsManager = manager, ExternalReportsTo = external, SortOrder = ++order };
            _db.Positions.Add(p); return p;
        }
        var sfm = P("Smart Factory Manager", sf, null, 1, head: true, manager: true, external: "General Manager");
        var dsm = P("Digital Systems Manager", ds, sfm, 1, head: true, manager: true);
        var srSde = P("Sr. System Development Engineer", dp, dsm, 1, head: true);
        var sde = P("System Development Engineer", dp, srSde, 8);
        P("System Development Technician", dp, sde, 6);
        var srSwe = P("Sr. Software Development Engineer", dev, dsm, 1, head: true);
        P("Software Development Engineer", dev, srSwe, 9);
        P("System Test Engineer", dev, srSwe, 2);
        P("DevOps Engineer", dev, srSwe, 1);
        var srSie = P("Sr. System Implementation Engineer", ins, dsm, 1, head: true);
        var sup = P("System Implementation Supervisor", ins, srSie, 2);
        P("System Implementation Technician", ins, sup, 8);
        var srEde = P("Sr. Equipment Development Engineer", ea, sfm, 1, head: true);
        P("Equipment Development Engineer", ea, srEde, 7);
        P("Equipment Development Technician", ea, srEde, 6);
        P("Department Assistant", ds, dsm, 1);
        await _db.SaveChangesAsync();
        _log.LogInformation("Seeded organization: 6 sections, 16 positions");
    }

    private async Task SeedPeriodsAsync(int fy, int half)
    {
        if (await _db.FiscalHalves.AnyAsync(h => h.FiscalYear == fy && h.Half == half)) return;
        var fh = FiscalCalendar.Build(fy, half);
        fh.IsCurrent = !await _db.FiscalHalves.AnyAsync(h => h.IsCurrent);
        _db.FiscalHalves.Add(fh);
        await _db.SaveChangesAsync();
        _log.LogInformation("Seeded {Label}: {Weeks} work weeks", fh.Label, FiscalCalendar.AllWeeks(fh).Count());
    }

    private async Task SeedTopGoalLinesAsync()
    {
        if (await _db.TopGoalLines.AnyAsync()) return;
        var rows = new (string f, string o, string m)[]
        {
            ("Financials", "Generate Significant Profit", "Profit After Tax (Company Wide)"),
            ("Financials", "Improve Automotive Profit", "Profit After Tax (Automotive)"),
            ("Financials", "Generate significant cost reduction", "Direct Cost Variance (Overall)"),
            ("Competitiveness (Customer Focus)", "Improve Customer Experience", "Customer Rating"),
            ("Competitiveness (Customer Focus)", "Improve Customer Experience", "Customer Claim"),
            ("Competitiveness (Customer Focus)", "Improve Customer Experience", "Delivery (LLPFAC)"),
            ("Competitiveness (Customer Focus)", "Technology Upgrade", "Smart Factory Score"),
            ("Business Process", "Prevent Safety and Environmental Accident", "Number of Disabling Injury and Environmental Accident"),
            ("Business Process", "Prevent Safety and Environmental Accident", "Number of Non Disabling Injury and Environmental Accident"),
            ("Business Process", "Prevent Safety and Environmental Accident", "Number of Near miss Injury and Environmental Accident"),
            ("Business Process", "Legal, Regulatory, Statutory Compliance", "Number of Major/Critical/Priority Non Compliance"),
            ("Business Process", "Diversity Supplier Base", "Sole Source Supplier Count (Chemicals)"),
            ("Business Process", "Enhancement of Existing Machine & Facilities", "Project Completion Rate"),
            ("Learning and Growth", "Increase Employee Engagement and Satisfaction", "Turnover Rate (Key Personnel G7 & above)"),
            ("Learning and Growth", "Raise Competency Levels of Personnel (Grade 7 and above)", "Competency Assessment Result"),
            ("Learning and Growth", "Process Innovation and Integration", "Reduction of Manual Process (Automation)"),
        };
        int i = 0;
        foreach (var (f, o, m) in rows) _db.TopGoalLines.Add(new TopGoalLine { FocusItem = f, StrategicObjective = o, Measure = m, SortOrder = ++i });
        await _db.SaveChangesAsync();
    }

    private async Task SeedRatingBasesAsync()
    {
        if (await _db.RatingBases.AnyAsync()) return;
        void B(string key, string name, RatingMode mode, decimal? t5, decimal? t4, decimal? t3, decimal? t2, string usedFor, string? notes = null)
            => _db.RatingBases.Add(new RatingBasis { Key = key, Name = name, Mode = mode, T5 = t5, T4 = t4, T3 = t3, T2 = t2, UsedFor = usedFor, Notes = notes,
                Band5Text = mode == RatingMode.M ? "Typed 5" : null, Band1Text = mode == RatingMode.M ? "Typed 1" : null });
        B("normal_hi", "Normal, higher is better", RatingMode.RH, 1.10m, 1.06m, 1.00m, 0.90m, "Scores vs. ramp, % within SLA, competency");
        B("normal_hi_cl", "Normal, higher is better (closure)", RatingMode.RH, 1.10m, 1.06m, 1.00m, 0.90m, "% closure of committed sub-items");
        B("difficult_hi", "Difficult, higher is better", RatingMode.RH, 1.05m, 1.03m, 1.00m, 0.95m, "Tight higher-is-better");
        B("easy_hi", "Easy, higher is better", RatingMode.RH, 1.20m, 1.11m, 1.00m, 0.90m, "Savings amounts");
        B("normal_lo_cnt", "Normal, lower is better (counts)", RatingMode.RL, 0.90m, 0.95m, 1.00m, 1.10m, "Defect counts vs. monthly target");
        B("difficult_lo", "Difficult, lower is better", RatingMode.RL, 0.95m, 0.97m, 1.00m, 1.05m, "Tight lower-is-better");
        B("iso_dt", "Isolated downtime", RatingMode.RL, 0.80m, 0.90m, 1.00m, 1.10m, "Isolated downtime minutes vs. 120-min allowance");
        B("easy_lo", "Easy, lower is better", RatingMode.RL, 0.80m, 0.90m, 1.00m, 1.10m, "Generic wide lower-is-better");
        B("zero_norm", "Zero target", RatingMode.AL, 0, -1, -1, -1, "Safety, legal, unmet training, overdue issues, projects behind", "0 = 5, anything else = 1");
        B("zero_diff", "Zero target, difficult", RatingMode.AL, 0, -1, -1, 1, "Plant-wide outages >30 min, resignations, escaped defects, E&A abnormalities", "0 = 5, 1 = 2, 2 or more = 1");
        B("zero_diff2", "Zero target, difficult (alternative)", RatingMode.AL, 0, -1, -1, 2, "Alternative for abnormalities", "0 = 5, 1–2 = 2, >2 = 1");
        B("hundred_norm", "100% target", RatingMode.RH, 0.9999m, 9, 9, 9, "100% targets: 5 if met else 1");
        B("hundred_diff", "100% target, difficult", RatingMode.RH, 0.9999m, 9, 9, 0.98m, "100% targets: 5 / 2 (98–99%) / 1");
        B("milestone", "Milestone (typed rating)", RatingMode.M, null, null, null, null, "T.O. positions filled; typed rating");
        await _db.SaveChangesAsync();
    }

    private async Task SeedTemplatesAsync()
    {
        if (await _db.PositionTemplateSlots.AnyAsync()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "kra-templates.json");
        if (!File.Exists(path)) { _log.LogWarning("Template file not found at {Path}", path); return; }
        var (p, s, errors) = await _templates.ImportAsync(await File.ReadAllTextAsync(path));
        foreach (var e in errors) _log.LogWarning("Template import: {Error}", e);
        _log.LogInformation("Seeded templates: {Positions} positions, {Slots} slots", p, s);
    }

    private static readonly (string position, string[] names)[] DemoPeople =
    {
        ("Smart Factory Manager", new[] { "Pete Velasquez" }),
        ("Digital Systems Manager", new[] { "Maria Santos" }),
        ("Sr. System Development Engineer", new[] { "Jon Reyes" }),
        ("System Development Engineer", new[] { "Ana Cruz", "Luis Garcia", "Kim Dela Cruz" }),
        ("System Development Technician", new[] { "Rey Bautista", "Joy Mendoza" }),
        ("Sr. Software Development Engineer", new[] { "Carlo Ramos" }),
        ("Software Development Engineer", new[] { "Bea Torres", "Mark Villanueva", "Nina Flores" }),
        ("System Test Engineer", new[] { "Paolo Castro" }),
        ("DevOps Engineer", new[] { "Ivy Navarro" }),
        ("Sr. System Implementation Engineer", new[] { "Dan Aquino" }),
        ("System Implementation Supervisor", new[] { "Lea Fernandez", "Ron Pascual" }),
        ("System Implementation Technician", new[] { "Gab Morales", "Tin Lopez", "Jay Domingo" }),
        ("Sr. Equipment Development Engineer", new[] { "Vic Salazar" }),
        ("Equipment Development Engineer", new[] { "Mae Gutierrez", "Ben Tolentino" }),
        ("Equipment Development Technician", new[] { "Cris Rivera", "Dex Marquez" }),
        ("Department Assistant", new[] { "Sam Ocampo" }),
    };

    private async Task SeedDemoPeopleAsync()
    {
        if (await _db.People.AnyAsync()) return;
        var positions = await _db.Positions.ToListAsync();
        int n = 0;
        foreach (var (title, names) in DemoPeople)
        {
            var pos = positions.First(p => p.Title == title);
            foreach (var name in names)
            {
                n++;
                var assignment = pos.Section?.Code is "D&P" or "DEV" ? (n % 2 == 0 ? Assignment.Customer : Assignment.Enterprise) : Assignment.None;
                _db.People.Add(new Person
                {
                    EmployeeId = $"DEMO-{n:000}", Name = name, Email = $"demo{n:000}@example.invalid", PositionId = pos.Id, Assignment = assignment,
                    StartDate = new DateOnly(2026, 4, 1), Active = true,
                });
            }
        }
        await _db.SaveChangesAsync();
        var admin = await _db.People.FirstAsync(p => p.EmployeeId == "DEMO-001");
        _db.RoleAssignments.Add(new RoleAssignment { PersonId = admin.Id, Role = PlatformRole.Administrator, GrantedBy = "seed", GrantedAt = _clock.UtcNow });
        var hr = new Person { EmployeeId = "DEMO-HR", Name = "HR Viewer (demo)", Email = "hr@example.invalid", PositionId = positions.First(p => p.Title == "Department Assistant").Id, StartDate = new DateOnly(2026, 4, 1), Active = true };
        _db.People.Add(hr);
        await _db.SaveChangesAsync();
        _db.RoleAssignments.Add(new RoleAssignment { PersonId = hr.Id, Role = PlatformRole.HrViewer, GrantedBy = "seed", GrantedAt = _clock.UtcNow });
        await _db.SaveChangesAsync();
        _log.LogInformation("Seeded {Count} demo people", n + 1);
    }

    /// <summary>Demo: the manager's and heads' KRAs locked as version 1 by "migration" with October actuals and the first November weeks.</summary>
    private async Task SeedDemoActualsAsync()
    {
        if (await _db.Kras.AnyAsync()) return;
        var half = await _db.FiscalHalves.Include(h => h.Months).ThenInclude(m => m.Weeks).FirstAsync(h => h.IsCurrent);
        var weeks = FiscalCalendar.AllWeeks(half).ToList();
        var people = await _db.People.Include(p => p.Position).Where(p => p.EmployeeId.StartsWith("DEMO-")).ToListAsync();
        foreach (var person in people)
        {
            var kra = await _kras.GetOrCreateAsync(person, half);
            if (!kra.Slots.Any(s => s.IsActive)) continue;
            var (_, ok) = KraService.WeightCheck(kra);
            if (!ok)
            {
                // Head templates carry only the Score slots; pad with a sectional closure slot so the demo KRA locks (interim, clearly marked).
                var remaining = 100m - kra.Slots.Where(s => s.IsActive).Sum(s => s.Weight);
                var closureBasis = await _db.RatingBases.FirstAsync(b => b.Key == "normal_hi_cl");
                var line = await _db.TopGoalLines.FirstAsync(t => t.Measure == "Smart Factory Score");
                var free = kra.Slots.First(s => !s.IsActive);
                free.Measure = "% closure of committed sub-items (demo placeholder until the definitions workbook is imported)";
                free.TopGoalLineId = line.Id; free.RatingBasisId = closureBasis.Id; free.Weight = remaining; free.Aggregation = Aggregation.LAST; free.WeeklyRule = WeeklyRule.RAMP;
                free.TargetText = "100% of the month's committed sub-items"; free.Plan1 = free.Plan2 = free.Plan3 = free.Plan4 = free.Plan5 = free.Plan6 = 1.0m; free.Notes = "Demo placeholder.";
                await _db.SaveChangesAsync();
            }
            await _kras.LockAsSystemAsync(kra, "migration", "Seed: locked as version 1");

            var rnd = new Random(person.Id * 7919);
            foreach (var slot in kra.Slots.Where(s => s.IsActive && s.Aggregation != Aggregation.MANUAL))
            {
                foreach (var w in weeks.Where(w => w.Sequence <= 6)) // WW40..WW45
                {
                    var month = half.Months.First(m => m.Id == w.FiscalMonthId);
                    var plan = slot.PlanFor(month.Index);
                    if (plan is null) continue;
                    decimal actual;
                    if (slot.WeeklyRule == WeeklyRule.ZERO) actual = rnd.NextDouble() < 0.08 ? 1 : 0;
                    else if (slot.WeeklyRule == WeeklyRule.PRORATE) actual = decimal.Round(plan.Value / w.N * (decimal)(0.4 + rnd.NextDouble() * 0.9), 0);
                    else if (slot.WeeklyRule == WeeklyRule.FLAT) actual = decimal.Round(plan.Value * (decimal)(0.85 + rnd.NextDouble() * 0.2), 3);
                    else
                    {
                        var prev = month.Index == 1 ? (slot.StartValue ?? 0m) : (slot.PlanFor(month.Index - 1) ?? 0m);
                        var target = prev + (plan.Value - prev) * w.K / w.N;
                        actual = decimal.Round(target * (decimal)(0.88 + rnd.NextDouble() * 0.22), 2);
                    }
                    _db.WeeklyResults.Add(new WeeklyResult { KraSlotId = slot.Id, WorkWeekId = w.Id, Actual = actual, Source = ResultSource.Typed, UpdatedAt = _clock.UtcNow });
                }
            }
            foreach (var slot in kra.Slots.Where(s => s.IsActive && s.RatingBasis?.Mode == RatingMode.M))
                _db.MonthlyManualValues.Add(new MonthlyManualValue { KraSlotId = slot.Id, FiscalMonthId = half.Months.First(m => m.Index == 1).Id, ManualRating = 4, UpdatedAt = _clock.UtcNow });
            await _db.SaveChangesAsync();
        }
        _log.LogInformation("Seeded demo KRAs and actuals");
    }
}
