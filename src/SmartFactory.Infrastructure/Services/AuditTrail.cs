using SmartFactory.Core.Audit;
using SmartFactory.Infrastructure.Data;

namespace SmartFactory.Infrastructure.Services;

public class AuditTrail : IAuditTrail
{
    private readonly SmartFactoryDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;

    public AuditTrail(SmartFactoryDbContext db, ICurrentUserAccessor user, IClock clock) { _db = db; _user = user; _clock = clock; }

    public void Record(string entity, string entityId, string action, string? field = null, string? oldValue = null, string? newValue = null, string? detail = null)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            At = _clock.UtcNow,
            ActorPersonId = _user.ActorPersonId,
            ActorName = _user.ActorName,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            Field = field,
            OldValue = Trim(oldValue),
            NewValue = Trim(newValue),
            Detail = Trim(detail),
        });
    }

    private static string? Trim(string? s) => s is { Length: > 2000 } ? s[..2000] : s;
}
