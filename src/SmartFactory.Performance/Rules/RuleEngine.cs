using SmartFactory.Performance.Entities;

namespace SmartFactory.Performance.Rules;

/// <summary>A rating basis as the rules need it (section 6.4).</summary>
public record RatingBasisDefinition(string Key, RatingMode Mode, decimal? T5, decimal? T4, decimal? T3, decimal? T2);

public enum Judgement { Met, Unmet }

/// <summary>Month status: Met (green), Unmet (red), Open = — (amber), None = no status (mode M or blank).</summary>
public enum MonthStatus { None, Open, Met, Unmet }

/// <summary>
/// Section 5 of the hand-off specification, implemented exactly as written and verified against the Excel system.
/// Every function is pure; nothing here touches storage.
/// </summary>
public static class RuleEngine
{
    /// <summary>5.2 Weekly target for week (k, n) of month m with plan Pm and previous plan Pm-1 (start value for the first month).</summary>
    public static decimal? WeeklyTarget(WeeklyRule rule, Aggregation aggregation, decimal? planM, decimal? planPrev, int k, int n, decimal? manualWeeklyTarget)
    {
        if (aggregation == Aggregation.MANUAL) return null;
        if (rule == WeeklyRule.MANUAL) return manualWeeklyTarget;
        if (planM is null) return null;
        var pm = planM.Value;
        var prev = planPrev ?? 0m;
        return rule switch
        {
            WeeklyRule.RAMP => prev + (pm - prev) * k / n,
            WeeklyRule.PRORATE => pm / n,
            WeeklyRule.ZERO => 0m,
            WeeklyRule.FLAT => pm,
            _ => null,
        };
    }

    /// <summary>5.3 Monthly actual from the weekly actuals of the month (blank weeks ignored), or the manual actual for MANUAL.</summary>
    public static decimal? MonthlyActual(Aggregation aggregation, IEnumerable<(int k, decimal? actual)> weeks, decimal? manualActual)
    {
        if (aggregation == Aggregation.MANUAL) return manualActual;
        var values = weeks.Where(w => w.actual.HasValue).OrderBy(w => w.k).Select(w => (w.k, a: w.actual!.Value)).ToList();
        if (values.Count == 0) return null;
        return aggregation switch
        {
            Aggregation.LAST => values[^1].a,
            Aggregation.SUM => values.Sum(v => v.a),
            Aggregation.AVE => values.Sum(v => v.a) / values.Count,
            _ => null,
        };
    }

    /// <summary>5.4 Rating of actual A against target T under a basis. The same function rates a month and a week.</summary>
    public static int? Rate(RatingBasisDefinition? basis, decimal? actual, decimal? target, int? manualRating = null)
    {
        if (basis is null) return null;
        switch (basis.Mode)
        {
            case RatingMode.M:
                return manualRating is >= 1 and <= 5 ? manualRating : null;
            case RatingMode.AL:
                {
                    if (actual is null) return null;
                    var a = actual.Value;
                    if (basis.T5.HasValue && a <= basis.T5.Value) return 5;
                    if (basis.T4.HasValue && a <= basis.T4.Value) return 4;
                    if (basis.T3.HasValue && a <= basis.T3.Value) return 3;
                    if (basis.T2.HasValue && a <= basis.T2.Value) return 2;
                    return 1;
                }
            case RatingMode.RH:
                {
                    if (actual is null || target is null || target.Value == 0m) return null;
                    var r = actual.Value / target.Value;
                    if (basis.T5.HasValue && r > basis.T5.Value) return 5;
                    if (basis.T4.HasValue && r >= basis.T4.Value) return 4;
                    if (basis.T3.HasValue && r >= basis.T3.Value) return 3;
                    if (basis.T2.HasValue && r >= basis.T2.Value) return 2;
                    return 1;
                }
            case RatingMode.RL:
                {
                    if (actual is null || target is null || target.Value == 0m) return null;
                    var r = actual.Value / target.Value;
                    if (basis.T5.HasValue && r < basis.T5.Value) return 5;
                    if (basis.T4.HasValue && r < basis.T4.Value) return 4;
                    if (basis.T3.HasValue && r <= basis.T3.Value) return 3;
                    if (basis.T2.HasValue && r <= basis.T2.Value) return 2;
                    return 1;
                }
        }
        return null;
    }

    /// <summary>5.5 Weighted score = rating × weight (weight in percent, so 4 × 5% = 0.20).</summary>
    public static decimal? WeightedScore(int? rating, decimal weightPercent)
        => rating is null ? null : rating.Value * weightPercent / 100m;

    /// <summary>5.5 Monthly total = Σ weighted scores of active slots; complete only when every active slot has a rating.</summary>
    public static (decimal? total, bool complete) MonthlyTotal(IEnumerable<(decimal weight, int? rating)> activeSlots)
    {
        var list = activeSlots.ToList();
        if (list.Count == 0) return (null, false);
        var rated = list.Where(s => s.rating.HasValue).ToList();
        if (rated.Count == 0) return (null, false);
        var total = rated.Sum(s => s.rating!.Value * s.weight / 100m);
        return (total, rated.Count == list.Count);
    }

    /// <summary>5.5 2H rating per slot = average of the monthly ratings that exist.</summary>
    public static decimal? HalfRating(IEnumerable<int?> monthlyRatings)
    {
        var r = monthlyRatings.Where(x => x.HasValue).Select(x => (decimal)x!.Value).ToList();
        return r.Count == 0 ? null : r.Average();
    }

    /// <summary>5.5 2H performance = Σ (weight × 2H rating per slot). A half-entered month does not drag it down.</summary>
    public static decimal? HalfPerformance(IEnumerable<(decimal weight, decimal? halfRating)> activeSlots)
    {
        var rated = activeSlots.Where(s => s.halfRating.HasValue).ToList();
        if (rated.Count == 0) return null;
        return rated.Sum(s => s.halfRating!.Value * s.weight / 100m);
    }

    /// <summary>5.6 Week judgement: Met if A ≥ T for RH, Met if A ≤ T for RL and AL, else Unmet; blank if a value is missing or mode is M.</summary>
    public static Judgement? WeekJudgement(RatingMode? mode, decimal? actual, decimal? target)
    {
        if (mode is null || mode == RatingMode.M || actual is null || target is null) return null;
        return mode switch
        {
            RatingMode.RH => actual.Value >= target.Value ? Judgement.Met : Judgement.Unmet,
            _ => actual.Value <= target.Value ? Judgement.Met : Judgement.Unmet,
        };
    }

    /// <summary>5.7 Month status. Concludes early only when the outcome cannot change.</summary>
    public static MonthStatus MonthStatusOf(RatingMode? mode, Aggregation aggregation, decimal? actual, decimal? target, bool final)
    {
        if (mode is null || mode == RatingMode.M) return MonthStatus.None;
        if (actual is null || target is null) return MonthStatus.Open;
        var a = actual.Value; var t = target.Value;
        if (mode == RatingMode.RH)
        {
            if (a >= t && (final || aggregation != Aggregation.AVE)) return MonthStatus.Met;
            return final ? MonthStatus.Unmet : MonthStatus.Open;
        }
        // RL and AL
        if (a > t) return (final || aggregation == Aggregation.SUM) ? MonthStatus.Unmet : MonthStatus.Open;
        return final ? MonthStatus.Met : MonthStatus.Open;
    }

    /// <summary>5.8 Weight check: active weights must total exactly 100%.</summary>
    public static (decimal total, bool ok) WeightCheck(IEnumerable<decimal> activeWeights)
    {
        var total = activeWeights.Sum();
        return (total, total == 100m);
    }
}
