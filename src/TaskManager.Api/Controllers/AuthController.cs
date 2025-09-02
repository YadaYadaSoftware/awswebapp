using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
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

        // Here you would typically:
        // 1. Extract user information from claims
        // 2. Create or update user in database
        // 3. Set up user session

        var claims = result.Principal?.Claims;
        var userInfo = new
        {
            Name = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value,
            Email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value,
            GoogleId = claims?.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
        };

        // For now, just return user info
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
    public IActionResult GetAuthStatus()
    {
        return Ok(new
        {
            IsAuthenticated = HttpContext.User.Identity?.IsAuthenticated ?? false,
            AuthenticationType = HttpContext.User.Identity?.AuthenticationType
        });
    }
}