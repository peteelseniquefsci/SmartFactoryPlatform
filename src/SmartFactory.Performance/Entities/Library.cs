namespace SmartFactory.Performance.Entities;

/// <summary>One of the 16 company Top Goal lines. Every slot must point to one.</summary>
public class TopGoalLine
{
    public int Id { get; set; }
    public string FocusItem { get; set; } = "";
    public string StrategicObjective { get; set; } = "";
    public string Measure { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>RH: higher is better, ratio. RL: lower is better, ratio. AL: absolute, lower is better. M: typed manual rating.</summary>
public enum RatingMode { RH = 0, RL = 1, AL = 2, M = 3 }

/// <summary>The rating basis library: the only place rating bands are defined.</summary>
public class RatingBasis
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public RatingMode Mode { get; set; }
    public decimal? T5 { get; set; }
    public decimal? T4 { get; set; }
    public decimal? T3 { get; set; }
    public decimal? T2 { get; set; }
    public string? Band5Text { get; set; }
    public string? Band4Text { get; set; }
    public string? Band3Text { get; set; }
    public string? Band2Text { get; set; }
    public string? Band1Text { get; set; }
    public string? Notes { get; set; }
    public string? UsedFor { get; set; }
    public bool Active { get; set; } = true;

    public Rules.RatingBasisDefinition ToDefinition() => new(Key, Mode, T5, T4, T3, T2);
}
