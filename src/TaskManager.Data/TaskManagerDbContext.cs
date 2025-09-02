using Microsoft.EntityFrameworkCore;
using TaskManager.Data.Entities;

namespace TaskManager.Data;

public class TaskManagerDbContext : DbContext
{
    public TaskManagerDbContext(DbContextOptions<TaskManagerDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Entities.Task> Tasks { get; set; }
    public DbSet<ProjectMember> ProjectMembers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskManagerDbContext).Assembly);

        // Configure composite key for ProjectMember
        modelBuilder.Entity<ProjectMember>()
            .HasKey(pm => new { pm.ProjectId, pm.UserId });

        // Configure relationships
        ConfigureUserRelationships(modelBuilder);
        ConfigureProjectRelationships(modelBuilder);
        ConfigureTaskRelationships(modelBuilder);
        ConfigureProjectMemberRelationships(modelBuilder);

        // Configure indexes
        ConfigureIndexes(modelBuilder);
    }

    private static void ConfigureUserRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasMany(u => u.OwnedProjects)
            .WithOne(p => p.Owner)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasMany(u => u.AssignedTasks)
            .WithOne(t => t.AssignedTo)
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<User>()
            .HasMany(u => u.ProjectMemberships)
            .WithOne(pm => pm.User)
            .HasForeignKey(pm => pm.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureProjectRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>()
            .HasMany(p => p.Tasks)
            .WithOne(t => t.Project)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Project>()
            .HasMany(p => p.Members)
            .WithOne(pm => pm.Project)
            .HasForeignKey(pm => pm.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTaskRelationships(ModelBuilder modelBuilder)
    {
        // Task relationships are already configured in User and Project configurations
    }

    private static void ConfigureProjectMemberRelationships(ModelBuilder modelBuilder)
    {
        // ProjectMember relationships are already configured in User and Project configurations
    }

    private static void ConfigureIndexes(ModelBuilder modelBuilder)
    {
        // User indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasFilter("\"GoogleId\" IS NOT NULL");

        // Project indexes
        modelBuilder.Entity<Project>()
            .HasIndex(p => p.OwnerId);

        modelBuilder.Entity<Project>()
            .HasIndex(p => new { p.Name, p.OwnerId });

        // Task indexes
        modelBuilder.Entity<Entities.Task>()
            .HasIndex(t => t.ProjectId);

        modelBuilder.Entity<Entities.Task>()
            .HasIndex(t => t.AssignedToId);

        modelBuilder.Entity<Entities.Task>()
            .HasIndex(t => t.Status);

        modelBuilder.Entity<Entities.Task>()
            .HasIndex(t => t.DueDate);

        // ProjectMember indexes
        modelBuilder.Entity<ProjectMember>()
            .HasIndex(pm => pm.UserId);

        modelBuilder.Entity<ProjectMember>()
            .HasIndex(pm => pm.ProjectId);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is User || e.Entity is Project || e.Entity is Entities.Task)
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is User user)
                {
                    user.CreatedAt = now;
                    user.UpdatedAt = now;
                }
                else if (entry.Entity is Project project)
                {
                    project.CreatedAt = now;
                    project.UpdatedAt = now;
                }
                else if (entry.Entity is Entities.Task task)
                {
                    task.CreatedAt = now;
                    task.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is User user)
                {
                    user.UpdatedAt = now;
                }
                else if (entry.Entity is Project project)
                {
                    project.UpdatedAt = now;
                }
                else if (entry.Entity is Entities.Task task)
                {
                    task.UpdatedAt = now;
                }
            }
        }
    }
}