using TaskManager.Shared.Enums;

namespace TaskManager.Shared.Models;

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    
    // Navigation properties for DTOs
    public List<ProjectMemberDto> Members { get; set; } = new();
    public List<TaskDto> Tasks { get; set; } = new();
    
    // Computed properties
    public int TaskCount => Tasks.Count;
    public int CompletedTaskCount => Tasks.Count(t => t.Status == Enums.TaskStatus.Done);
    public int PendingTaskCount => Tasks.Count(t => t.Status != Enums.TaskStatus.Done);
}

public class ProjectMemberDto
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public ProjectRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}