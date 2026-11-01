using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Definitions;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;
using SmartFactory.Web.Infrastructure;

namespace SmartFactory.Web.Controllers;

[Authorize(Policy = Policies.HeadOrManager)]
[Route("approvals")]
public class ApprovalsController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly KraService _kras;

    public ApprovalsController(WebCurrentUserAccessor current, PeriodService periods, KraService kras) { _current = current; _periods = periods; _kras = kras; }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? trail)
    {
        var user = (await _current.LoadAsync())!;
        var half = await _periods.CurrentHalfAsync();
        var all = await _kras.PendingForAsync(user, half);
        ViewBag.Half = half; ViewBag.Today = _periods.Today; ViewBag.FirstWeekEnd = half.Months[0].Weeks[0].EndDate;
        ViewBag.Waiting = all.Where(k => k.Status == ApprovalStatus.Submitted).ToList();
        ViewBag.Unlocks = all.Where(k => k.UnlockRequested).ToList();
        ViewBag.NeverLocked = all.Where(k => k.Version == 0 && k.Status != ApprovalStatus.Submitted).ToList();
        ViewBag.Locked = all.Where(k => k.Mode == DefinitionMode.Locked && !k.UnlockRequested).ToList();
        ViewBag.Trail = new List<AuditEntry>();
        Kra? trailKra = trail is null ? null : all.FirstOrDefault(k => k.Id == trail);
        if (trailKra is not null) { ViewBag.TrailKra = trailKra; ViewBag.Trail = await _kras.ChangeTrailAsync(trailKra, 200); }
        return View();
    }

    [HttpPost("approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int kraId, string? note)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId); if (kra is null) return NotFound();
        await _kras.ApproveAndLockAsync(kra, user, note);
        TempData["Ok"] = $"{kra.Person?.Name}'s KRA is approved and LOCKED as version {kra.Version}.";
        return Redirect("/approvals");
    }

    [HttpPost("return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int kraId, string comment)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId); if (kra is null) return NotFound();
        await _kras.ReturnAsync(kra, user, comment);
        TempData["Ok"] = $"Returned to {kra.Person?.Name} with your comments.";
        return Redirect("/approvals");
    }

    [HttpPost("unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int kraId, string? reason)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId); if (kra is null) return NotFound();
        await _kras.UnlockAsync(kra, user, reason);
        TempData["Ok"] = $"{kra.Person?.Name}'s KRA is back in SETUP. The next lock creates version {kra.Version + 1}.";
        return Redirect("/approvals");
    }

    [HttpPost("decline-unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineUnlock(int kraId)
    {
        var user = (await _current.LoadAsync())!;
        var kra = await _kras.LoadAsync(kraId); if (kra is null) return NotFound();
        kra.UnlockRequested = false;
        await _kras.ReturnAsync(kra, user, "Unlock request declined; the KRA stays LOCKED.");
        kra.Status = ApprovalStatus.Approved;
        await _kras.LockAsSystemNoVersionAsync(kra);
        TempData["Ok"] = "Unlock request declined.";
        return Redirect("/approvals");
    }
}
