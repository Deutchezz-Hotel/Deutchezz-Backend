using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<Result<LoginResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}