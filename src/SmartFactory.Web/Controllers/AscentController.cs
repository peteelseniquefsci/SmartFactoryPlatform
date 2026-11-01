using Microsoft.AspNetCore.Mvc;
using SmartFactory.Core.Definitions;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Web.Infrastructure;
using SmartFactory.Web.ViewModels;

namespace SmartFactory.Web.Controllers;

public class AscentController : Controller
{
    private readonly WebCurrentUserAccessor _current;
    private readonly PeriodService _periods;
    private readonly EvaluationService _eval;
    private readonly KraService _kras;

    public AscentController(WebCurrentUserAccessor current, PeriodService periods, EvaluationService eval, KraService kras)
    { _current = current; _periods = periods; _eval = eval; _kras = kras; }

    public async Task<IActionResult> Index()
    {
        var user = (await _current.LoadAsync())!;
        var half = await _periods.CurrentHalfAsync();
        var week = _periods.ThisWeek(half);
        var weekly = EvaluationContext.WeeklyAt(half, week);
        var (compliance, factory, managerEval) = await _eval.CompanyScoresAsync(weekly);
        var kra = await _kras.GetOrCreateAsync(user.Person, half);
        var mine = _eval.Evaluate(kra, EvaluationContext.Monthly(half));
        var mineWeekly = _eval.Evaluate(kra, weekly);
        int pending = 0;
        if (user.IsHeadOrManager)
        {
            var list = await _kras.PendingForAsync(user, half);
            pending = list.Count(k => k.Status == ApprovalStatus.Submitted || k.UnlockRequested || (k.Version == 0 && _periods.Today > half.Months[0].Weeks[0].EndDate));
        }
        return View(new AscentViewModel { User = user, Half = half, ThisWeek = week, Today = _periods.Today, Compliance = compliance, Factory = factory, ManagerEvaluation = managerEval, Mine = mine, MineWeekly = mineWeekly, PendingApprovals = pending });
    }
}
