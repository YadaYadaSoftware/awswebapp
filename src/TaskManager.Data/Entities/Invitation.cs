using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace TaskManager.Data.Entities;

public class Invitation
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string InvitedByUserId { get; set; } = string.Empty;

    public DateTime InvitedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool IsAccepted { get; set; } = false;
    public bool IsRevoked { get; set; } = false;

    // Navigation properties
    public virtual IdentityUser InvitedByUser { get; set; } = null!;
}