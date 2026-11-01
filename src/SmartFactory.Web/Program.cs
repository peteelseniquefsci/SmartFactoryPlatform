using Microsoft.AspNetCore.Authentication.Cookies;
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();

app.MapControllerRoute(name: "default", pattern: "{controller=Ascent}/{action=Index}/{id?}");

app.Run();
