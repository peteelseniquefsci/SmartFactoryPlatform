using SmartFactory.Core.Definitions;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;

namespace SmartFactory.Performance.Entities;

public enum Aggregation { LAST = 0, SUM = 1, AVE = 2, MANUAL = 3 }
public enum WeeklyRule { RAMP = 0, PRORATE = 1, ZERO = 2, FLAT = 3, MANUAL = 4 }
public enum ResultSource { Typed = 0, Imported = 1, Register = 2 }

/// <summary>One KRA per person per half-year. History is kept as versions.</summary>
public class Kra : IVersionedDefinition
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int FiscalHalfId { get; set; }
    public FiscalHalf? FiscalHalf { get; set; }
    public DefinitionMode Mode { get; set; } = DefinitionMode.Setup;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Draft;
    /// <summary>Incremented on each lock; 0 while never locked.</summary>
    public int Version { get; set; }
    public int? ApprovedByPersonId { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ReturnComment { get; set; }
    public bool UnlockRequested { get; set; }
    public int? UnlockRequestedByPersonId { get; set; }
    public DateTime? UnlockRequestedAt { get; set; }
    public string? UnlockReason { get; set; }
    /// <summary>A leaver's KRA is frozen with its results.</summary>
    public bool Frozen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<KraSlot> Slots { get; set; } = new();
    public List<KraVersion> Versions { get; set; } = new();
}

/// <summary>One of the 20 slots. Active when the measure is non-empty. Weekly results stay attached to the slot across versions.</summary>
public class KraSlot
{
    public int Id { get; set; }
    public int KraId { get; set; }
    public Kra? Kra { get; set; }
    public int SlotNumber { get; set; }
    public int? TopGoalLineId { get; set; }
    public TopGoalLine? TopGoalLine { get; set; }
    public string? DepartmentalGoal { get; set; }
    public string? DepartmentalMeasure { get; set; }
    public string? SectionalGoal { get; set; }
    public string? SectionalMeasure { get; set; }
    public string Measure { get; set; } = "";
    public string? TargetText { get; set; }
    public decimal Weight { get; set; }
    public int? RatingBasisId { get; set; }
    public RatingBasis? RatingBasis { get; set; }
    public Aggregation Aggregation { get; set; } = Aggregation.LAST;
    public WeeklyRule WeeklyRule { get; set; } = WeeklyRule.RAMP;
    public decimal? StartValue { get; set; }
    public decimal? Plan1 { get; set; }
    public decimal? Plan2 { get; set; }
    public decimal? Plan3 { get; set; }
    public decimal? Plan4 { get; set; }
    public decimal? Plan5 { get; set; }
    public decimal? Plan6 { get; set; }
    public string? Notes { get; set; }
    /// <summary>A retired slot keeps its history but no longer counts. Retire a slot instead of changing a measure's meaning.</summary>
    public bool Retired { get; set; }
    public List<WeeklyResult> WeeklyResults { get; set; } = new();
    public List<MonthlyManualValue> MonthlyManualValues { get; set; } = new();
    public List<Remark> Remarks { get; set; } = new();

    public bool IsActive => !Retired && !string.IsNullOrWhiteSpace(Measure);

    public decimal?[] Plans => new[] { Plan1, Plan2, Plan3, Plan4, Plan5, Plan6 };

    public decimal? PlanFor(int monthIndex) => monthIndex switch
    {
        1 => Plan1, 2 => Plan2, 3 => Plan3, 4 => Plan4, 5 => Plan5, 6 => Plan6, _ => null
    };

    public void SetPlan(int monthIndex, decimal? value)
    {
        switch (monthIndex)
        {
            case 1: Plan1 = value; break; case 2: Plan2 = value; break; case 3: Plan3 = value; break;
            case 4: Plan4 = value; break; case 5: Plan5 = value; break; case 6: Plan6 = value; break;
        }
    }
}

/// <summary>The actual typed (later fed by a register) for a slot in a work week.</summary>
public class WeeklyResult
{
    public int Id { get; set; }
    public int KraSlotId { get; set; }
    public KraSlot? KraSlot { get; set; }
    public int WorkWeekId { get; set; }
    public WorkWeek? WorkWeek { get; set; }
    public decimal? Actual { get; set; }
    public decimal? ManualWeeklyTarget { get; set; }
    public ResultSource Source { get; set; } = ResultSource.Typed;
    /// <summary>Row id in the source register or import file; imports are idempotent on it.</summary>
    public string? SourceRowId { get; set; }
    public int? UpdatedByPersonId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Read only when aggregation = MANUAL (actual) or basis mode = M (rating).</summary>
public class MonthlyManualValue
{
    public int Id { get; set; }
    public int KraSlotId { get; set; }
    public KraSlot? KraSlot { get; set; }
    public int FiscalMonthId { get; set; }
    public FiscalMonth? FiscalMonth { get; set; }
    public decimal? ManualActual { get; set; }
    public int? ManualRating { get; set; }
    public int? UpdatedByPersonId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Remark
{
    public int Id { get; set; }
    public int KraSlotId { get; set; }
    public KraSlot? KraSlot { get; set; }
    public int? WorkWeekId { get; set; }
    public WorkWeek? WorkWeek { get; set; }
    public int? FiscalMonthId { get; set; }
    public FiscalMonth? FiscalMonth { get; set; }
    public int AuthorPersonId { get; set; }
    public Person? Author { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Snapshot of the slot definitions taken at each lock.</summary>
public class KraVersion
{
    public int Id { get; set; }
    public int KraId { get; set; }
    public Kra? Kra { get; set; }
    public int Version { get; set; }
    public string ApprovedByName { get; set; } = "";
    public DateTime ApprovedAt { get; set; }
    public string SlotsJson { get; set; } = "[]";
    public string? Note { get; set; }
}

/// <summary>Slot definitions a position's new incumbent inherits as a SETUP draft.</summary>
public class PositionTemplateSlot
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public Position? Position { get; set; }
    public int SlotNumber { get; set; }
    public int? TopGoalLineId { get; set; }
    public string? DepartmentalGoal { get; set; }
    public string? DepartmentalMeasure { get; set; }
    public string? SectionalGoal { get; set; }
    public string? SectionalMeasure { get; set; }
    public string Measure { get; set; } = "";
    public string? TargetText { get; set; }
    public decimal Weight { get; set; }
    public int? RatingBasisId { get; set; }
    public Aggregation Aggregation { get; set; }
    public WeeklyRule WeeklyRule { get; set; }
    public decimal? StartValue { get; set; }
    public decimal? Plan1 { get; set; }
    public decimal? Plan2 { get; set; }
    public decimal? Plan3 { get; set; }
    public decimal? Plan4 { get; set; }
    public decimal? Plan5 { get; set; }
    public decimal? Plan6 { get; set; }
    public string? Notes { get; set; }
}
