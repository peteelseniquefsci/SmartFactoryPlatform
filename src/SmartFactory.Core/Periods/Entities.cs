namespace SmartFactory.Core.Periods;

/// <summary>A half of a fiscal year. The fiscal year runs April to March; 2H FY2026 is October 2026 to March 2027.</summary>
public class FiscalHalf
{
    public int Id { get; set; }
    public int FiscalYear { get; set; }
    /// <summary>1 or 2.</summary>
    public int Half { get; set; }
    /// <summary>"2H FY2026".</summary>
    public string Label { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public List<FiscalMonth> Months { get; set; } = new();
}

public class FiscalMonth
{
    public int Id { get; set; }
    public int FiscalHalfId { get; set; }
    public FiscalHalf? FiscalHalf { get; set; }
    /// <summary>1..6 within the half.</summary>
    public int Index { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>"Oct 2026".</summary>
    public string Label { get; set; } = "";
    /// <summary>n: work weeks assigned to this month.</summary>
    public int WeeksInMonth { get; set; }
    public List<WorkWeek> Weeks { get; set; } = new();
}

/// <summary>An ISO work week, Monday to Sunday, assigned to the month holding its Thursday.</summary>
public class WorkWeek
{
    public int Id { get; set; }
    public int FiscalMonthId { get; set; }
    public FiscalMonth? FiscalMonth { get; set; }
    /// <summary>"WW40".</summary>
    public string Label { get; set; } = "";
    public int IsoYear { get; set; }
    public int IsoWeek { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>k: week number within its month, from 1.</summary>
    public int K { get; set; }
    /// <summary>n: weeks in the month.</summary>
    public int N { get; set; }
    /// <summary>Order within the half, from 1.</summary>
    public int Sequence { get; set; }
}
