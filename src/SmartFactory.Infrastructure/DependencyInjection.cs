using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Core.Audit;
using SmartFactory.Core.Identity;
using SmartFactory.Core.Messaging;
using SmartFactory.Infrastructure.Data;
using SmartFactory.Infrastructure.Export;
using SmartFactory.Infrastructure.Identity;
using SmartFactory.Infrastructure.Import;
using SmartFactory.Infrastructure.Messaging;
using SmartFactory.Infrastructure.Seed;
using SmartFactory.Infrastructure.Services;

namespace SmartFactory.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers MySQL, the services and the company API clients. Identity: "Development" or "CompanyApi"; Email: "Logging" or "ExchangeApi".</summary>
    public static IServiceCollection AddSmartFactoryInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("SmartFactory") ?? throw new InvalidOperationException("ConnectionStrings:SmartFactory is required.");
        var versionText = config["Database:ServerVersion"];
        services.AddDbContext<SmartFactoryDbContext>(o =>
        {
            var version = string.IsNullOrWhiteSpace(versionText) ? ServerVersion.AutoDetect(conn) : ServerVersion.Parse(versionText);
            o.UseMySql(conn, version, m => m.MigrationsAssembly(typeof(SmartFactoryDbContext).Assembly.FullName));
        });

        services.Configure<CompanyApiOptions>(config.GetSection("CompanyApi"));
        services.Configure<SeedOptions>(config.GetSection("Seed"));

        var clockDate = config["Clock:Today"];
        if (!string.IsNullOrWhiteSpace(clockDate)) services.AddSingleton<IClock>(new FixedClock(DateOnly.Parse(clockDate)));
        else services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<PeriodService>();
        services.AddScoped<KraService>();
        services.AddScoped<WeeklyEntryService>();
        services.AddScoped<EvaluationService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ExcelExportService>();
        services.AddScoped<WeeklyResultImporter>();
        services.AddScoped<TemplateImporter>();
        services.AddScoped<Seeder>();

        if (string.Equals(config["Identity:Provider"], "CompanyApi", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IIdentityProvider, LdapApiIdentityProvider>();
        else
            services.AddScoped<IIdentityProvider, DevelopmentIdentityProvider>();

        if (string.Equals(config["Email:Provider"], "ExchangeApi", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IEmailSender, ExchangeApiEmailSender>();
        else
            services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
