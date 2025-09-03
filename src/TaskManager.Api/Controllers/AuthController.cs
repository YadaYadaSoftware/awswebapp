using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IInvitationService _invitationService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IInvitationService invitationService, ILogger<AuthController> logger)
    {
        _invitationService = invitationService;
        _logger = logger;
    }
    [HttpGet("login")]
    public IActionResult Login(string returnUrl = "/")
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(LoginCallback), new { returnUrl })
        };
        return Challenge(properties, "Google");
    }

    [HttpGet("login-callback")]
    public async Task<IActionResult> LoginCallback(string returnUrl = "/")
    {
        var result = await HttpContext.AuthenticateAsync("Cookies");
        if (!result.Succeeded)
        {
            return BadRequest("Authentication failed");
        }

        var claims = result.Principal?.Claims;
        var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
        var googleId = claims?.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        var name = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("No email claim found in Google OAuth response");
            return BadRequest("Email not provided by Google OAuth");
        }

        // Check if user is invited
        var isInvited = await _invitationService.IsUserInvitedAsync(email);
        if (!isInvited)
        {
            _logger.LogWarning("Unauthorized login attempt from non-invited user: {Email}", email);
            
            // Sign out the user
            await HttpContext.SignOutAsync("Cookies");
            
            return Unauthorized(new {
                error = "Access denied",
                message = "You must be invited to access this application. Please contact an administrator."
            });
        }

        // Accept invitation if this is first login
        if (!string.IsNullOrEmpty(googleId))
        {
            await _invitationService.AcceptInvitationAsync(email, googleId);
        }

        var userInfo = new
        {
            Name = name,
            Email = email,
            GoogleId = googleId,
            IsAuthorized = true
        };

        _logger.LogInformation("Successful authorized login for invited user: {Email}", email);
        return Ok(userInfo);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookies");
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("user")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var claims = HttpContext.User.Claims;
        var userInfo = new
        {
            Name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value,
            Email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value,
            GoogleId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value,
            IsAuthenticated = HttpContext.User.Identity?.IsAuthenticated ?? false
        };

        return Ok(userInfo);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetAuthStatus()
    {
        var email = HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        var isInvited = !string.IsNullOrEmpty(email) && await _invitationService.IsUserInvitedAsync(email);
        
        return Ok(new
        {
            IsAuthenticated = HttpContext.User.Identity?.IsAuthenticated ?? false,
            AuthenticationType = HttpContext.User.Identity?.AuthenticationType,
            Email = email,
            IsInvited = isInvited
        });
    }
}