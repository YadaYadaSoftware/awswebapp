using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using TaskManager.Shared.Enums;

namespace TaskManager.Data.Entities;

public class ProjectMember
{
    [Required]
    public Guid ProjectId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ProjectRole Role { get; set; } = ProjectRole.Member;
    public DateTime JoinedAt { get; set; }

    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual IdentityUser User { get; set; } = null!;
}