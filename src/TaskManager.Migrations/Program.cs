using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManager.Data;

namespace TaskManager.Migrations;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        
        try
        {
            var context = services.GetRequiredService<TaskManagerDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Starting database migration...");
            
            // Apply any pending migrations
            await context.Database.MigrateAsync();
            
            logger.LogInformation("Database migration completed successfully.");
            
            // Seed initial data if needed
            await SeedData(context, logger);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
                
                services.AddDbContext<TaskManagerDbContext>(options =>
                    options.UseSqlServer(connectionString));
                
                services.AddLogging();
            });

    private static async Task SeedData(TaskManagerDbContext context, ILogger logger)
    {
        logger.LogInformation("Checking for seed data...");
        
        // Check if we already have users
        if (await context.Users.AnyAsync())
        {
            logger.LogInformation("Database already contains data. Skipping seed.");
            return;
        }
        
        logger.LogInformation("Seeding initial data...");
        
        // Create a sample user for testing
        var sampleUser = new TaskManager.Data.Entities.User
        {
            Id = Guid.NewGuid(),
            Email = "admin@taskmanager.com",
            FirstName = "Admin",
            LastName = "User",
            GoogleId = null, // Will be set when user logs in with Google
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        context.Users.Add(sampleUser);
        
        // Create a sample project
        var sampleProject = new TaskManager.Data.Entities.Project
        {
            Id = Guid.NewGuid(),
            Name = "Welcome Project",
            Description = "Your first project in TaskManager",
            OwnerId = sampleUser.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        context.Projects.Add(sampleProject);
        
        // Create a sample task
        var sampleTask = new TaskManager.Data.Entities.Task
        {
            Id = Guid.NewGuid(),
            Title = "Welcome to TaskManager",
            Description = "This is your first task. You can edit or delete it.",
            ProjectId = sampleProject.Id,
            AssignedToId = sampleUser.Id,
            Status = TaskManager.Shared.Enums.TaskStatus.Todo,
            Priority = TaskManager.Shared.Enums.TaskPriority.Medium,
            DueDate = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        context.Tasks.Add(sampleTask);
        
        // Add project member relationship
        var projectMember = new TaskManager.Data.Entities.ProjectMember
        {
            ProjectId = sampleProject.Id,
            UserId = sampleUser.Id,
            Role = TaskManager.Shared.Enums.ProjectRole.Owner,
            JoinedAt = DateTime.UtcNow
        };
        
        context.ProjectMembers.Add(projectMember);
        
        await context.SaveChangesAsync();
        
        logger.LogInformation("Seed data created successfully.");
        logger.LogInformation($"Sample user: {sampleUser.Email}");
        logger.LogInformation($"Sample project: {sampleProject.Name}");
        logger.LogInformation($"Sample task: {sampleTask.Title}");
    }
}