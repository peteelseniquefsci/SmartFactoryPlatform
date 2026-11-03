using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartFactory.Core.Organization;
using SmartFactory.Infrastructure;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Seed;
using SmartFactory.Infrastructure.Services;
using SmartFactory.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartFactoryInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WebCurrentUserAccessor>();
builder.Services.AddScoped<ICurrentUserAccessor>(sp => sp.GetRequiredService<WebCurrentUserAccessor>());
builder.Services.Configure<ProductOptions>(builder.Configuration.GetSection("Product"));

// Hosting: IIS (in-process) or Kestrel behind a reverse proxy. Keys persist to a folder so sign-in cookies survive app-pool
// recycles and work across servers; set DataProtection:KeysPath to a folder the app-pool identity can write.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("SmartFactoryPlatform");
if (builder.Configuration.GetValue("Hosting:BehindReverseProxy", false))
    builder.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.KnownNetworks.Clear(); o.KnownProxies.Clear(); });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/account/sign-in";
    o.AccessDeniedPath = "/account/denied";
    o.ExpireTimeSpan = TimeSpan.FromHours(10);
    o.SlidingExpiration = true;
    o.Cookie.Name = "smartfactory.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.HeadOrManager, p => p.RequireRole(nameof(PlatformRole.SectionHead), nameof(PlatformRole.Manager), nameof(PlatformRole.Administrator)));
    o.AddPolicy(Policies.Manager, p => p.RequireRole(nameof(PlatformRole.Manager), nameof(PlatformRole.Administrator)));
    o.AddPolicy(Policies.Exports, p => p.RequireRole(nameof(PlatformRole.Manager), nameof(PlatformRole.HrViewer), nameof(PlatformRole.Administrator)));
    o.AddPolicy(Policies.Administrator, p => p.RequireRole(nameof(PlatformRole.Administrator)));
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddControllersWithViews(o => o.Filters.Add<RuleViolationFilter>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var db = scope.ServiceProvider.GetRequiredService<SmartFactoryDbContext>();
    if (config.GetValue("Database:MigrateOnStartup", true)) await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<Seeder>();
    await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value);
}

var pathBase = app.Configuration["Hosting:PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase)) app.UsePathBase(pathBase);   // IIS sets the path base itself; this is for Kestrel behind a proxy.
if (app.Configuration.GetValue("Hosting:BehindReverseProxy", false)) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    if (app.Configuration.GetValue("Hosting:UseHsts", true)) app.UseHsts();
    if (app.Configuration.GetValue("Hosting:RedirectToHttps", false)) app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();

app.MapControllerRoute(name: "default", pattern: "{controller=Ascent}/{action=Index}/{id?}");

app.Run();
