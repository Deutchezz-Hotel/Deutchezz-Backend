using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

/// <summary>
/// An unverified sign-up awaiting its emailed 6-digit code. Not an account: it is only turned
/// into a <see cref="User"/> once the code is confirmed (BR-026).
/// </summary>
public class PendingRegistration : AuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CodeExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public int SendCount { get; set; }
    public DateTime SendWindowStartAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
