using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

[Authorize(Policy = Policies.HeadOrManager)]
[Route("team")]
public class TeamController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly EvaluationService _eval;
    private readonly OrganizationService _org;

    public TeamController(WebCurrentUserAccessor current, PeriodService periods, EvaluationService eval, OrganizationService org)
    { _current = current; _periods = periods; _eval = eval; _org = org; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? week)
    {
        var user = (await _current.LoadAsync())!;
        var half = await _periods.CurrentHalfAsync();
        var ww = FiscalCalendar.AllWeeks(half).FirstOrDefault(w => w.Label.Equals(week, StringComparison.OrdinalIgnoreCase)) ?? _periods.ThisWeek(half);
        var rows = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.WeeklyAt(half, ww), includeSelf: false);
        var monthly = await _eval.ForVisiblePeopleAsync(user, EvaluationContext.Monthly(half), includeSelf: false);
        var people = await _org.VisiblePeopleAsync(user);
        var without = people.Where(p => p.Id != user.PersonId && rows.All(r => r.Kra.PersonId != p.Id)).ToList();
        return View(new TeamViewModel { User = user, Half = half, Week = ww, Rows = rows, RowsMonthly = monthly, WithoutKra = without });
    }
}
