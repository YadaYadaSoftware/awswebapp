using System.ComponentModel.DataAnnotations;

namespace TaskManager.Data.Entities;

public class Invitation
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    public Guid InvitedByUserId { get; set; }
    
    public DateTime InvitedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool IsAccepted { get; set; } = false;
    public bool IsRevoked { get; set; } = false;
    
    // Navigation properties
    public virtual User InvitedByUser { get; set; } = null!;
}