namespace TaskManager.Shared.Models;

public class InvitationDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string InvitedByUserId { get; set; } = string.Empty;
    public string InvitedByName { get; set; } = string.Empty;
    public DateTime InvitedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool IsAccepted { get; set; }
    public bool IsRevoked { get; set; }
}