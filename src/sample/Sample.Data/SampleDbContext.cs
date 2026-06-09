using Microsoft.EntityFrameworkCore;
using Tjb.Web.Framework.Data;

namespace Sample.Data;

/// <summary>
/// The sample's consumer DbContext: derives the framework's Identity base
/// (<see cref="AwsWebAppIdentityDbContext"/>) and adds the sample's own data. This is the
/// reuse seam the change exists to prove — Identity schema from the package, app data here.
/// </summary>
public class SampleDbContext : AwsWebAppIdentityDbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = "Server=localhost;Database=sampledb;User=root;Password=password;";
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mysqlOptions =>
                mysqlOptions.MigrationsAssembly("Sample.Migrations"));
        }
    }

    public DbSet<GuestbookEntry> GuestbookEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // base first — preserves the framework's Identity mapping (the migration-equivalence guarantee).
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<GuestbookEntry>(entry =>
        {
            entry.HasKey(g => g.Id);
            entry.Property(g => g.AuthorUserId).IsRequired().HasMaxLength(450);
            entry.Property(g => g.Message).IsRequired().HasMaxLength(1000);
            entry.HasIndex(g => g.CreatedUtc);
        });
    }
}
