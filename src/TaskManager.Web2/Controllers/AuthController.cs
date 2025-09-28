using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Web2.Services;

namespace TaskManager.Web2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<IdentityUser> userManager,
        JwtTokenService jwtTokenService,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    [HttpGet("token")]
    [Authorize]
    public async Task<IActionResult> GetToken()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var token = await _jwtTokenService.GenerateTokenAsync(user);

        _logger.LogInformation("JWT token generated for user {UserId}", user.Id);

        return Ok(new
        {
            token = token,
            expiresIn = 60, // minutes
            user = new
            {
                id = user.Id,
                email = user.Email,
                userName = user.UserName
            }
        });
    }
}