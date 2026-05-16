using System.ComponentModel.DataAnnotations;
using TaskManager.Shared.Enums;

namespace TaskManager.Data.Entities;

public class Task
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    [Required]
    public Guid ProjectId { get; set; }
    
    public Guid? AssignedToId { get; set; }
    
    public Shared.Enums.TaskStatus Status { get; set; } = Shared.Enums.TaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual User? AssignedTo { get; set; }
}