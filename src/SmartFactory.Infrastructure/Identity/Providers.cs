using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartFactory.Core.Identity;
using SmartFactory.Infrastructure.Data;

namespace SmartFactory.Infrastructure.Identity;

public class CompanyApiOptions
{
    /// <summary>Base address of the company LDAP / employee-information API. Sign-in never binds to LDAP directly.</summary>
    public string? IdentityApiBaseUrl { get; set; }
    public string IdentityAuthenticatePath { get; set; } = "/api/auth/login";
    public string IdentityLookupPath { get; set; } = "/api/employees/{id}";
    /// <summary>The single permitted Exchange API server address for email.</summary>
    public string? ExchangeApiUrl { get; set; }
    public string? ExchangeSender { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>Development only: accepts any password for a seeded employee id.</summary>
public class DevelopmentIdentityProvider : IIdentityProvider
{
    private readonly SmartFactoryDbContext _db;
    public DevelopmentIdentityProvider(SmartFactoryDbContext db) { _db = db; }
    public bool IsDevelopment => true;

    public async Task<ExternalIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
        => await LookupAsync(username, ct);

    public async Task<ExternalIdentity?> LookupAsync(string employeeId, CancellationToken ct = default)
    {
        var p = await _db.People.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        return p is null ? null : new ExternalIdentity(p.EmployeeId, p.Name, p.Email);
    }
}

/// <summary>Sign-in through the company LDAP API over HTTP. Expected JSON reply: { "employeeId", "displayName", "email" }.</summary>
public class LdapApiIdentityProvider : IIdentityProvider
{
    private readonly HttpClient _http;
    private readonly CompanyApiOptions _options;
    public LdapApiIdentityProvider(HttpClient http, IOptions<CompanyApiOptions> options) { _http = http; _options = options.Value; }
    public bool IsDevelopment => false;

    private record ApiIdentity(string EmployeeId, string DisplayName, string? Email);

    public async Task<ExternalIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.IdentityApiBaseUrl)) throw new InvalidOperationException("CompanyApi:IdentityApiBaseUrl is not configured.");
        using var resp = await _http.PostAsJsonAsync(new Uri(new Uri(_options.IdentityApiBaseUrl), _options.IdentityAuthenticatePath), new { username, password }, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var id = await resp.Content.ReadFromJsonAsync<ApiIdentity>(cancellationToken: ct);
        return id is null ? null : new ExternalIdentity(id.EmployeeId, id.DisplayName, id.Email);
    }

    public async Task<ExternalIdentity?> LookupAsync(string employeeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.IdentityApiBaseUrl)) throw new InvalidOperationException("CompanyApi:IdentityApiBaseUrl is not configured.");
        var path = _options.IdentityLookupPath.Replace("{id}", Uri.EscapeDataString(employeeId));
        using var resp = await _http.GetAsync(new Uri(new Uri(_options.IdentityApiBaseUrl), path), ct);
        if (!resp.IsSuccessStatusCode) return null;
        var id = await resp.Content.ReadFromJsonAsync<ApiIdentity>(cancellationToken: ct);
        return id is null ? null : new ExternalIdentity(id.EmployeeId, id.DisplayName, id.Email);
    }
}
