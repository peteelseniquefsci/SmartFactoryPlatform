using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartFactory.Infrastructure.Data;

/// <summary>Used by `dotnet ef` to generate migrations without a running database.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<SmartFactoryDbContext>
{
    public SmartFactoryDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("SMARTFACTORY_CONNECTION") ?? "Server=localhost;Database=smartfactory;User=sf;Password=sf;";
        var version = Environment.GetEnvironmentVariable("SMARTFACTORY_SERVERVERSION") ?? "8.0.36-mysql";
        var options = new DbContextOptionsBuilder<SmartFactoryDbContext>()
            .UseMySql(conn, ServerVersion.Parse(version), o => o.MigrationsAssembly(typeof(SmartFactoryDbContext).Assembly.FullName))
            .Options;
        return new SmartFactoryDbContext(options);
    }
}
