using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Profile;

public record UpdateProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, string? Email = null);

public record ProfileResponse(Guid Id, string FirstName, string LastName, string Email, string? PhoneNumber);