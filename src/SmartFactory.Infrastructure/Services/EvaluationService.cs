using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Evaluation;
using SmartFactory.Performance.Rules;

namespace SmartFactory.Infrastructure.Services;

/// <summary>Evaluated KRAs for the dashboards: one person, a team, or every KRA beneath a manager.</summary>
public class EvaluationService
{
    private readonly SmartFactoryDbContext _db;
    private readonly KraService _kras;
    private readonly OrganizationService _org;

    public EvaluationService(SmartFactoryDbContext db, KraService kras, OrganizationService org) { _db = db; _kras = kras; _org = org; }

    public async Task<KraEvaluation?> ForPersonAsync(int personId, EvaluationContext ctx)
    {
        var kra = await _kras.FindAsync(personId, ctx.Half.Id);
        if (kra is null) return null;
        Attach(kra, ctx.Half);
        return KraEvaluator.Evaluate(kra, ctx);
    }

    public KraEvaluation Evaluate(Kra kra, EvaluationContext ctx) { Attach(kra, ctx.Half); return KraEvaluator.Evaluate(kra, ctx); }

    /// <summary>Evaluated KRAs of the people a user may see (excluding frozen leavers unless asked).</summary>
    public async Task<List<KraEvaluation>> ForVisiblePeopleAsync(CurrentUser user, EvaluationContext ctx, bool includeSelf = true)
    {
        var people = await _org.VisiblePeopleAsync(user);
        var ids = people.Where(p => includeSelf || p.Id != user.PersonId).Select(p => p.Id).ToList();
        var kras = await _kras.FullKras().Where(k => k.FiscalHalfId == ctx.Half.Id && ids.Contains(k.PersonId)).ToListAsync();
        var byPerson = kras.ToDictionary(k => k.PersonId);
        var list = new List<KraEvaluation>();
        foreach (var p in people.Where(p => ids.Contains(p.Id)))
        {
            if (!byPerson.TryGetValue(p.Id, out var kra)) continue;
            kra.Person = p;
            Attach(kra, ctx.Half);
            list.Add(KraEvaluator.Evaluate(kra, ctx));
        }
        return list;
    }

    public async Task<List<KraEvaluation>> ForPeopleAsync(IEnumerable<int> personIds, EvaluationContext ctx)
    {
        var ids = personIds.ToList();
        var kras = await _kras.FullKras().Where(k => k.FiscalHalfId == ctx.Half.Id && ids.Contains(k.PersonId)).ToListAsync();
        return kras.Select(k => { Attach(k, ctx.Half); return KraEvaluator.Evaluate(k, ctx); }).ToList();
    }

    /// <summary>The two company Scores: the Compliance and Factory slots on the department manager's KRA.</summary>
    public async Task<(SlotEvaluation? compliance, SlotEvaluation? factory, KraEvaluation? managerEval)> CompanyScoresAsync(EvaluationContext ctx)
    {
        var managerPositions = await _db.Positions.Where(p => p.IsManager && p.ReportsToPositionId == null).Select(p => p.Id).ToListAsync();
        var manager = await _db.People.Where(p => managerPositions.Contains(p.PositionId) && p.Active).OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (manager is null) return (null, null, null);
        var eval = await ForPersonAsync(manager.Id, ctx);
        if (eval is null) return (null, null, null);
        return (FindScore(eval, "Compliance"), FindScore(eval, "Factory"), eval);
    }

    public static SlotEvaluation? FindScore(KraEvaluation eval, string word)
        => eval.ActiveSlots.FirstOrDefault(s => s.Slot.Measure.Contains(word + " Score", StringComparison.OrdinalIgnoreCase))
           ?? eval.ActiveSlots.FirstOrDefault(s => s.Slot.Measure.Contains(word, StringComparison.OrdinalIgnoreCase) && s.Slot.Measure.Contains("Score", StringComparison.OrdinalIgnoreCase));

    /// <summary>The slot shown on the Team view row: the heaviest active slot.</summary>
    public static SlotEvaluation? MainSlot(KraEvaluation eval) => eval.ActiveSlots.OrderByDescending(s => s.Slot.Weight).ThenBy(s => s.Slot.SlotNumber).FirstOrDefault();

    /// <summary>Make the loaded calendar the one the evaluator uses so week and month ids match.</summary>
    private static void Attach(Kra kra, FiscalHalf half)
    {
        var weeks = FiscalCalendar.AllWeeks(half).ToDictionary(w => w.Id);
        var months = half.Months.ToDictionary(m => m.Id);
        foreach (var s in kra.Slots)
        {
            foreach (var r in s.WeeklyResults) if (weeks.TryGetValue(r.WorkWeekId, out var w)) r.WorkWeek = w;
            foreach (var v in s.MonthlyManualValues) if (months.TryGetValue(v.FiscalMonthId, out var m)) v.FiscalMonth = m;
        }
    }

    public static string StatusKey(MonthStatus s) => s switch { MonthStatus.Met => "met", MonthStatus.Unmet => "unmet", MonthStatus.Open => "open", _ => "none" };
    public static string JudgementKey(Judgement? j) => j switch { Judgement.Met => "met", Judgement.Unmet => "unmet", _ => "none" };
}
