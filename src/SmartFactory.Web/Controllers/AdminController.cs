using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Import;
using SmartFactory.Infrastructure.Seed;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;
using SmartFactory.Web.Infrastructure;

namespace SmartFactory.Web.Controllers;

[Authorize(Policy = Policies.Administrator)]
[Route("admin")]
public class AdminController : Controller
{
    private readonly SmartFactoryDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly PeriodService _periods;
    private readonly OrganizationService _org;
    private readonly KraService _kras;
    private readonly IIdentityProvider _identity;
    private readonly TemplateImporter _templates;
    private readonly WeeklyResultImporter _results;
    private readonly WebCurrentUserAccessor _current;
    private readonly IClock _clock;

    public AdminController(SmartFactoryDbContext db, IAuditTrail audit, PeriodService periods, OrganizationService org, KraService kras, IIdentityProvider identity, TemplateImporter templates, WeeklyResultImporter results, WebCurrentUserAccessor current, IClock clock)
    { _db = db; _audit = audit; _periods = periods; _org = org; _kras = kras; _identity = identity; _templates = templates; _results = results; _current = current; _clock = clock; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.People = await _db.People.CountAsync(p => p.Active);
        ViewBag.Positions = await _db.Positions.CountAsync();
        ViewBag.Halves = await _periods.HalvesAsync();
        ViewBag.Bases = await _db.RatingBases.CountAsync();
        ViewBag.Lines = await _db.TopGoalLines.CountAsync();
        ViewBag.Templates = await _db.PositionTemplateSlots.CountAsync();
        ViewBag.Audit = await _db.AuditEntries.OrderByDescending(a => a.Id).Take(30).ToListAsync();
        return View();
    }

    // ---------- Organization ----------
    [HttpGet("organization")]
    public async Task<IActionResult> Organization()
    {
        ViewBag.Sections = await _org.SectionsAsync();
        ViewBag.Positions = await _org.PositionsAsync();
        ViewBag.People = await _db.People.Include(p => p.Position).Include(p => p.Roles).OrderBy(p => p.Position!.SortOrder).ThenBy(p => p.Name).ToListAsync();
        ViewBag.IsDevelopment = _identity.IsDevelopment;
        return View();
    }

