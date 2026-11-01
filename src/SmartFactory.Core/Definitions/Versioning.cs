namespace SmartFactory.Core.Definitions;

/// <summary>Mode of any versioned definition the platform approves. SETUP allows edits; LOCKED rejects them.</summary>
public enum DefinitionMode { Setup = 0, Locked = 1 }

/// <summary>Where a definition sits in its approval workflow.</summary>
public enum ApprovalStatus { Draft = 0, Submitted = 1, Returned = 2, Approved = 3 }

/// <summary>A definition that carries SETUP / LOCKED state, versions and approvals. Modules implement it; the core approval service works on it.</summary>
public interface IVersionedDefinition
{
    DefinitionMode Mode { get; set; }
    ApprovalStatus Status { get; set; }
    int Version { get; set; }
    int? ApprovedByPersonId { get; set; }
    string? ApprovedByName { get; set; }
    DateTime? ApprovedAt { get; set; }
}
