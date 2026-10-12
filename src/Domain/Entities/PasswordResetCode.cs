using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

/// <summary>
/// One active password-reset code for a user's reset request (BR-029). Only the HMAC of the
/// 6-digit code is stored.
/// </summary>
public class PasswordResetCode : AuditableEntity
{
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public int SendCount { get; set; }
    public DateTime SendWindowStartAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime LastSentAt { get; set; }

    public User? User { get; set; }
}
