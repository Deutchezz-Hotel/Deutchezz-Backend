using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Profile;

public record UpdateProfileRequest(string? FullName, string? Phone);

public record ProfileResponse(Guid Id, string FullName, string Email, string? Phone);