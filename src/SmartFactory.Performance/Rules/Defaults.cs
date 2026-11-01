using SmartFactory.Performance.Entities;

namespace SmartFactory.Performance.Rules;

/// <summary>6.5 Default aggregation and weekly rule by measure type.</summary>
public static class MeasureDefaults
{
    public enum MeasureType { Score, Closure, Savings, WithinSla, DowntimeMinutes, DefectCount, ZeroTargetCount, MonthEndSnapshotCount, Competency, Milestone }

    public static (Aggregation aggregation, WeeklyRule rule) For(MeasureType type) => type switch
    {
        MeasureType.Score or MeasureType.Closure or MeasureType.Savings => (Aggregation.LAST, WeeklyRule.RAMP),
        MeasureType.WithinSla => (Aggregation.AVE, WeeklyRule.FLAT),
        MeasureType.DowntimeMinutes or MeasureType.DefectCount => (Aggregation.SUM, WeeklyRule.PRORATE),
        MeasureType.ZeroTargetCount => (Aggregation.SUM, WeeklyRule.ZERO),
        MeasureType.MonthEndSnapshotCount => (Aggregation.LAST, WeeklyRule.ZERO),
        _ => (Aggregation.MANUAL, WeeklyRule.MANUAL),
    };
}
