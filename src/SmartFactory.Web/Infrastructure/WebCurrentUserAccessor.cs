using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using SmartFactory.Core.Identity;
using SmartFactory.Infrastructure.Services;

namespace SmartFactory.Web.Infrastructure;

public class ProductOptions { public string Name { get; set; } = "Smart Factory Platform"; public string Company { get; set; } = ""; }

public static class Policies
{
    public const string HeadOrManager = "HeadOrManager";
    public const string Manager = "Manager";
    public const string Exports = "Exports";
    public const string Administrator = "Administrator";
}

/// <summary>Resolves the signed-in person once per request from the cookie's person id claim.</summary>
public class WebCurrentUserAccessor : ICurrentUserAccessor
{
    public const string PersonIdClaim = "sf:pid";
    private readonly IHttpContextAccessor _http;
    private readonly OrganizationService _org;
    private CurrentUser? _user;
    private bool _loaded;

    public WebCurrentUserAccessor(IHttpContextAccessor http, OrganizationService org) { _http = http; _org = org; }

    public CurrentUser? User => _loaded ? _user : LoadAsync().GetAwaiter().GetResult();

    public async Task<CurrentUser?> LoadAsync()
    {
        if (_loaded) return _user;
        _loaded = true;
        var principal = _http.HttpContext?.User;
        var claim = principal?.FindFirst(PersonIdClaim)?.Value;
        if (claim is null || !int.TryParse(claim, out var pid)) return null;
        var person = await _org.LoadPersonAsync(pid);
        if (person is null || !person.Active) return null;
        _user = new CurrentUser { Person = person, Roles = RoleResolver.Resolve(person) };
        return _user;
    }

    public string ActorName => User?.Name ?? "system";
    public int? ActorPersonId => User?.PersonId;
}

/// <summary>Loads the current user early so roles in the cookie are re-checked against the organization on every request.</summary>
public class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;
    public CurrentUserMiddleware(RequestDelegate next) { _next = next; }
    public async Task InvokeAsync(HttpContext context, WebCurrentUserAccessor accessor)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = await accessor.LoadAsync();
            if (user is null && !context.Request.Path.StartsWithSegments("/account"))
            {
                await context.SignOutAsync();
                context.Response.Redirect("/account/sign-in");
                return;
            }
            if (user is not null)
            {
                var identity = (ClaimsIdentity)context.User.Identity;
                foreach (var r in user.Roles) if (!identity.HasClaim(ClaimTypes.Role, r.ToString())) identity.AddClaim(new Claim(ClaimTypes.Role, r.ToString()));
            }
        }
        await _next(context);
    }
}
