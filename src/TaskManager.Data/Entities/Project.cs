using System.ComponentModel.DataAnnotations;

namespace TaskManager.Data.Entities;

public class Project
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    public Guid OwnerId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual User Owner { get; set; } = null!;
    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
    public virtual ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
}