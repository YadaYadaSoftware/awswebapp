namespace Tjb.Api.Services;

public interface IDatabaseMigrationService
{
    Task MigrateAsync();
    Task SeedDataAsync();
}