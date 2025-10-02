using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskManager.Data.Entities;

namespace TaskManager.Data;

public class TaskManagerDbContext : IdentityDbContext<IdentityUser>
{
    public TaskManagerDbContext(DbContextOptions<TaskManagerDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects { get; set; }
    public DbSet<Entities.Task> Tasks { get; set; }
    public DbSet<ProjectMember> ProjectMembers { get; set; }
    public DbSet<Invitation> Invitations { get; set; }

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
        ConfigureInvitationRelationships(modelBuilder);

        // Configure indexes
        ConfigureIndexes(modelBuilder);
    }

    private static void ConfigureUserRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>()
            .HasOne<IdentityUser>(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Entities.Task>()
            .HasOne<IdentityUser>(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ProjectMember>()
            .HasOne<IdentityUser>(pm => pm.User)
            .WithMany()
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

    private static void ConfigureInvitationRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invitation>()
            .HasOne(i => i.InvitedByUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureIndexes(ModelBuilder modelBuilder)
    {

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

        // Invitation indexes
        modelBuilder.Entity<Invitation>()
            .HasIndex(i => i.Email)
            .IsUnique();

        modelBuilder.Entity<Invitation>()
            .HasIndex(i => i.InvitedByUserId);

        modelBuilder.Entity<Invitation>()
            .HasIndex(i => i.IsAccepted);
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
            .Where(e => e.Entity is Project || e.Entity is Entities.Task || e.Entity is ProjectMember || e.Entity is Invitation)
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                // Generate Id if not set
                if (entry.Entity is Project project)
                {
                    if (project.Id == Guid.Empty) project.Id = Guid.NewGuid();
                    project.CreatedAt = now;
                    project.UpdatedAt = now;
                }
                else if (entry.Entity is Entities.Task task)
                {
                    if (task.Id == Guid.Empty) task.Id = Guid.NewGuid();
                    task.CreatedAt = now;
                    task.UpdatedAt = now;
                }
                else if (entry.Entity is ProjectMember projectMember)
                {
                    // ProjectMember has composite key, no Id to generate
                    projectMember.JoinedAt = now;
                }
                else if (entry.Entity is Invitation invitation)
                {
                    if (invitation.Id == Guid.Empty) invitation.Id = Guid.NewGuid();
                    invitation.InvitedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is Project project)
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