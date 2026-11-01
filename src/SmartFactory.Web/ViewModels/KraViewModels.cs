using SmartFactory.Core.Audit;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Performance.Entities;
using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Web.ViewModels;

public class KraPageViewModel
{
    public required CurrentUser User { get; init; }
    public required Person Owner { get; init; }
    public required FiscalHalf Half { get; init; }
    public required Kra Kra { get; init; }
    public required KraEvaluation Evaluation { get; init; }
    public required List<TopGoalLine> TopGoalLines { get; init; }
    public required List<RatingBasis> RatingBases { get; init; }
    public required List<AuditEntry> Trail { get; init; }
    public bool IsOwner => User.PersonId == Owner.Id;
    public bool CanEdit => IsOwner && Kra.Mode == Core.Definitions.DefinitionMode.Setup && !Kra.Frozen;
    public bool IsSuperior { get; init; }
}

public class SaveSlotsForm
{
    public int KraId { get; set; }
    public List<SlotInput> Slots { get; set; } = new();
}

public class WeekPageViewModel
{
    public required CurrentUser User { get; init; }
    public required Person Owner { get; init; }
    public required FiscalHalf Half { get; init; }
    public required WorkWeek Week { get; init; }
    public required WorkWeek ThisWeek { get; init; }
    public required Kra Kra { get; init; }
    public required KraEvaluation Evaluation { get; init; }
    public bool IsOwner => User.PersonId == Owner.Id;
    public bool CanEnter => IsOwner && !Kra.Frozen;
    public bool CanRemark { get; init; }
    public WorkWeek? Previous { get; init; }
    public WorkWeek? Next { get; init; }
}

public class WeekEntryForm
{
    public int KraId { get; set; }
    public string Week { get; set; } = "";
    public List<WeekEntryRow> Rows { get; set; } = new();
}
public class WeekEntryRow { public int SlotNumber { get; set; } public decimal? Actual { get; set; } public decimal? ManualTarget { get; set; } public string? Remark { get; set; } }

public class MonthEntryForm
{
    public int KraId { get; set; }
    public int MonthIndex { get; set; }
    public string? ReturnWeek { get; set; }
    public List<MonthEntryRow> Rows { get; set; } = new();
}
public class MonthEntryRow { public int SlotNumber { get; set; } public decimal? ManualActual { get; set; } public int? ManualRating { get; set; } public string? Remark { get; set; } }
