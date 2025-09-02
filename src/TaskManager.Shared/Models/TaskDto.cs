using TaskManager.Shared.Enums;

namespace TaskManager.Shared.Models;

public class TaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public Enums.TaskStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Computed properties
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.UtcNow && Status != Enums.TaskStatus.Done;
    public bool IsDueSoon => DueDate.HasValue && DueDate.Value <= DateTime.UtcNow.AddDays(3) && Status != Enums.TaskStatus.Done;
    public string StatusDisplay => Status.ToString();
    public string PriorityDisplay => Priority.ToString();
}