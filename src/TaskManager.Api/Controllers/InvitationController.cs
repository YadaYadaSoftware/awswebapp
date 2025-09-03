using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Api.Services;
using TaskManager.Shared.Models;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvitationController : ControllerBase
{
    private readonly IInvitationService _invitationService;
    private readonly ILogger<InvitationController> _logger;

    public InvitationController(IInvitationService invitationService, ILogger<InvitationController> logger)
    {
        _invitationService = invitationService;
        _logger = logger;
    }

    [HttpPost("invite")]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
    {
        try
        {
            var currentUserId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized("User ID not found in claims");
            }

            var invitation = await _invitationService.CreateInvitationAsync(request.Email, currentUserId);
            
            _logger.LogInformation("User {CurrentUser} invited {Email}", currentUserId, request.Email);
            
            return Ok(new { 
                message = $"Invitation sent to {request.Email}",
                invitation = invitation
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating invitation for {Email}", request.Email);
            return StatusCode(500, new { error = "Failed to create invitation" });
        }
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingInvitations()
    {
        try
        {
            var invitations = await _invitationService.GetPendingInvitationsAsync();
            return Ok(invitations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending invitations");
            return StatusCode(500, new { error = "Failed to get pending invitations" });
        }
    }

    [HttpDelete("revoke/{email}")]
    public async Task<IActionResult> RevokeInvitation(string email)
    {
        try
        {
            var success = await _invitationService.RevokeInvitationAsync(email);
            
            if (!success)
            {
                return NotFound(new { error = $"No pending invitation found for {email}" });
            }

            _logger.LogInformation("Invitation revoked for {Email}", email);
            return Ok(new { message = $"Invitation revoked for {email}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking invitation for {Email}", email);
            return StatusCode(500, new { error = "Failed to revoke invitation" });
        }
    }

    [HttpGet("check/{email}")]
    public async Task<IActionResult> CheckInvitationStatus(string email)
    {
        try
        {
            var isInvited = await _invitationService.IsUserInvitedAsync(email);
            
            return Ok(new { 
                email = email,
                isInvited = isInvited,
                message = isInvited ? "User is invited" : "User is not invited"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking invitation status for {Email}", email);
            return StatusCode(500, new { error = "Failed to check invitation status" });
        }
    }
}

public class InviteUserRequest
{
    public string Email { get; set; } = string.Empty;
}