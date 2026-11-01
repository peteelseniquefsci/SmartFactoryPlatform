using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Performance.Rules;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

[Authorize(Policy = Policies.HeadOrManager)]
[Route("weekbyweek")]
public class WeekByWeekController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly EvaluationService _eval;
    private readonly OrganizationService _org;
    private readonly SmartFactoryDbContext _db;

    public WeekByWeekController(WebCurrentUserAccessor current, PeriodService periods, EvaluationService eval, OrganizationService org, SmartFactoryDbContext db)
    { _current = current; _periods = periods; _eval = eval; _org = org; _db = db; }

    /// <summary>scope = department | section:{id} | person:{id}. The grid shows the KRA of the department manager, the section head, or the person.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? scope)
    {
        var user = (await _current.LoadAsync())!;
        var half = await _periods.CurrentHalfAsync();
        var week = _periods.ThisWeek(half);
        var weeks = FiscalCalendar.AllWeeks(half).ToList();
        var people = await _org.VisiblePeopleAsync(user);
        var positions = await _org.PositionsAsync();
        var sections = await _org.SectionsAsync();

        var options = new List<(string name, string title)>();
        var topPos = positions.First(p => p.ReportsToPositionId is null);
        if (user.Is(Core.Organization.PlatformRole.Manager) || user.IsAdministrator || people.Any(p => p.PositionId == topPos.Id)) options.Add(("department", "Department"));
        foreach (var s in sections.Where(s => s.ParentSectionId is not null))
        {
            var headPos = positions.FirstOrDefault(p => p.SectionId == s.Id && p.IsSectionHead);
            if (headPos is not null && people.Any(p => p.PositionId == headPos.Id)) options.Add(($"section:{s.Id}", $"Section · {s.Name}"));
        }
        foreach (var p in people) options.Add(($"person:{p.Id}", p.Name));

        scope = options.Any(o => o.name == scope) ? scope! : options.FirstOrDefault().name ?? $"person:{user.PersonId}";
        int personId;
        string title;
        if (scope == "department")
        {
            var holder = await _db.People.FirstOrDefaultAsync(p => p.PositionId == topPos.Id && p.Active);
            personId = holder?.Id ?? user.PersonId; title = "Department";
        }
        else if (scope.StartsWith("section:"))
        {
            var sid = int.Parse(scope[8..]);
            var headPos = positions.First(p => p.SectionId == sid && p.IsSectionHead);
            var holder = people.First(p => p.PositionId == headPos.Id);
            personId = holder.Id; title = $"Section · {sections.First(s => s.Id == sid).Name}";
        }
        else { personId = int.Parse(scope[7..]); title = people.First(p => p.Id == personId).Name; }

        var eval = await _eval.ForPersonAsync(personId, EvaluationContext.WeeklyAt(half, week));
        var rows = eval?.ActiveSlots.Select(s => (s.Slot.Measure, weeks.Select(w => s.Week(w.Label)?.Judgement).ToList())).ToList() ?? new List<(string, List<Judgement?>)>();
        return View(new WeekByWeekViewModel { User = user, Half = half, Week = week, Scope = scope, ScopeTitle = title, ScopeOptions = options, Weeks = weeks, Rows = rows });
    }
}
