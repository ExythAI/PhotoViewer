using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoViewer.Api.Models;
using PhotoViewer.Api.Services;

namespace PhotoViewer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly LoginRateLimiter _rateLimiter;

    public AuthController(AuthService authService, LoginRateLimiter rateLimiter)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
    }

    public record LoginRequest(string Username, string Password);
    public record LoginResponse(string Token, string Username, string Role);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (_rateLimiter.IsBlocked(ip))
            return StatusCode(429, new { message = "Too many login attempts. Try again in 1 minute." });

        var (token, user) = await _authService.LoginAsync(request.Username, request.Password);
        if (token == null || user == null)
        {
            _rateLimiter.RecordAttempt(ip);
            return Unauthorized(new { message = "Invalid username or password" });
        }

        _rateLimiter.ClearAttempts(ip);
        return Ok(new LoginResponse(token, user.Username, user.Role.ToString()));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest(new { message = "New password must be at least 8 characters" });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var success = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

        if (!success)
            return BadRequest(new { message = "Current password is incorrect" });

        return Ok(new { message = "Password changed successfully" });
    }
}
