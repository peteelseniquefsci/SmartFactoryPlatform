using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Core.Identity;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Web.Infrastructure;

namespace SmartFactory.Web.Controllers;

[AllowAnonymous]
[Route("account")]
public class AccountController : Controller
{
    private readonly IIdentityProvider _identity;
    private readonly OrganizationService _org;
    private readonly SmartFactoryDbContext _db;

    public AccountController(IIdentityProvider identity, OrganizationService org, SmartFactoryDbContext db) { _identity = identity; _org = org; _db = db; }

    [HttpGet("sign-in")]
    public async Task<IActionResult> SignIn(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.IsDevelopment = _identity.IsDevelopment;
        if (_identity.IsDevelopment)
            ViewBag.People = await _db.People.Include(p => p.Position).Where(p => p.Active).OrderBy(p => p.Position!.SortOrder).ThenBy(p => p.Name).ToListAsync();
        return View();
    }

    [HttpPost("sign-in")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn(string username, string? password, string? returnUrl = null)
    {
        var external = await _identity.AuthenticateAsync(username?.Trim() ?? "", password ?? "");
        if (external is null)
        {
            TempData["Error"] = "Sign-in failed. Check your employee id and password.";
            return RedirectToAction(nameof(SignIn), new { returnUrl });
        }
        var person = await _org.LoadPersonByEmployeeIdAsync(external.EmployeeId);
        if (person is null || !person.Active)
        {
            TempData["Error"] = "You are signed in with the company directory, but you are not yet on the Smart Factory table of organization. Ask an administrator to add you.";
            return RedirectToAction(nameof(SignIn), new { returnUrl });
        }
        var roles = RoleResolver.Resolve(person);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, person.EmployeeId),
            new(ClaimTypes.Name, person.Name),
            new(WebCurrentUserAccessor.PersonIdClaim, person.Id.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r.ToString())));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }

    [HttpPost("sign-out")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutPost()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(SignIn));
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();
}

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("error")]
    public IActionResult Index() { ViewBag.Message = "The page could not be shown."; return View("~/Views/Shared/Error.cshtml"); }

    [Route("error/{code:int}")]
    public IActionResult Code(int code)
    {
        ViewBag.Message = code switch { 404 => "That page does not exist.", 403 => "You do not have access to that page.", _ => "The page could not be shown." };
        Response.StatusCode = code;
        return View("~/Views/Shared/Error.cshtml");
    }
}
