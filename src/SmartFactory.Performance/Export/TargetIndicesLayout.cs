using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Performance.Export;

/// <summary>One row of the company Target Indices form (same columns as the definitions workbook).</summary>
public record TargetIndicesRow(
    int SlotNumber,
    string FocusItem,
    string StrategicObjective,
    string TopGoalsMeasure,
    string? DepartmentalGoal,
    string? DepartmentalMeasure,
    string? SectionalGoal,
    string? SectionalMeasure,
    string Measure,
    string? Target,
    decimal Weight,
    int?[] MonthlyRatings,
    decimal? HalfRating,
    decimal? WeightedScore,
    string RatingReference);

public record TargetIndicesSheet(
    string PersonName,
    string EmployeeId,
    string PositionTitle,
    string SectionName,
    string DepartmentName,
    string HalfLabel,
    string[] MonthLabels,
    string Mode,
    int Version,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    List<TargetIndicesRow> Rows,
    decimal?[] MonthlyTotals,
    decimal? HalfPerformance);

public static class TargetIndicesLayout
{
    public static readonly string[] Headers =
    {
        "No.", "Focus item", "Strategic objective", "Top Goals measure",
        "Departmental goal", "Departmental measure", "Sectional goal", "Sectional measure",
        "Measure (individual)", "Target", "Weight %",
    };

    public static TargetIndicesSheet Build(KraEvaluation eval, string departmentName)
    {
        var kra = eval.Kra;
        var person = kra.Person!;
        var months = eval.Context.Half.Months.OrderBy(m => m.Index).ToList();
        var rows = eval.ActiveSlots.Select(s => new TargetIndicesRow(
            s.Slot.SlotNumber,
            s.Slot.TopGoalLine?.FocusItem ?? "",
            s.Slot.TopGoalLine?.StrategicObjective ?? "",
            s.Slot.TopGoalLine?.Measure ?? "",
            s.Slot.DepartmentalGoal, s.Slot.DepartmentalMeasure, s.Slot.SectionalGoal, s.Slot.SectionalMeasure,
            s.Slot.Measure, s.Slot.TargetText, s.Slot.Weight,
            months.Select(m => s.Month(m.Index)?.Rating).ToArray(),
            s.HalfRating, s.HalfWeightedScore,
            RatingReference(s))).ToList();

        return new TargetIndicesSheet(
            person.Name, person.EmployeeId, person.Position?.Title ?? "", person.Position?.Section?.Name ?? "", departmentName,
            eval.Context.Half.Label, months.Select(m => m.Label).ToArray(),
            kra.Mode.ToString().ToUpperInvariant(), kra.Version, kra.ApprovedByName, kra.ApprovedAt,
            rows, months.Select(m => eval.MonthTotals.First(t => t.Month.Index == m.Index).Total).ToArray(), eval.HalfPerformance);
    }

    public static string RatingReference(SlotEvaluation s)
    {
        var b = s.Slot.RatingBasis;
        if (b is null) return "";
        if (b.Mode == Entities.RatingMode.M) return $"{b.Name}: typed rating 1–5";
        string f(decimal? v) => v is null ? "–" : v.Value.ToString("0.####");
        return b.Mode switch
        {
            Entities.RatingMode.RH => $"{b.Name} (A/T): 5 > {f(b.T5)}; 4 ≥ {f(b.T4)}; 3 ≥ {f(b.T3)}; 2 ≥ {f(b.T2)}; else 1",
            Entities.RatingMode.RL => $"{b.Name} (A/T): 5 < {f(b.T5)}; 4 < {f(b.T4)}; 3 ≤ {f(b.T3)}; 2 ≤ {f(b.T2)}; else 1",
            _ => $"{b.Name} (A): 5 ≤ {f(b.T5)}; 4 ≤ {f(b.T4)}; 3 ≤ {f(b.T3)}; 2 ≤ {f(b.T2)}; else 1",
        };
    }
}
