using SmartFactory.Core.Organization;

namespace SmartFactory.Core.Identity;

/// <summary>What the company sign-in API returns for a successful credential check.</summary>
public record ExternalIdentity(string EmployeeId, string DisplayName, string? Email);

/// <summary>Sign-in through the company LDAP API. The platform never binds to LDAP directly.</summary>
public interface IIdentityProvider
{
    Task<ExternalIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
    /// <summary>Employee information lookup by id, for joiners and administration.</summary>
    Task<ExternalIdentity?> LookupAsync(string employeeId, CancellationToken ct = default);
    /// <summary>True for the development provider, which lets a developer pick any seeded person.</summary>
    bool IsDevelopment { get; }
}

/// <summary>The signed-in person with their effective roles.</summary>
public class CurrentUser
{
    public required Person Person { get; init; }
    public required IReadOnlySet<PlatformRole> Roles { get; init; }
    public bool Is(PlatformRole role) => Roles.Contains(role);
    public bool IsHeadOrManager => Is(PlatformRole.SectionHead) || Is(PlatformRole.Manager);
    public bool IsAdministrator => Is(PlatformRole.Administrator);
    public int PersonId => Person.Id;
    public string Name => Person.Name;
}

public static class RoleResolver
{
    /// <summary>Person always; SectionHead and Manager from the position; HrViewer and Administrator from assignments.</summary>
    public static IReadOnlySet<PlatformRole> Resolve(Person person)
    {
        var roles = new HashSet<PlatformRole> { PlatformRole.Person };
        if (person.Position?.IsSectionHead == true) roles.Add(PlatformRole.SectionHead);
        if (person.Position?.IsManager == true) { roles.Add(PlatformRole.Manager); roles.Add(PlatformRole.SectionHead); }
        foreach (var r in person.Roles) roles.Add(r.Role);
        return roles;
    }
}
