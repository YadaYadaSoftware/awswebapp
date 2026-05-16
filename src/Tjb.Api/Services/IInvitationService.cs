using Tjb.Shared.Models;

namespace Tjb.Api.Services;

public interface IInvitationService
{
    Task<bool> IsUserInvitedAsync(string email);
    Task<InvitationDto> CreateInvitationAsync(string email, string invitedByUserId);
    Task<bool> AcceptInvitationAsync(string email, string googleId);
    Task<List<InvitationDto>> GetPendingInvitationsAsync();
    Task<bool> RevokeInvitationAsync(string email);
}