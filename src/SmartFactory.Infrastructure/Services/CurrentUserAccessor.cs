using SmartFactory.Core.Identity;

namespace SmartFactory.Infrastructure.Services;

/// <summary>Who is acting. The web layer sets it from the signed-in cookie; imports and seeding set a system actor.</summary>
public interface ICurrentUserAccessor
{
    CurrentUser? User { get; }
    string ActorName { get; }
    int? ActorPersonId { get; }
}

public class SystemUserAccessor : ICurrentUserAccessor
{
    public SystemUserAccessor(string name) { ActorName = name; }
    public CurrentUser? User => null;
    public string ActorName { get; }
    public int? ActorPersonId => null;
}

public interface IClock { DateTime UtcNow { get; } DateOnly Today { get; } }

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>A clock pinned to a date, for demos and tests.</summary>
public class FixedClock : IClock
{
    public FixedClock(DateOnly today) { Today = today; }
    public DateTime UtcNow => Today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
    public DateOnly Today { get; }
}
