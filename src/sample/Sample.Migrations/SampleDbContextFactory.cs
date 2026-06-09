using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Sample.Data;

namespace Sample.Migrations;

/// <summary>
/// Design-time factory so `dotnet ef` can build a <see cref="SampleDbContext"/>. Uses a FIXED
/// MySQL server version (not <c>ServerVersion.AutoDetect</c>) so migrations can be generated
/// without a live database — the migrate/seed runner and the web host connect for real at runtime.
/// </summary>
public class SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    public SampleDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=sampledb;User=root;Password=password;";

        var optionsBuilder = new DbContextOptionsBuilder<SampleDbContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 35)),
            mysqlOptions => mysqlOptions.MigrationsAssembly("Sample.Migrations"));

        return new SampleDbContext(optionsBuilder.Options);
    }
}
