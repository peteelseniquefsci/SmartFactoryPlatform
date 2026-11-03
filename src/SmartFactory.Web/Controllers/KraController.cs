using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Organization;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

[Route("kra")]
public class KraController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly KraService _kras;
    private readonly EvaluationService _eval;
    private readonly OrganizationService _org;
    private readonly SmartFactoryDbContext _db;

    public KraController(WebCurrentUserAccessor current, PeriodService periods, KraService kras, EvaluationService eval, OrganizationService org, SmartFactoryDbContext db)
    { _current = current; _periods = periods; _kras = kras; _eval = eval; _org = org; _db = db; }

    [HttpGet("")]
    public Task<IActionResult> Index(int? half) => Show(null, half);

    [HttpGet("person/{id:int}")]
    public Task<IActionResult> Person(int id, int? half) => Show(id, half);

    private async Task<IActionResult> Show(int? personId, int? halfId)
    {
        var user = (await _current.LoadAsync())!;
        var owner = personId is null ? user.Person : await _org.LoadPersonAsync(personId.Value);
        if (owner is null) return NotFound();
        if (!await _org.CanViewAsync(user, owner)) return Forbid();
        var half = halfId is null ? await _periods.CurrentHalfAsync() : await _periods.HalfAsync(halfId.Value);
        if (half is null) return NotFound();
        var kra = owner.Id == user.PersonId ? await _kras.GetOrCreateAsync(owner, half) : await _kras.FindAsync(owner.Id, half.Id);
        if (kra is null) { ViewBag.Owner = owner; ViewBag.Half = half; return View("NoKra"); }
        kra.Person = owner;
        var vm = new KraPageViewModel
        {
            User = user, Owner = owner, Half = half, Kra = kra,
            Evaluation = _eval.Evaluate(kra, EvaluationContext.Monthly(half)),
            TopGoalLines = await _db.TopGoalLines.Where(t => t.Active).OrderBy(t => t.SortOrder).ToListAsync(),
            RatingBases = await _db.RatingBases.Where(b => b.Active).OrderBy(b => b.Mode).ThenBy(b => b.Key).ToListAsync(),
            Trail = await _kras.ChangeTrailAsync(kra, 100),
            IsSuperior = owner.Id != user.PersonId && (user.IsAdministrator || await _org.IsSuperiorOfAsync(user, owner)),
        };
        return View("Index", vm);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveSlotsForm form)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(form.KraId);
        if (kra is null) return NotFound();
        await _kras.SaveSlotsAsync(kra, form.Slots, user);
        TempData["Ok"] = "KRA saved.";
        return Redirect(kra.PersonId == user.PersonId ? "/kra" : $"/kra/person/{kra.PersonId}");
    }

    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int kraId)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId);
        if (kra is null) return NotFound();
        await _kras.SubmitAsync(kra, user);
        TempData["Ok"] = "Submitted for approval. Your superior reviews the slots against the Top Goal lines and your position.";
        return Redirect(Url.Content("~/kra"));
    }

    [HttpPost("request-unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestUnlock(int kraId, string reason)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId);
        if (kra is null) return NotFound();
        await _kras.RequestUnlockAsync(kra, user, reason);
        TempData["Ok"] = "Unlock requested. Your superior decides in Approvals.";
        return Redirect(Url.Content("~/kra"));
    }
}
