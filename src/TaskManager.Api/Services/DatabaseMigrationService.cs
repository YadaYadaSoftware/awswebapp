using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManager.Data;
using TaskManager.Data.Entities;
using TaskManager.Shared.Enums;
using System.Threading.Tasks;

namespace TaskManager.Api.Services;

public class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly TaskManagerDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(TaskManagerDbContext context, UserManager<IdentityUser> userManager, ILogger<DatabaseMigrationService> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async System.Threading.Tasks.Task MigrateAsync()
    {
        try
        {
            _logger.LogInformation("Ensuring database exists and applying migrations...");

            // This will create the database if it doesn't exist
            await _context.Database.EnsureCreatedAsync();

            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();

            if (pendingMigrations.Any())
            {
                _logger.LogInformation("Applying {Count} pending migrations: {Migrations}",
                    pendingMigrations.Count(),
                    string.Join(", ", pendingMigrations));

                await _context.Database.MigrateAsync();

                _logger.LogInformation("Database migrations applied successfully.");

                // Apply seed data after migrations
                await SeedDataAsync();
            }
            else
            {
                _logger.LogInformation("No pending migrations found.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply database migrations.");
            throw;
        }
    }

    public async System.Threading.Tasks.Task SeedDataAsync()
    {
        try
        {
            _logger.LogInformation("Checking for seed data...");

            // Check if we already have users
            if (await _userManager.Users.AnyAsync())
            {
                _logger.LogInformation("Database already contains data. Skipping seed.");
                return;
            }

            _logger.LogInformation("Seeding initial data...");

            // Create a sample user for testing
            var sampleUser = new IdentityUser
            {
                UserName = "admin@taskmanager.com",
                Email = "admin@taskmanager.com",
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(sampleUser, "Admin123!");
            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create sample user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return;
            }

            // Create initial admin invitation for the deployer
            var adminInvitation = new Invitation
            {
                Id = Guid.NewGuid(),
                Email = "admin@taskmanager.com", // This should be your email
                InvitedByUserId = sampleUser.Id,
                InvitedAt = DateTime.UtcNow,
                IsAccepted = true, // Pre-accepted for admin
                IsRevoked = false
            };

            _context.Invitations.Add(adminInvitation);

            // Create a sample project
            var sampleProject = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Welcome Project",
                Description = "Your first project in TaskManager - feel free to edit or delete this!",
                OwnerId = sampleUser.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Projects.Add(sampleProject);

            // Create sample tasks
            var tasks = new[]
            {
                new Data.Entities.Task
                {
                    Id = Guid.NewGuid(),
                    Title = "Welcome to TaskManager!",
                    Description = "This is your first task. You can edit, complete, or delete it.",
                    ProjectId = sampleProject.Id,
                    AssignedToId = sampleUser.Id,
                    Status = Shared.Enums.TaskStatus.Todo,
                    Priority = TaskPriority.High,
                    DueDate = DateTime.UtcNow.AddDays(7),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Data.Entities.Task
                {
                    Id = Guid.NewGuid(),
                    Title = "Explore the application",
                    Description = "Take a look around and familiarize yourself with the features.",
                    ProjectId = sampleProject.Id,
                    AssignedToId = sampleUser.Id,
                    Status = Shared.Enums.TaskStatus.InProgress,
                    Priority = TaskPriority.Medium,
                    DueDate = DateTime.UtcNow.AddDays(3),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Data.Entities.Task
                {
                    Id = Guid.NewGuid(),
                    Title = "Create your first real project",
                    Description = "When you're ready, create a project for your actual work.",
                    ProjectId = sampleProject.Id,
                    AssignedToId = null, // Unassigned
                    Status = Shared.Enums.TaskStatus.Todo,
                    Priority = TaskPriority.Low,
                    DueDate = DateTime.UtcNow.AddDays(14),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };

            _context.Tasks.AddRange(tasks);

            // Add project member relationship
            var projectMember = new ProjectMember
            {
                ProjectId = sampleProject.Id,
                UserId = sampleUser.Id,
                Role = ProjectRole.Owner,
                JoinedAt = DateTime.UtcNow
            };

            _context.ProjectMembers.Add(projectMember);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Seed data created successfully.");
            _logger.LogInformation("Sample user: {Email}", sampleUser.Email);
            _logger.LogInformation("Sample project: {ProjectName}", sampleProject.Name);
            _logger.LogInformation("Sample tasks: {TaskCount}", tasks.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed initial data.");
            // Don't throw - seeding is optional
        }
    }
}