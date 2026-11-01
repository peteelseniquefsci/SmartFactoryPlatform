namespace SmartFactory.Core.Audit;

/// <summary>One change: who, when, what, old and new value. Every change to a definition, approval, lock, result or remark writes one.</summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? ActorPersonId { get; set; }
    public string ActorName { get; set; } = "";
    /// <summary>Entity type name, e.g. "KraSlot".</summary>
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>Create, Update, Delete, Lock, Unlock, Submit, Approve, Return, Import.</summary>
    public string Action { get; set; } = "";
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Detail { get; set; }
}

public interface IAuditTrail
{
    void Record(string entity, string entityId, string action, string? field = null, string? oldValue = null, string? newValue = null, string? detail = null);
}
