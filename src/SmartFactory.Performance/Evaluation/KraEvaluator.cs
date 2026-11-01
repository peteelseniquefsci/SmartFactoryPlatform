using SmartFactory.Core.Periods;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Rules;

namespace SmartFactory.Performance.Evaluation;

public enum ViewKind { Weekly, Monthly }

/// <summary>What the evaluator is looking from: the weekly view at a given work week, or the monthly view.</summary>
public record EvaluationContext(FiscalHalf Half, ViewKind Kind, WorkWeek? ViewWeek)
{
    public static EvaluationContext Monthly(FiscalHalf half) => new(half, ViewKind.Monthly, null);
    public static EvaluationContext WeeklyAt(FiscalHalf half, WorkWeek week) => new(half, ViewKind.Weekly, week);
}

public class SlotWeekEvaluation
{
    public required WorkWeek Week { get; init; }
    public decimal? Target { get; init; }
    public decimal? Actual { get; init; }
    public Judgement? Judgement { get; init; }
    public int? Rating { get; init; }
    public ResultSource? Source { get; init; }
}

public class SlotMonthEvaluation
{
    public required FiscalMonth Month { get; init; }
    public decimal? Plan { get; init; }
    public decimal? Actual { get; init; }
    public int? Rating { get; init; }
    public decimal? WeightedScore { get; init; }
    public MonthStatus Status { get; init; }
    public bool Final { get; init; }
    public bool Provisional => Rating.HasValue && !Final;
    public int? ManualRating { get; init; }
    public decimal? ManualActual { get; init; }
}

public class SlotEvaluation
{
    public required KraSlot Slot { get; init; }
    public RatingBasisDefinition? Basis { get; init; }
    public List<SlotWeekEvaluation> Weeks { get; init; } = new();
    public List<SlotMonthEvaluation> Months { get; init; } = new();
    public decimal? HalfRating { get; init; }
    public decimal? HalfWeightedScore => HalfRating is null ? null : HalfRating * Slot.Weight / 100m;
    public SlotWeekEvaluation? Week(string label) => Weeks.FirstOrDefault(w => w.Week.Label == label);
    public SlotMonthEvaluation? Month(int index) => Months.FirstOrDefault(m => m.Month.Index == index);
}

public class MonthTotalEvaluation
{
    public required FiscalMonth Month { get; init; }
    public decimal? Total { get; init; }
    public bool Complete { get; init; }
    public bool Final { get; init; }
    public bool Provisional => Total.HasValue && (!Complete || !Final);
}

public class KraEvaluation
{
    public required Kra Kra { get; init; }
    public required EvaluationContext Context { get; init; }
    public List<SlotEvaluation> Slots { get; init; } = new();
    public IEnumerable<SlotEvaluation> ActiveSlots => Slots.Where(s => s.Slot.IsActive);
    public List<MonthTotalEvaluation> MonthTotals { get; init; } = new();
    public decimal? HalfPerformance { get; init; }
    public decimal WeightTotal { get; init; }
    public bool WeightsOk => WeightTotal == 100m;
}

