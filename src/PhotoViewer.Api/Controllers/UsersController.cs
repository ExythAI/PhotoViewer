using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoViewer.Api.Models;
using PhotoViewer.Api.Services;

namespace PhotoViewer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;

    public UsersController(AuthService authService)
    {
        _authService = authService;
    }

    public record CreateUserRequest(string Username, string Password, string Role = "User");
    public record UserDto(int Id, string Username, string Role, DateTime CreatedAt);

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _authService.GetUsersAsync();
        return Ok(users.Select(u => new UserDto(u.Id, u.Username, u.Role.ToString(), u.CreatedAt)));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Username and password are required" });

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            role = UserRole.User;

        var user = await _authService.CreateUserAsync(request.Username, request.Password, role);
        if (user == null)
            return Conflict(new { message = "Username already exists" });

        return Ok(new UserDto(user.Id, user.Username, user.Role.ToString(), user.CreatedAt));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (id == 1)
            return BadRequest(new { message = "Cannot delete the default admin user" });

        var success = await _authService.DeleteUserAsync(id);
        if (!success) return NotFound();

        return Ok(new { message = "User deleted" });
    }
}
