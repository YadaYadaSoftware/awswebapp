using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    [HttpGet("user")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        _logger.LogInformation("Get current user called for user {UserId}", userId);

        return Ok(new {
            id = userId,
            email = email,
            isAuthenticated = true
        });
    }

    [HttpGet("status")]
    [Authorize]
    public IActionResult GetAuthStatus()
    {
        var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;

        _logger.LogInformation("Get auth status called for user {UserId}", userId);

        return Ok(new {
            IsAuthenticated = !string.IsNullOrEmpty(userId),
            AuthenticationType = "JWT",
            Email = email ?? string.Empty,
            UserId = userId ?? string.Empty
        });
    }

    [HttpPost("validate")]
    [Authorize]
    public IActionResult ValidateToken()
    {
        var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        _logger.LogInformation("Token validated for user {UserId}", userId);

        return Ok(new { message = "Token is valid", userId = userId });
    }
}