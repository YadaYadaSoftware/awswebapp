using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManager.Data;
using TaskManager.Data.Entities;
using TaskManager.Shared.Models;

namespace TaskManager.Api.Services;

public class InvitationService : IInvitationService
{
    private readonly TaskManagerDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(TaskManagerDbContext context, UserManager<IdentityUser> userManager, ILogger<InvitationService> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<bool> IsUserInvitedAsync(string email)
    {
        try
        {
            // Check if user already exists
            var existingUser = await _userManager.FindByEmailAsync(email.ToLower());
            if (existingUser != null)
            {
                return true; // Existing user
            }

            // Check for pending invitation
            var invitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Email.ToLower() == email.ToLower()
                                     && !i.IsRevoked
                                     && !i.IsAccepted);

            return invitation != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking invitation status for email: {Email}", email);
            return false;
        }
    }

    public async Task<InvitationDto> CreateInvitationAsync(string email, string invitedByUserId)
    {
        try
        {
            // Check if invitation already exists
            var existingInvitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Email.ToLower() == email.ToLower() && !i.IsRevoked);

            if (existingInvitation != null)
            {
                throw new InvalidOperationException($"Invitation already exists for {email}");
            }

            // Check if user already exists
            var existingUser = await _userManager.FindByEmailAsync(email.ToLower());

            if (existingUser != null)
            {
                throw new InvalidOperationException($"User {email} already exists");
            }

            var invitation = new Invitation
            {
                Id = Guid.NewGuid(),
                Email = email.ToLower(),
                InvitedByUserId = invitedByUserId,
                InvitedAt = DateTime.UtcNow,
                IsAccepted = false,
                IsRevoked = false
            };

            _context.Invitations.Add(invitation);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Invitation created for {Email} by user {InvitedBy}", email, invitedByUserId);

            return await GetInvitationDtoAsync(invitation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating invitation for email: {Email}", email);
            throw;
        }
    }

    public async Task<bool> AcceptInvitationAsync(string email, string googleId)
    {
        try
        {
            var invitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Email.ToLower() == email.ToLower()
                                     && !i.IsRevoked
                                     && !i.IsAccepted);

            if (invitation == null)
            {
                _logger.LogWarning("No valid invitation found for email: {Email}", email);
                return false;
            }

            // Mark invitation as accepted
            invitation.IsAccepted = true;
            invitation.AcceptedAt = DateTime.UtcNow;

            // Create user account
            var user = new IdentityUser
            {
                UserName = email.ToLower(),
                Email = email.ToLower(),
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return false;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Invitation accepted and user created for {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invitation for email: {Email}", email);
            return false;
        }
    }

    public async Task<List<InvitationDto>> GetPendingInvitationsAsync()
    {
        try
        {
            var invitations = await _context.Invitations
                .Include(i => i.InvitedByUser)
                .Where(i => !i.IsAccepted && !i.IsRevoked)
                .ToListAsync();

            var result = new List<InvitationDto>();
            foreach (var i in invitations)
            {
                var invitedByUser = await _userManager.FindByIdAsync(i.InvitedByUserId);
                result.Add(new InvitationDto
                {
                    Id = i.Id,
                    Email = i.Email,
                    InvitedByUserId = i.InvitedByUserId,
                    InvitedByName = invitedByUser?.UserName ?? invitedByUser?.Email ?? "Unknown",
                    InvitedAt = i.InvitedAt,
                    AcceptedAt = i.AcceptedAt,
                    IsAccepted = i.IsAccepted,
                    IsRevoked = i.IsRevoked
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending invitations");
            return new List<InvitationDto>();
        }
    }

    public async Task<bool> RevokeInvitationAsync(string email)
    {
        try
        {
            var invitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Email.ToLower() == email.ToLower() 
                                     && !i.IsRevoked 
                                     && !i.IsAccepted);

            if (invitation == null)
            {
                return false;
            }

            invitation.IsRevoked = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Invitation revoked for {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking invitation for email: {Email}", email);
            return false;
        }
    }

    private async Task<InvitationDto> GetInvitationDtoAsync(Guid invitationId)
    {
        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null)
        {
            throw new InvalidOperationException("Invitation not found");
        }

        var invitedByUser = await _userManager.FindByIdAsync(invitation.InvitedByUserId);

        return new InvitationDto
        {
            Id = invitation.Id,
            Email = invitation.Email,
            InvitedByUserId = invitation.InvitedByUserId,
            InvitedByName = invitedByUser?.UserName ?? invitedByUser?.Email ?? "Unknown",
            InvitedAt = invitation.InvitedAt,
            AcceptedAt = invitation.AcceptedAt,
            IsAccepted = invitation.IsAccepted,
            IsRevoked = invitation.IsRevoked
        };
    }
}