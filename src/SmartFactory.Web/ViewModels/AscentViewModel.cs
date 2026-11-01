using SmartFactory.Core.Identity;
using SmartFactory.Core.Periods;
using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Web.ViewModels;

public class AscentViewModel
{
    public required CurrentUser User { get; init; }
    public required FiscalHalf Half { get; init; }
    public required WorkWeek ThisWeek { get; init; }
    public DateOnly Today { get; init; }
    public SlotEvaluation? Compliance { get; init; }
    public SlotEvaluation? Factory { get; init; }
    public KraEvaluation? ManagerEvaluation { get; init; }
    public KraEvaluation? Mine { get; init; }
    public KraEvaluation? MineWeekly { get; init; }
    public int PendingApprovals { get; init; }
}
