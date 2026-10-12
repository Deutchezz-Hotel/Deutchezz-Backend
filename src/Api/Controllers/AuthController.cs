using Deutchezz.Application.Auth;
using Deutchezz.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [EnableRateLimiting("otp")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : result.Error.ToActionResult();
    }

    [EnableRateLimiting("otp")]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.VerifyEmailAsync(request, cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting("otp")]
    [HttpPost("resend-code")]
    public async Task<IActionResult> ResendCode(ResendCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ResendCodeAsync(request, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : result.Error.ToActionResult();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken, cancellationToken);
        return result.ToActionResult();
    }

    [EnableRateLimiting("otp")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ForgotPasswordAsync(request, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : result.Error.ToActionResult();
    }

    [EnableRateLimiting("otp")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ResetPasswordAsync(request, cancellationToken);
        return result.ToActionResult();
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Unauthorized();
        var result = await authService.LogoutAsync(userId.Value, cancellationToken);
        return result.ToActionResult();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        if (userId is null)
            return Unauthorized();
        var result = await authService.GetCurrentUserAsync(userId.Value, cancellationToken);
        return result.ToActionResult();
    }
}
