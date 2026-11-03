using Microsoft.AspNetCore.Mvc;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

[Route("week")]
public class WeekController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly KraService _kras;
    private readonly EvaluationService _eval;
    private readonly OrganizationService _org;
    private readonly WeeklyEntryService _entry;

    public WeekController(WebCurrentUserAccessor current, PeriodService periods, KraService kras, EvaluationService eval, OrganizationService org, WeeklyEntryService entry)
    { _current = current; _periods = periods; _kras = kras; _eval = eval; _org = org; _entry = entry; }

    [HttpGet("")]
    public Task<IActionResult> Index(string? week, int? person) => Show(person, week);

    private async Task<IActionResult> Show(int? personId, string? weekLabel)
    {
        var user = (await _current.LoadAsync())!;
        var owner = personId is null ? user.Person : await _org.LoadPersonAsync(personId.Value);
        if (owner is null) return NotFound();
        if (!await _org.CanViewAsync(user, owner)) return Forbid();
        var half = await _periods.CurrentHalfAsync();
        var weeks = FiscalCalendar.AllWeeks(half).ToList();
        var thisWeek = _periods.ThisWeek(half);
        var week = weeks.FirstOrDefault(w => w.Label.Equals(weekLabel, StringComparison.OrdinalIgnoreCase)) ?? thisWeek;
        var kra = owner.Id == user.PersonId ? await _kras.GetOrCreateAsync(owner, half) : await _kras.FindAsync(owner.Id, half.Id);
        if (kra is null) { ViewBag.Owner = owner; ViewBag.Half = half; return View("~/Views/Kra/NoKra.cshtml"); }
        kra.Person = owner;
        var idx = weeks.IndexOf(week);
        var vm = new WeekPageViewModel
        {
            User = user, Owner = owner, Half = half, Week = week, ThisWeek = thisWeek, Kra = kra,
            Evaluation = _eval.Evaluate(kra, EvaluationContext.WeeklyAt(half, week)),
            CanRemark = owner.Id == user.PersonId || user.IsAdministrator || await _org.IsSuperiorOfAsync(user, owner),
            Previous = idx > 0 ? weeks[idx - 1] : null,
            Next = idx < weeks.Count - 1 ? weeks[idx + 1] : null,
        };
        return View("Index", vm);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WeekEntryForm form)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(form.KraId);
        if (kra is null) return NotFound();
        var half = await _periods.CurrentHalfAsync();
        var week = FiscalCalendar.AllWeeks(half).FirstOrDefault(w => w.Label == form.Week);
        if (week is null) return NotFound();
        await _entry.SaveWeekAsync(kra, week, form.Rows.Select(r => new WeekEntry(r.SlotNumber, r.Actual, r.ManualTarget, r.Remark)), user);
        TempData["Ok"] = $"{week.Label} saved. Judgements and the month status are updated.";
        return Redirect(Url.Content($"~/week?week={week.Label}{(kra.PersonId == user.PersonId ? "" : $"&person={kra.PersonId}")}"));
    }

    [HttpPost("save-month")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMonth(MonthEntryForm form)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(form.KraId);
        if (kra is null) return NotFound();
        var half = await _periods.CurrentHalfAsync();
        var month = half.Months.FirstOrDefault(m => m.Index == form.MonthIndex);
        if (month is null) return NotFound();
        await _entry.SaveMonthAsync(kra, month, form.Rows.Select(r => new MonthEntry(r.SlotNumber, r.ManualActual, r.ManualRating, r.Remark)), user);
        TempData["Ok"] = $"Manual values for {month.Label} saved.";
        return Redirect(Url.Content($"~/week?week={form.ReturnWeek}{(kra.PersonId == user.PersonId ? "" : $"&person={kra.PersonId}")}"));
    }

    [HttpPost("remark")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remark(int kraId, int slotNumber, string week, string text)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId);
        if (kra is null) return NotFound();
        var owner = await _org.LoadPersonAsync(kra.PersonId);
        if (owner is null) return NotFound();
        if (kra.PersonId != user.PersonId && !user.IsAdministrator && !await _org.IsSuperiorOfAsync(user, owner)) return Forbid();
        var half = await _periods.CurrentHalfAsync();
        var ww = FiscalCalendar.AllWeeks(half).FirstOrDefault(w => w.Label == week);
        var slot = kra.Slots.FirstOrDefault(s => s.SlotNumber == slotNumber);
        if (ww is null || slot is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(text)) await _entry.AddRemarkAsync(slot, ww, null, text, user);
        TempData["Ok"] = "Remark added.";
        return Redirect(Url.Content($"~/week?week={week}{(kra.PersonId == user.PersonId ? "" : $"&person={kra.PersonId}")}"));
    }
}
