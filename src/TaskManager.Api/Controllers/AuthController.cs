using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("login")]
    public IActionResult Login()
    {
        _logger.LogInformation("Login endpoint called - authentication disabled");
        return Ok(new { message = "Authentication is disabled. API runs anonymously." });
    }

    [HttpGet("login-callback")]
    public IActionResult LoginCallback()
    {
        _logger.LogInformation("Login callback called - authentication disabled");
        return Ok(new { message = "Authentication is disabled. Redirect to home." });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _logger.LogInformation("Logout endpoint called - authentication disabled");
        return Ok(new { message = "Logout completed. API runs anonymously." });
    }

    [HttpGet("user")]
    public IActionResult GetCurrentUser()
    {
        _logger.LogInformation("Get current user called - authentication disabled");
        return Ok(new { message = "No user authentication. API runs anonymously." });
    }

    [HttpGet("status")]
    public IActionResult GetAuthStatus()
    {
        _logger.LogInformation("Get auth status called - authentication disabled");
        return Ok(new {
            IsAuthenticated = false,
            AuthenticationType = "None",
            Email = string.Empty,
            IsInvited = false,
            message = "Authentication is disabled. API runs anonymously."
        });
    }
}