using SmartFactory.Core.Identity;
using SmartFactory.Core.Organization;
using SmartFactory.Core.Periods;
using SmartFactory.Performance.Evaluation;

namespace SmartFactory.Web.ViewModels;

public class TeamViewModel
{
    public required CurrentUser User { get; init; }
    public required FiscalHalf Half { get; init; }
    public required WorkWeek Week { get; init; }
    public required List<KraEvaluation> Rows { get; init; }
    public required List<KraEvaluation> RowsMonthly { get; init; }
    public required List<Person> WithoutKra { get; init; }
}

public class CascadeNode
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public int Ring { get; init; }
    public string? ParentKey { get; init; }
    public Person? Person { get; init; }
    public KraEvaluation? Evaluation { get; init; }
    public List<CascadeNode> Children { get; } = new();
}

public class CascadeViewModel
{
    public required CurrentUser User { get; init; }
    public required FiscalHalf Half { get; init; }
    public required WorkWeek Week { get; init; }
    public required CascadeNode Root { get; init; }
    public required List<CascadeNode> All { get; init; }
    public CascadeNode? Selected { get; init; }
}

public class WeekByWeekViewModel
{
    public required CurrentUser User { get; init; }
    public required FiscalHalf Half { get; init; }
    public required WorkWeek Week { get; init; }
    public required string Scope { get; init; }
    public required string ScopeTitle { get; init; }
    public required List<(string name, string title)> ScopeOptions { get; init; }
    public required List<WorkWeek> Weeks { get; init; }
    public required List<(string measure, List<Performance.Rules.Judgement?> cells)> Rows { get; init; }
}
