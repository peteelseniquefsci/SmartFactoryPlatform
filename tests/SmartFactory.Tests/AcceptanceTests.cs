using SmartFactory.Core.Periods;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Rules;
using static SmartFactory.Tests.TestData;

namespace SmartFactory.Tests;

/// <summary>Section 11 of the hand-off specification: cases 1–18 on the rule engine. Cases 19–22 are in WorkflowTests.</summary>
public class AcceptanceTests
{
    private readonly FiscalHalf _half = Half2H2026();

    [Fact(DisplayName = "1. Calendar for Oct 2026 – Mar 2027")]
    public void Calendar()
    {
        var weeks = FiscalCalendar.AllWeeks(_half).ToList();
        Assert.Equal(26, weeks.Count);
        Assert.Equal("WW40", weeks.First().Label);
        Assert.Equal("WW12", weeks.Last().Label);
        Assert.Equal(new DateOnly(2026, 9, 28), weeks.First().StartDate);
        Assert.Equal(new DateOnly(2027, 3, 28), weeks.Last().EndDate);
        Assert.Equal(new[] { 5, 4, 5, 4, 4, 4 }, _half.Months.OrderBy(m => m.Index).Select(m => m.WeeksInMonth).ToArray());
        var ww44 = Week(_half, "WW44");
        Assert.Equal(new DateOnly(2026, 10, 26), ww44.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 1), ww44.EndDate);
        Assert.Equal(10, ww44.FiscalMonth!.Month);
        Assert.Equal(5, ww44.K);
        var ww49 = Week(_half, "WW49");
        Assert.Equal(new DateOnly(2026, 11, 30), ww49.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 6), ww49.EndDate);
        Assert.Equal(12, ww49.FiscalMonth!.Month);
        Assert.Equal(1, ww49.K);
        Assert.Null(FiscalCalendar.WeekOf(_half, new DateOnly(2027, 3, 30)));
    }

    [Fact(DisplayName = "2. Compliance slot, RAMP, start value 10, Oct plan 13.13")]
    public void WeeklyTargetRamp()
    {
        var slot = Slot(1, "Compliance Score", 25, NormalHi, Aggregation.LAST, WeeklyRule.RAMP, 10m, 13.13m, 20.31m, 28.13m, 32.81m, 36.88m, 41.56m);
        var eval = Monthly(Kra(slot), _half).Slots[0];
        var oct = eval.Weeks.Where(w => w.Week.FiscalMonth!.Index == 1).Select(w => decimal.Round(w.Target!.Value, 3)).ToArray();
        Assert.Equal(new[] { 10.626m, 11.252m, 11.878m, 12.504m, 13.13m }, oct);
        Assert.Equal(14.925m, decimal.Round(eval.Week("WW45")!.Target!.Value, 3));
    }

    [Fact(DisplayName = "3. Isolated downtime, PRORATE, plan 120")]
    public void WeeklyTargetProrate()
    {
        var slot = Slot(1, "Isolated downtime", 5, IsoDt, Aggregation.SUM, WeeklyRule.PRORATE, null, 120, 120, 120, 120, 120, 120);
        var eval = Monthly(Kra(slot), _half).Slots[0];
        Assert.Equal(24m, eval.Week("WW40")!.Target);   // October, 5 weeks
        Assert.Equal(30m, eval.Week("WW45")!.Target);   // November, 4 weeks
    }

    [Fact(DisplayName = "4. Compliance, LAST, weekly actuals in Oct")]
    public void MonthlyActualLastAndRating()
    {
        var slot = Slot(1, "Compliance Score", 25, NormalHi, Aggregation.LAST, WeeklyRule.RAMP, 10m, 13.13m, 20.31m)
            .Enter(_half, 1, 10.5m, 11.2m, 12.0m, 12.8m, 13.5m);
        var m = Monthly(Kra(slot), _half).Slots[0].Month(1)!;
        Assert.Equal(13.5m, m.Actual);
        Assert.Equal(3, m.Rating);
        Assert.True(m.Final);
    }

    [Fact(DisplayName = "5. Compliance, first two Nov weeks entered")]
    public void ProvisionalRating()
    {
        var slot = Slot(1, "Compliance Score", 25, NormalHi, Aggregation.LAST, WeeklyRule.RAMP, 10m, 13.13m, 20.31m)
            .Enter(_half, 2, 16.0m, 18.0m);
        var m = Monthly(Kra(slot), _half).Slots[0].Month(2)!;
        Assert.Equal(18m, m.Actual);
        Assert.Equal(1, m.Rating);
        Assert.True(m.Provisional);
        Assert.False(m.Final);
    }

    [Fact(DisplayName = "6. Isolated downtime, SUM, Oct weeks 10, 30, 0, 20, 15")]
    public void SumAndRatingLow()
    {
        var slot = Slot(1, "Isolated downtime", 5, IsoDt, Aggregation.SUM, WeeklyRule.PRORATE, null, 120).Enter(_half, 1, 10, 30, 0, 20, 15);
        var m = Monthly(Kra(slot), _half).Slots[0].Month(1)!;
        Assert.Equal(75m, m.Actual);
        Assert.Equal(5, m.Rating);
    }

    [Fact(DisplayName = "7. Plant-wide outages, zero_diff")]
    public void ZeroDiffBands()
    {
        var slot = Slot(1, "Plant-wide outages >30 min", 10, ZeroDiff, Aggregation.SUM, WeeklyRule.ZERO, null, 0).Enter(_half, 1, 0, 0, 1, 0, 0);
        Assert.Equal(2, Monthly(Kra(slot), _half).Slots[0].Month(1)!.Rating);
        Assert.Equal(1, RuleEngine.Rate(ZeroDiff.ToDefinition(), 2m, 0m));
        Assert.Equal(5, RuleEngine.Rate(ZeroDiff.ToDefinition(), 0m, 0m));
        Assert.Equal(2, RuleEngine.Rate(ZeroDiff2.ToDefinition(), 2m, 0m));
        Assert.Equal(1, RuleEngine.Rate(ZeroDiff2.ToDefinition(), 3m, 0m));
        Assert.Equal(1, RuleEngine.Rate(ZeroNorm.ToDefinition(), 1m, 0m));
    }

    [Fact(DisplayName = "8. % within SLA, AVE, FLAT 0.90")]
    public void AverageAndRating()
    {
        var slot = Slot(1, "% within SLA", 10, NormalHi, Aggregation.AVE, WeeklyRule.FLAT, null, 0.90m).Enter(_half, 1, 1.0m, 0.8m, 0.9m, 0.6m, 1.0m);
        var m = Monthly(Kra(slot), _half).Slots[0].Month(1)!;
        Assert.Equal(0.86m, m.Actual);
        Assert.Equal(2, m.Rating);
        Assert.Equal(0.90m, Monthly(Kra(slot), _half).Slots[0].Week("WW40")!.Target);
    }

    [Fact(DisplayName = "9. Competency, MANUAL, monthly manual 0.85 vs 0.80")]
    public void ManualAggregation()
    {
        var slot = Slot(1, "Competency", 5, NormalHi, Aggregation.MANUAL, WeeklyRule.MANUAL, null, 0.80m).Manual(_half, 1, 0.85m);
        var e = Monthly(Kra(slot), _half).Slots[0];
        Assert.Equal(4, e.Month(1)!.Rating);
        Assert.Null(e.Week("WW40")!.Target); // aggregation MANUAL: weekly target blank
    }

    [Fact(DisplayName = "10. Milestone slot, typed rating 4, weight 5%")]
    public void MilestoneWeightedScore()
    {
        var slot = Slot(1, "T.O. positions filled", 5, Milestone, Aggregation.MANUAL, WeeklyRule.MANUAL).Manual(_half, 1, null, 4);
        var m = Monthly(Kra(slot), _half).Slots[0].Month(1)!;
        Assert.Equal(4, m.Rating);
        Assert.Equal(0.20m, m.WeightedScore);
        Assert.Equal(MonthStatus.None, m.Status);
    }

    [Fact(DisplayName = "11. Regretted resignations zero_diff; competency 97.5%")]
    public void ResignationsAndCompetency()
    {
        Assert.Equal(2, RuleEngine.Rate(ZeroDiff.ToDefinition(), 1m, 0m));
        Assert.Equal(2, RuleEngine.Rate(NormalHi.ToDefinition(), 0.78m, 0.80m));
    }

    [Fact(DisplayName = "12. Department Oct total 3.40")]
    public void DepartmentTotal()
    {
        decimal[] weights = { 25, 25, 10, 5, 10, 2.5m, 1.25m, 1.25m, 5, 5, 5 };
        int[] ratings = { 5, 2, 5, 3, 5, 1, 5, 5, 2, 3, 2 };
        var (total, _) = RuleEngine.MonthlyTotal(weights.Zip(ratings, (w, r) => (w, (int?)r)));
        Assert.Equal(3.40m, decimal.Round(total!.Value, 2));
    }

    [Fact(DisplayName = "13. 2H rating with Oct complete and Nov partly entered")]
    public void HalfRatingIgnoresUnenteredMonths()
    {
        var a = Slot(1, "Compliance Score", 60, NormalHi, Aggregation.LAST, WeeklyRule.RAMP, 10m, 13.13m, 20.31m)
            .Enter(_half, 1, 13.5m).Enter(_half, 2, 22.0m);          // Oct 3, Nov 4 (22/20.31 = 1.083)
        var b = Slot(2, "Isolated downtime", 40, IsoDt, Aggregation.SUM, WeeklyRule.PRORATE, null, 120, 120)
            .Enter(_half, 1, 10, 30, 0, 20, 15);                       // Oct 5, Nov blank
        var e = Monthly(Kra(a, b), _half);
        Assert.Equal(3.5m, e.Slots[0].HalfRating);
        Assert.Equal(5m, e.Slots[1].HalfRating);
        Assert.Equal(0.6m * 3.5m + 0.4m * 5m, e.HalfPerformance);
        Assert.True(e.MonthTotals[1].Provisional);
    }

    [Fact(DisplayName = "14. Weekly view at WW42: safety incident in WW41")]
    public void SafetyUnmetImmediately()
    {
        var slot = Slot(1, "Disabling injury", 2.5m, ZeroNorm, Aggregation.SUM, WeeklyRule.ZERO, null, 0).Enter(_half, 1, 0, 1);
        var m = WeeklyAt(Kra(slot), _half, "WW42").Slots[0].Month(1)!;
        Assert.Equal(MonthStatus.Unmet, m.Status);
        Assert.False(m.Final);
    }

    [Fact(DisplayName = "15. Weekly view at WW42: Factory 4.5 vs Oct plan 3.92 (LAST)")]
    public void CumulativeMetEarly()
    {
        var slot = Slot(1, "Factory Score", 25, NormalHi, Aggregation.LAST, WeeklyRule.RAMP, 2m, 3.92m).Enter(_half, 1, 3.0m, 4.0m, 4.5m);
        var m = WeeklyAt(Kra(slot), _half, "WW42").Slots[0].Month(1)!;
        Assert.Equal(MonthStatus.Met, m.Status);
    }

    [Fact(DisplayName = "16. Weekly view at WW42: three slots still —")]
    public void OpenUntilMonthEnd()
    {
        var downtime = Slot(1, "Isolated downtime", 5, IsoDt, Aggregation.SUM, WeeklyRule.PRORATE, null, 120).Enter(_half, 1, 10, 20);
        var sla = Slot(2, "% within SLA", 10, NormalHi, Aggregation.AVE, WeeklyRule.FLAT, null, 0.90m).Enter(_half, 1, 0.85m, 0.90m, 0.875m);
        var overdue = Slot(3, "Overdue open issues", 5, ZeroNorm, Aggregation.LAST, WeeklyRule.ZERO, null, 0).Enter(_half, 1, 0, 0, 1);
        var e = WeeklyAt(Kra(downtime, sla, overdue), _half, "WW42");
        Assert.Equal(MonthStatus.Open, e.Slots[0].Month(1)!.Status);
        Assert.Equal(MonthStatus.Open, e.Slots[1].Month(1)!.Status);
        Assert.Equal(MonthStatus.Open, e.Slots[2].Month(1)!.Status);
    }

    [Fact(DisplayName = "17. Weekly view at WW45: October statuses final")]
    public void FinalAfterLastWeek()
    {
        var downtime = Slot(1, "Isolated downtime", 5, IsoDt, Aggregation.SUM, WeeklyRule.PRORATE, null, 120).Enter(_half, 1, 10, 20, 0, 0, 0);
        var sla = Slot(2, "% within SLA", 10, NormalHi, Aggregation.AVE, WeeklyRule.FLAT, null, 0.90m).Enter(_half, 1, 0.85m, 0.90m, 0.875m);
        var e = WeeklyAt(Kra(downtime, sla), _half, "WW45");
        Assert.True(e.Slots[0].Month(1)!.Final);
        Assert.Equal(MonthStatus.Met, e.Slots[0].Month(1)!.Status);
        Assert.Equal(MonthStatus.Unmet, e.Slots[1].Month(1)!.Status);
        Assert.False(WeeklyAt(Kra(downtime, sla), _half, "WW44").Slots[0].Month(1)!.Final);
    }

    [Fact(DisplayName = "18. hundred_diff basis")]
    public void HundredDiffBands()
    {
        var b = HundredDiff.ToDefinition();
        Assert.Equal(2, RuleEngine.Rate(b, 0.985m, 1.0m));
        Assert.Equal(1, RuleEngine.Rate(b, 0.975m, 1.0m));
        Assert.Equal(5, RuleEngine.Rate(b, 1.0m, 1.0m));
        Assert.Equal(5, RuleEngine.Rate(HundredNorm.ToDefinition(), 1.0m, 1.0m));
        Assert.Equal(1, RuleEngine.Rate(HundredNorm.ToDefinition(), 0.99m, 1.0m));
    }

    [Fact(DisplayName = "Rating blanks: RH/RL blank when A or T blank or T = 0; AL blank only when A blank")]
    public void RatingBlanks()
    {
        Assert.Null(RuleEngine.Rate(NormalHi.ToDefinition(), null, 1m));
        Assert.Null(RuleEngine.Rate(NormalHi.ToDefinition(), 1m, 0m));
        Assert.Null(RuleEngine.Rate(IsoDt.ToDefinition(), 1m, null));
        Assert.Null(RuleEngine.Rate(ZeroNorm.ToDefinition(), null, null));
        Assert.Equal(5, RuleEngine.Rate(ZeroNorm.ToDefinition(), 0m, null));
    }

    [Fact(DisplayName = "Week judgement: Met/Unmet by mode, blank for M or missing")]
    public void WeekJudgements()
    {
        Assert.Equal(Judgement.Met, RuleEngine.WeekJudgement(RatingMode.RH, 11m, 10m));
        Assert.Equal(Judgement.Unmet, RuleEngine.WeekJudgement(RatingMode.RH, 9m, 10m));
        Assert.Equal(Judgement.Met, RuleEngine.WeekJudgement(RatingMode.RL, 9m, 10m));
        Assert.Equal(Judgement.Met, RuleEngine.WeekJudgement(RatingMode.AL, 0m, 0m));
        Assert.Equal(Judgement.Unmet, RuleEngine.WeekJudgement(RatingMode.AL, 1m, 0m));
        Assert.Null(RuleEngine.WeekJudgement(RatingMode.M, 1m, 0m));
        Assert.Null(RuleEngine.WeekJudgement(RatingMode.RH, null, 0m));
    }
}