    [HttpPost("positions/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePosition(int? id, string title, int sectionId, int? reportsToPositionId, int seats, bool isSectionHead, bool isManager, string? externalReportsTo)
    {
        var p = id is null ? new Position { SortOrder = (await _db.Positions.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1 } : await _db.Positions.FindAsync(id.Value);
        if (p is null) return NotFound();
        if (id is null) _db.Positions.Add(p);
        var before = $"{p.Title}|{p.SectionId}|{p.ReportsToPositionId}|{p.Seats}|{p.IsSectionHead}|{p.IsManager}";
        p.Title = title.Trim(); p.SectionId = sectionId; p.ReportsToPositionId = reportsToPositionId == p.Id ? null : reportsToPositionId; p.Seats = seats; p.IsSectionHead = isSectionHead; p.IsManager = isManager; p.ExternalReportsTo = string.IsNullOrWhiteSpace(externalReportsTo) ? null : externalReportsTo.Trim();
        await _db.SaveChangesAsync();
        _audit.Record("Position", p.Id.ToString(), id is null ? "Create" : "Update", oldValue: before, newValue: $"{p.Title}|{p.SectionId}|{p.ReportsToPositionId}|{p.Seats}|{p.IsSectionHead}|{p.IsManager}");
        await _db.SaveChangesAsync();
        TempData["Ok"] = "Position saved.";
        return Redirect("/admin/organization");
    }

    [HttpPost("people/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePerson(int? id, string employeeId, string? name, string? email, int positionId, Assignment assignment, DateOnly? startDate, DateOnly? endDate, bool active = true)
    {
        var person = id is null ? new Person() : await _db.People.FindAsync(id.Value);
        if (person is null) return NotFound();
        employeeId = employeeId.Trim();
        if (id is null)
        {
            var external = await _identity.LookupAsync(employeeId);
            if (external is null && !_identity.IsDevelopment) throw new RuleViolationException($"Employee {employeeId} was not found in the company employee-information API.");
            if (external is null && string.IsNullOrWhiteSpace(name)) throw new RuleViolationException("Give the person's name (development provider has no directory).");
            person.EmployeeId = external?.EmployeeId ?? employeeId; person.Name = external?.DisplayName ?? name!.Trim(); person.Email = external?.Email ?? email;
            if (await _db.People.AnyAsync(p => p.EmployeeId == person.EmployeeId)) throw new RuleViolationException($"Employee {person.EmployeeId} is already on the table of organization.");
            _db.People.Add(person);
        }
        else if (!string.IsNullOrWhiteSpace(name)) { person.Name = name.Trim(); person.Email = email; }
        var before = $"{person.PositionId}|{person.Assignment}|{person.Active}|{person.EndDate}";
        var wasActive = person.Active;
        person.PositionId = positionId; person.Assignment = assignment; person.StartDate = startDate ?? person.StartDate; person.EndDate = endDate; person.Active = active && (endDate is null || endDate >= _clock.Today);
        await _db.SaveChangesAsync();
        _audit.Record("Person", person.Id.ToString(), id is null ? "Create" : "Update", oldValue: before, newValue: $"{person.PositionId}|{person.Assignment}|{person.Active}|{person.EndDate}");
        if (wasActive && !person.Active)
        {
            foreach (var kra in await _db.Kras.Where(k => k.PersonId == person.Id && !k.Frozen).ToListAsync()) await _kras.FreezeAsync(kra, "Leaver: KRA frozen with its results");
        }
        await _db.SaveChangesAsync();
        TempData["Ok"] = id is null ? $"{person.Name} added. Their KRA is created as a SETUP draft from the position template when they first sign in." : "Person saved.";
        return Redirect("/admin/organization");
    }

    [HttpPost("roles/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRole(int personId, PlatformRole role)
    {
        if (role is not (PlatformRole.Administrator or PlatformRole.HrViewer)) throw new RuleViolationException("Only Administrator and HR viewer are assigned; the other roles follow the organization.");
        var user = (await _current.LoadAsync())!;
        var existing = await _db.RoleAssignments.FirstOrDefaultAsync(r => r.PersonId == personId && r.Role == role);
        if (existing is not null) { _db.RoleAssignments.Remove(existing); _audit.Record("RoleAssignment", personId.ToString(), "Revoke", newValue: role.ToString()); }
        else { _db.RoleAssignments.Add(new RoleAssignment { PersonId = personId, Role = role, GrantedBy = user.Name, GrantedAt = _clock.UtcNow }); _audit.Record("RoleAssignment", personId.ToString(), "Grant", newValue: role.ToString()); }
        await _db.SaveChangesAsync();
        return Redirect("/admin/organization");
    }

    // ---------- Periods ----------
    [HttpGet("periods")]
    public async Task<IActionResult> Periods()
    {
        var halves = await _db.FiscalHalves.Include(h => h.Months).ThenInclude(m => m.Weeks).OrderByDescending(h => h.StartDate).ToListAsync();
        foreach (var h in halves) { h.Months = h.Months.OrderBy(m => m.Index).ToList(); foreach (var m in h.Months) m.Weeks = m.Weeks.OrderBy(w => w.K).ToList(); }
        return View(halves);
    }

    [HttpPost("periods/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePeriod(int fiscalYear, int half, bool makeCurrent)
    {
        var h = await _periods.EnsureHalfAsync(fiscalYear, half, makeCurrent);
        _audit.Record("FiscalHalf", h.Id.ToString(), "Create", detail: h.Label);
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"{h.Label} is ready with its generated calendar.";
        return Redirect("/admin/periods");
    }

    [HttpPost("periods/current")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCurrent(int id)
    {
        foreach (var h in await _db.FiscalHalves.ToListAsync()) h.IsCurrent = h.Id == id;
        await _db.SaveChangesAsync();
        return Redirect("/admin/periods");
    }

    // ---------- Rating basis library ----------
    [HttpGet("rating-bases")]
    public async Task<IActionResult> RatingBases() => View(await _db.RatingBases.OrderBy(b => b.Mode).ThenBy(b => b.Key).ToListAsync());

    [HttpPost("rating-bases/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRatingBasis(int? id, string key, string name, RatingMode mode, decimal? t5, decimal? t4, decimal? t3, decimal? t2, string? usedFor, string? notes, bool active = true)
    {
        var b = id is null ? new RatingBasis() : await _db.RatingBases.FindAsync(id.Value);
        if (b is null) return NotFound();
        if (id is null) _db.RatingBases.Add(b);
        var before = $"{b.Mode} {b.T5} {b.T4} {b.T3} {b.T2}";
        b.Key = key.Trim(); b.Name = name.Trim(); b.Mode = mode; b.T5 = t5; b.T4 = t4; b.T3 = t3; b.T2 = t2; b.UsedFor = usedFor; b.Notes = notes; b.Active = active;
        await _db.SaveChangesAsync();
        _audit.Record("RatingBasis", b.Key, id is null ? "Create" : "Update", oldValue: before, newValue: $"{b.Mode} {b.T5} {b.T4} {b.T3} {b.T2}");
        await _db.SaveChangesAsync();
        TempData["Ok"] = "Rating basis saved. Ratings are derived, so every slot using it now rates with the new bands.";
        return Redirect("/admin/rating-bases");
    }

    // ---------- Top Goal lines ----------
    [HttpGet("top-goals")]
    public async Task<IActionResult> TopGoals() => View(await _db.TopGoalLines.OrderBy(t => t.SortOrder).ToListAsync());

    [HttpPost("top-goals/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTopGoal(int? id, string focusItem, string strategicObjective, string measure, bool active = true)
    {
        var t = id is null ? new TopGoalLine { SortOrder = (await _db.TopGoalLines.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1 } : await _db.TopGoalLines.FindAsync(id.Value);
        if (t is null) return NotFound();
        if (id is null) _db.TopGoalLines.Add(t);
        var before = $"{t.FocusItem}|{t.StrategicObjective}|{t.Measure}|{t.Active}";
        t.FocusItem = focusItem.Trim(); t.StrategicObjective = strategicObjective.Trim(); t.Measure = measure.Trim(); t.Active = active;
        await _db.SaveChangesAsync();
        _audit.Record("TopGoalLine", t.Id.ToString(), id is null ? "Create" : "Update", oldValue: before, newValue: $"{t.FocusItem}|{t.StrategicObjective}|{t.Measure}|{t.Active}");
        await _db.SaveChangesAsync();
        return Redirect("/admin/top-goals");
    }

    // ---------- Imports ----------
    [HttpGet("imports")]
    public async Task<IActionResult> Imports()
    {
        ViewBag.Templates = await _db.PositionTemplateSlots.Include(t => t.Position).GroupBy(t => t.Position!.Title).Select(g => new { Position = g.Key, Count = g.Count() }).ToListAsync();
        ViewBag.Halves = await _periods.HalvesAsync();
        return View();
    }

    [HttpPost("imports/templates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportTemplates(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var (p, s, errors) = await _templates.ImportAsync(await reader.ReadToEndAsync());
        TempData["Ok"] = $"Templates imported: {p} positions, {s} slots." + (errors.Count > 0 ? $" {errors.Count} rows skipped: {string.Join(" ", errors.Take(5))}" : "");
        return Redirect("/admin/imports");
    }

    [HttpPost("imports/results")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportResults(IFormFile file, int halfId, ResultSource source, bool overwriteTyped)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var summary = await _results.ImportCsvAsync(reader, halfId, source, overwriteTyped);
        TempData["Ok"] = $"Import: {summary.Rows} rows, {summary.Created} created, {summary.Updated} updated, {summary.Unchanged} unchanged." + (summary.Errors.Count > 0 ? $" {summary.Errors.Count} notes: {string.Join(" ", summary.Errors.Take(5))}" : "");
        return Redirect("/admin/imports");
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(string? entity, int page = 1)
    {
        var q = _db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.Entity == entity);
        ViewBag.Entity = entity; ViewBag.Page = page;
        return View(await q.OrderByDescending(a => a.Id).Skip((page - 1) * 100).Take(100).ToListAsync());
    }
}
