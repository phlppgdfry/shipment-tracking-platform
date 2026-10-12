using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShipmentTracking.Infrastructure.Persistence;
using ShipmentTracking.Infrastructure.Services.Auth;

namespace ShipmentTracking.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    JwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (string.Equals(request.UserName, "demo-dispatcher", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { title = "This user name is reserved for trusted demo provisioning." });
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return BadRequest(new
        {
            title = "Registratie mislukt.",
            errors = result.Errors.ToDictionary(x => x.Code, x => x.Description)
        });

        await users.AddToRoleAsync(user, "Customer");
        return StatusCode(StatusCodes.Status201Created, new AuthResponse(tokens.GenerateToken(user.Id, user.UserName!, ["Customer"], user.CustomerId), user.UserName!, user.CustomerId, ["Customer"]));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByNameAsync(request.UserName);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password)) return Unauthorized();
        var roles = await users.GetRolesAsync(user);
        return Ok(new AuthResponse(tokens.GenerateToken(user.Id, user.UserName!, roles, user.CustomerId), user.UserName!, user.CustomerId, roles.ToArray()));
    }
}

public sealed record RegisterRequest(string UserName, string Email, string Password);
public sealed record LoginRequest(string UserName, string Password);
public sealed record AuthResponse(string AccessToken, string? UserName = null, string? CustomerId = null, IReadOnlyList<string>? Roles = null);
