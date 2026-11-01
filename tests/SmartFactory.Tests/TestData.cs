using SmartFactory.Core.Periods;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Tests;

/// <summary>Builders shared by the acceptance tests. Ids are assigned in memory so the evaluator can match weeks and months.</summary>
public static class TestData
{
    public static FiscalHalf Half2H2026()
    {
        var half = FiscalCalendar.Build(2026, 2);
        half.Id = 1;
        int mid = 1, wid = 1;
        foreach (var m in half.Months) { m.Id = mid++; m.FiscalHalfId = half.Id; foreach (var w in m.Weeks) { w.Id = wid++; w.FiscalMonthId = m.Id; } }
        return half;
    }

    public static RatingBasis Basis(string key, RatingMode mode, decimal? t5, decimal? t4, decimal? t3, decimal? t2)
        => new() { Id = key.GetHashCode() & 0x7fffffff, Key = key, Name = key, Mode = mode, T5 = t5, T4 = t4, T3 = t3, T2 = t2 };

    public static readonly RatingBasis NormalHi = Basis("normal_hi", RatingMode.RH, 1.10m, 1.06m, 1.00m, 0.90m);
    public static readonly RatingBasis IsoDt = Basis("iso_dt", RatingMode.RL, 0.80m, 0.90m, 1.00m, 1.10m);
    public static readonly RatingBasis ZeroNorm = Basis("zero_norm", RatingMode.AL, 0, -1, -1, -1);
    public static readonly RatingBasis ZeroDiff = Basis("zero_diff", RatingMode.AL, 0, -1, -1, 1);
    public static readonly RatingBasis ZeroDiff2 = Basis("zero_diff2", RatingMode.AL, 0, -1, -1, 2);
    public static readonly RatingBasis HundredDiff = Basis("hundred_diff", RatingMode.RH, 0.9999m, 9, 9, 0.98m);
    public static readonly RatingBasis HundredNorm = Basis("hundred_norm", RatingMode.RH, 0.9999m, 9, 9, 9);
    public static readonly RatingBasis Milestone = Basis("milestone", RatingMode.M, null, null, null, null);

    public static KraSlot Slot(int number, string measure, decimal weight, RatingBasis basis, Aggregation agg, WeeklyRule rule, decimal? start = null, params decimal?[] plans)
    {
        var s = new KraSlot { Id = number, SlotNumber = number, Measure = measure, Weight = weight, RatingBasis = basis, RatingBasisId = basis.Id, Aggregation = agg, WeeklyRule = rule, StartValue = start };
        for (int i = 0; i < plans.Length && i < 6; i++) s.SetPlan(i + 1, plans[i]);
        return s;
    }

    public static Kra Kra(params KraSlot[] slots)
    {
        var k = new Kra { Id = 1, PersonId = 1, FiscalHalfId = 1 };
        foreach (var s in slots) { s.KraId = 1; k.Slots.Add(s); }
        return k;
    }

    /// <summary>Enter weekly actuals for a month in week order (k = 1..).</summary>
    public static KraSlot Enter(this KraSlot slot, FiscalHalf half, int monthIndex, params decimal?[] actuals)
    {
        var weeks = half.Months.First(m => m.Index == monthIndex).Weeks.OrderBy(w => w.K).ToList();
        for (int i = 0; i < actuals.Length; i++)
        {
            if (actuals[i] is null) continue;
            slot.WeeklyResults.Add(new WeeklyResult { KraSlotId = slot.Id, WorkWeekId = weeks[i].Id, WorkWeek = weeks[i], Actual = actuals[i] });
        }
        return slot;
    }

    public static KraSlot Manual(this KraSlot slot, FiscalHalf half, int monthIndex, decimal? actual, int? rating = null)
    {
        var m = half.Months.First(x => x.Index == monthIndex);
        slot.MonthlyManualValues.Add(new MonthlyManualValue { KraSlotId = slot.Id, FiscalMonthId = m.Id, FiscalMonth = m, ManualActual = actual, ManualRating = rating });
        return slot;
    }

    public static WorkWeek Week(FiscalHalf half, string label) => FiscalCalendar.AllWeeks(half).First(w => w.Label == label);

    public static KraEvaluation Monthly(Kra kra, FiscalHalf half) => KraEvaluator.Evaluate(kra, EvaluationContext.Monthly(half));
    public static KraEvaluation WeeklyAt(Kra kra, FiscalHalf half, string week) => KraEvaluator.Evaluate(kra, EvaluationContext.WeeklyAt(half, Week(half, week)));
}
