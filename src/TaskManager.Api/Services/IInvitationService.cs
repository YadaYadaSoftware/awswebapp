using TaskManager.Shared.Models;

namespace TaskManager.Api.Services;

public interface IInvitationService
{
    Task<bool> IsUserInvitedAsync(string email);
    Task<InvitationDto> CreateInvitationAsync(string email, string invitedByUserId);
    Task<bool> AcceptInvitationAsync(string email, string googleId);
    Task<List<InvitationDto>> GetPendingInvitationsAsync();
    Task<bool> RevokeInvitationAsync(string email);
}

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