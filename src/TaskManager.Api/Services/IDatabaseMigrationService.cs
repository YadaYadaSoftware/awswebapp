namespace TaskManager.Api.Services;

public interface IDatabaseMigrationService
{
    Task MigrateAsync();
    Task SeedDataAsync();
}