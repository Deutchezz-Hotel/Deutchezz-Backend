namespace Deutchezz.Application.Common.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string DefaultConnection { get; set; } = string.Empty;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public string Key { get; set; } = string.Empty;
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;

    public bool HasAdminCredentials =>
        !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword);
}

public sealed class CancellationOptions
{
    public const string SectionName = "Cancellation";

    public int WindowHours { get; set; } = 24;
}

public sealed class BookingOptions
{
    public const string SectionName = "Booking";

    /// <summary>Minutes after a booking is created during which a second booking for the same
    /// guest triggers the duplicate-booking warning (v1.10, 7m, BR-041).</summary>
    public int DuplicateWarningMinutes { get; set; } = 30;
}

public sealed class SettingsOptions
{
    public const string SectionName = "Settings";

    public int MaxRoomsPerBooking { get; set; } = 20;
    public string CheckInTime { get; set; } = "14:00";
    public string CheckOutTime { get; set; } = "12:00";
    public bool RequireFullPaymentAtCheckIn { get; set; }
    public bool RequirePaymentBeforeCheckOut { get; set; }
    public bool EarlyCheckOutRequiresAdmin { get; set; }

    // v1.12 stay policy (time-aware stays).
    public string DefaultCheckInTime { get; set; } = "14:00";
    public string DefaultCheckOutTime { get; set; } = "12:00";
    public int TimeSlotMinutes { get; set; } = 30;
    public string EarliestCheckInTime { get; set; } = "00:00";
    public string LatestCheckInTime { get; set; } = "23:30";
    public string EarliestCheckOutTime { get; set; } = "00:00";
    public string LatestCheckOutTime { get; set; } = "23:30";
    public int TurnaroundMinutes { get; set; }

    // v1.14 duration-based late-check-out pricing.
    public int MaxStayNights { get; set; } = 30;
    public int GraceHours { get; set; } = 2;

    // v1.16 check-out reminders: email the front desk shortly before a checked-in stay ends.
    /// <summary>Minutes before a checked-in booking's scheduled check-out to email the desk.</summary>
    public int CheckOutReminderLeadMinutes { get; set; } = 30;

    /// <summary>
    /// Comma/semicolon-separated staff inboxes for check-out reminders. Empty falls back to
    /// <see cref="EmailOptions.AdminNotificationAddress"/>.
    /// </summary>
    public string ReceptionNotificationEmails { get; set; } = string.Empty;
}

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public int PendingExpiryIntervalMinutes { get; set; } = 15;
    public int VerificationCleanupIntervalMinutes { get; set; } = 60;
    public int CheckOutReminderIntervalMinutes { get; set; } = 5;
}

/// <summary>
/// How long cancelled bookings are retained before they are permanently removed (SoT 7n / BR-042).
/// Retention is measured from <c>CancelledAt</c>.
/// </summary>
public sealed class BookingRetentionOptions
{
    public const string SectionName = "BookingRetention";

    /// <summary>Master switch for the hourly purge.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long a cancelled booking is retained before it is purged.</summary>
    public int CancelledDays { get; set; } = 30;

    /// <summary>How often the purge cycle runs.</summary>
    public int IntervalMinutes { get; set; } = 60;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Minutes a 6-digit verification/reset code stays valid.</summary>
    public int CodeMinutes { get; set; } = 15;

    /// <summary>Seconds a user must wait before a code can be resent.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>Maximum number of codes that may be sent to one address per hour.</summary>
    public int MaxSendsPerHour { get; set; } = 5;

    /// <summary>How long an unverified pending registration is kept before cleanup.</summary>
    public int PendingRegistrationHours { get; set; } = 24;

    /// <summary>Failed code guesses allowed before the code is invalidated.</summary>
    public int MaxFailedAttempts { get; set; } = 5;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>HMAC key used to hash verification/reset codes (user secrets only).</summary>
    public string OtpHmacKey { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173", "http://localhost:3000"];
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitRule Auth { get; set; } = new() { PermitLimit = 10 };
    public RateLimitRule Otp { get; set; } = new() { PermitLimit = 5 };
    public RateLimitRule Lookup { get; set; } = new() { PermitLimit = 5 };
    public RateLimitRule BookingCreate { get; set; } = new() { PermitLimit = 10 };
    public RateLimitRule Contact { get; set; } = new() { PermitLimit = 5 };
}

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Inbox that receives a staff alert for every new booking. Empty disables the alert.</summary>
    public string AdminNotificationAddress { get; set; } = string.Empty;

    /// <summary>Base URL of the web app, used to build links in emails.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>SmtpUsername and SmtpPassword are set via user secrets only.</summary>
    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "Deutchezz Hotel Ispahan";

        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }
}
