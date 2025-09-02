using System.ComponentModel.DataAnnotations;
using TaskManager.Shared.Enums;

namespace TaskManager.Shared.Models;

public class CreateTaskRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;
    
    [StringLength(1000)]
    public string? Description { get; set; }
    
    [Required]
    public Guid ProjectId { get; set; }
    
    public Guid? AssignedToId { get; set; }
    
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    
    public DateTime? DueDate { get; set; }
}

public class UpdateTaskRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;
    
    [StringLength(1000)]
    public string? Description { get; set; }
    
    public Guid? AssignedToId { get; set; }
    
    public Enums.TaskStatus Status { get; set; }
    
    public TaskPriority Priority { get; set; }
    
    public DateTime? DueDate { get; set; }
}

public class CreateProjectRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
}

public class UpdateProjectRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
}

public class AddProjectMemberRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    public ProjectRole Role { get; set; } = ProjectRole.Member;
}