/// <summary>Computes every derived value of a KRA (never stored as truth) from the rules in section 5.</summary>
public static class KraEvaluator
{
    public static KraEvaluation Evaluate(Kra kra, EvaluationContext ctx, Func<int, RatingBasisDefinition?>? basisLookup = null)
    {
        var weeks = FiscalCalendar.AllWeeks(ctx.Half).ToList();
        var months = ctx.Half.Months.OrderBy(m => m.Index).ToList();
        var slots = new List<SlotEvaluation>();

        foreach (var slot in kra.Slots.OrderBy(s => s.SlotNumber))
        {
            var basis = slot.RatingBasis?.ToDefinition() ?? (slot.RatingBasisId is int bid ? basisLookup?.Invoke(bid) : null);
            var mode = basis?.Mode;
            var weekEvals = new List<SlotWeekEvaluation>();
            var monthEvals = new List<SlotMonthEvaluation>();

            foreach (var week in weeks)
            {
                var month = week.FiscalMonth ?? months.First(m => m.Id == week.FiscalMonthId);
                var result = slot.WeeklyResults.FirstOrDefault(r => r.WorkWeekId == week.Id || (r.WorkWeek is not null && r.WorkWeek.Label == week.Label && r.WorkWeek.StartDate == week.StartDate));
                var planM = slot.PlanFor(month.Index);
                var planPrev = month.Index == 1 ? slot.StartValue : slot.PlanFor(month.Index - 1);
                var target = RuleEngine.WeeklyTarget(slot.WeeklyRule, slot.Aggregation, planM, planPrev, week.K, week.N, result?.ManualWeeklyTarget);
                var actual = slot.Aggregation == Aggregation.MANUAL ? null : result?.Actual;
                weekEvals.Add(new SlotWeekEvaluation
                {
                    Week = week,
                    Target = target,
                    Actual = actual,
                    Judgement = RuleEngine.WeekJudgement(mode, actual, target),
                    Rating = mode == RatingMode.M ? null : RuleEngine.Rate(basis, actual, target),
                    Source = result?.Source,
                });
            }

            foreach (var month in months)
            {
                var monthWeeks = weeks.Where(w => w.FiscalMonthId == month.Id || (w.FiscalMonth?.Index == month.Index)).OrderBy(w => w.K).ToList();
                var visible = ctx.Kind == ViewKind.Weekly && ctx.ViewWeek is not null
                    ? monthWeeks.Where(w => w.Sequence <= ctx.ViewWeek.Sequence).ToList()
                    : monthWeeks;
                var manual = slot.MonthlyManualValues.FirstOrDefault(v => v.FiscalMonthId == month.Id || (v.FiscalMonth is not null && v.FiscalMonth.Index == month.Index));
                var weekly = visible.Select(w => (w.K, weekEvals.First(e => e.Week.Sequence == w.Sequence).Actual));
                var actual = RuleEngine.MonthlyActual(slot.Aggregation, weekly, manual?.ManualActual);
                var plan = slot.PlanFor(month.Index);
                var rating = RuleEngine.Rate(basis, actual, plan, manual?.ManualRating);

                bool final;
                var lastWeek = monthWeeks.LastOrDefault();
                if (ctx.Kind == ViewKind.Weekly && ctx.ViewWeek is not null)
                    final = lastWeek is not null && lastWeek.Sequence < ctx.ViewWeek.Sequence;
                else
                    final = (slot.Aggregation == Aggregation.MANUAL && manual?.ManualActual is not null)
                            || (mode == RatingMode.M && manual?.ManualRating is not null)
                            || (lastWeek is not null && slot.Aggregation != Aggregation.MANUAL && weekEvals.First(e => e.Week.Sequence == lastWeek.Sequence).Actual.HasValue);

                monthEvals.Add(new SlotMonthEvaluation
                {
                    Month = month,
                    Plan = plan,
                    Actual = actual,
                    Rating = rating,
                    WeightedScore = RuleEngine.WeightedScore(rating, slot.Weight),
                    Status = RuleEngine.MonthStatusOf(mode, slot.Aggregation, actual, plan, final),
                    Final = final,
                    ManualActual = manual?.ManualActual,
                    ManualRating = manual?.ManualRating,
                });
            }

            slots.Add(new SlotEvaluation
            {
                Slot = slot,
                Basis = basis,
                Weeks = weekEvals,
                Months = monthEvals,
                HalfRating = RuleEngine.HalfRating(monthEvals.Select(m => m.Rating)),
            });
        }

        var active = slots.Where(s => s.Slot.IsActive).ToList();
        var totals = months.Select(m =>
        {
            var perSlot = active.Select(s => (s.Slot.Weight, s.Month(m.Index)?.Rating)).ToList();
            var (total, complete) = RuleEngine.MonthlyTotal(perSlot);
            var final = active.Count > 0 && active.All(s => s.Month(m.Index)?.Final == true);
            return new MonthTotalEvaluation { Month = m, Total = total, Complete = complete, Final = final };
        }).ToList();

        return new KraEvaluation
        {
            Kra = kra,
            Context = ctx,
            Slots = slots,
            MonthTotals = totals,
            HalfPerformance = RuleEngine.HalfPerformance(active.Select(s => (s.Slot.Weight, s.HalfRating))),
            WeightTotal = active.Sum(s => s.Slot.Weight),
        };
    }
}
