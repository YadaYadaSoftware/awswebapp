## Brief overview
Project-specific guidelines for database migration management in the TaskManager application, ensuring migrations are applied automatically by applications and never manually from console tools.

## Migration application policy
- Never apply database migrations from the console using `dotnet run --project src/TaskManager.Migrations`
- Always allow applications to apply migrations automatically on startup
- Migrations are applied by both TaskManager.Api and TaskManager.Web2 during application initialization
- The TaskManager.Migrations project serves only for design-time operations (adding/removing migrations)

## Application startup behavior
- Both API and Web applications call `ApplyDatabaseMigrations()` during startup
- Migrations are applied with error handling to prevent application startup failures
- Seed data is applied after migrations if the database is empty
- Migration failures are logged but don't crash the application

## Console tool restrictions
- Running `src/TaskManager.Migrations` directly displays a warning about disabled manual migration
- The console tool provides guidance to start applications instead for migration application
- Design-time factory remains functional for EF Core CLI operations

## Documentation updates
- Remove all references to manual migration commands from guides
- Emphasize automatic migration application in development and production workflows
- Update local development setup to start applications rather than run migrations manually