using System.ComponentModel.DataAnnotations;
using TaskManager.Shared.Enums;

namespace TaskManager.Data.Entities;

public class ProjectMember
{
    [Required]
    public Guid ProjectId { get; set; }
    
    [Required]
    public Guid UserId { get; set; }
    
    public ProjectRole Role { get; set; } = ProjectRole.Member;
    public DateTime JoinedAt { get; set; }
    
    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}