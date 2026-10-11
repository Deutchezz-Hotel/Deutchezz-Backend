using System.Text;
using Deutchezz.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace Deutchezz.Api.Extensions;

public static class OptionsExtensions
{
    public static IServiceCollection AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.DefaultConnection),
                "ConnectionStrings:DefaultConnection is missing or empty. Set it in appsettings.Development.json " +
                "or run: dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"Host=localhost;Port=5432;Database=deutchezz_dev;Username=postgres;Password=***\" --project src/Deutchezz.csproj")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is missing or empty.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is missing or empty.")
            .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be greater than 0.")
            .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than 0.")
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.Key) >= 32,
                "Jwt:Key is missing or shorter than 32 bytes. Generate one with: $b = New-Object byte[] 64; " +
                "[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); " +
                "then set it via: dotnet user-secrets set \"Jwt:Key\" ([Convert]::ToBase64String($b)) --project src/Deutchezz.csproj")
            .ValidateOnStart();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.AdminEmail) == !string.IsNullOrWhiteSpace(o.AdminPassword),
                "Seed:AdminEmail and Seed:AdminPassword must both be set (recommended: dotnet user-secrets) or both left empty.")
            .ValidateOnStart();

        services.AddOptions<CancellationOptions>()
            .Bind(configuration.GetSection(CancellationOptions.SectionName))
            .Validate(o => o.WindowHours > 0, "Cancellation:WindowHours must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<SettingsOptions>()
            .Bind(configuration.GetSection(SettingsOptions.SectionName))
            .Validate(o => o.MaxRoomsPerBooking >= 1 && o.MaxRoomsPerBooking <= 1000,
                "Settings:MaxRoomsPerBooking must be between 1 and 1000.")
            .Validate(o => TimeOnly.TryParse(o.CheckInTime, out _), "Settings:CheckInTime must be a time such as 14:00.")
            .Validate(o => TimeOnly.TryParse(o.CheckOutTime, out _), "Settings:CheckOutTime must be a time such as 12:00.")
            .ValidateOnStart();

        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName))
            .Validate(o => o.PendingExpiryIntervalMinutes > 0,
                "Jobs:PendingExpiryIntervalMinutes must be greater than 0.")
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .Validate(
                o => o.AllowedOrigins.Length > 0 && o.AllowedOrigins.All(origin => Uri.TryCreate(origin, UriKind.Absolute, out _)),
                "Cors:AllowedOrigins must be a non-empty list of origins such as http://localhost:5173.")
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.Auth.PermitLimit > 0 && o.Auth.WindowSeconds > 0, "RateLimiting:Auth PermitLimit/WindowSeconds must be positive.")
            .Validate(o => o.Lookup.PermitLimit > 0 && o.Lookup.WindowSeconds > 0, "RateLimiting:Lookup PermitLimit/WindowSeconds must be positive.")
            .Validate(o => o.BookingCreate.PermitLimit > 0 && o.BookingCreate.WindowSeconds > 0, "RateLimiting:BookingCreate PermitLimit/WindowSeconds must be positive.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => o.Smtp.Port > 0 && o.Smtp.Port <= 65535, "Email:Smtp:Port must be a valid TCP port.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Forces validation of every options class registered by <see cref="AddValidatedOptions"/>.
    /// Accessing <c>IOptions&lt;T&gt;.Value</c> runs the same validators as ValidateOnStart,
    /// but immediately — before migrations/seeding instead of at host start.
    /// </summary>
    public static void ValidateRequiredOptions(this IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        _ = services.GetRequiredService<IOptions<JwtOptions>>().Value;
        _ = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        _ = services.GetRequiredService<IOptions<CancellationOptions>>().Value;
        _ = services.GetRequiredService<IOptions<SettingsOptions>>().Value;
        _ = services.GetRequiredService<IOptions<CorsOptions>>().Value;
        _ = services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        _ = services.GetRequiredService<IOptions<EmailOptions>>().Value;
        _ = services.GetRequiredService<IOptions<JobsOptions>>().Value;
    }
}
