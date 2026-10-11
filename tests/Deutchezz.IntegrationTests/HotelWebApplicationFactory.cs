using System.Text.Json;
using Deutchezz.Application.Abstractions;
using Deutchezz.IntegrationTests.TestFakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// Test host for the Deutchezz API. Swaps the real clock and email sender for fakes and
/// points the database at the local integration database (deutchezz_test) so the developer
/// database is never touched by tests.
/// </summary>
public class HotelWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string UserSecretsId = "cdaa8b81-1b62-4af7-8482-1a99c82496ca";

    // 64 ASCII chars => 64 bytes, satisfies the >= 32 byte Jwt:Key validation.
    private const string TestJwtKey =
        "integration-tests-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz-ABCD";

    public FakeEmailSender EmailSender { get; } = new();

    public FakeClock Clock { get; } = new(new DateOnly(2026, 10, 10));

    public string ConnectionString { get; } = ResolveTestConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Key", TestJwtKey);

        builder.UseSetting("Seed:AdminEmail", "admin@deutchezz.test");
        builder.UseSetting("Seed:AdminPassword", "Admin-Test-123!");

        // Integration tests share a single loopback address, so the per-IP fixed windows from
        // production would throttle the suite. Raise them so tests exercise behaviour, not limits.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Lookup:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:BookingCreate:PermitLimit", "100000");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            services.RemoveAll<IBusinessClock>();
            services.AddSingleton<IBusinessClock>(Clock);
        });
    }

    /// <summary>
    /// Reuses the developer's configured connection string (user secrets / environment) but
    /// redirects it at a dedicated integration database. Falls back to localhost defaults when
    /// no developer connection string is present (for example on a CI agent).
    /// </summary>
    private static string ResolveTestConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("DEUTCHEZZ_TEST_CONNECTION") ?? ReadUserSecret();

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = 5432,
            Username = "postgres",
            Password = "postgres",
            Database = "deutchezz_test"
        };

        if (!string.IsNullOrWhiteSpace(configured))
        {
            builder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = "deutchezz_test"
            };
        }

        return builder.ConnectionString;
    }

    private static string? ReadUserSecret()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", UserSecretsId, "secrets.json");

        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var value)
            ? value.GetString()
            : null;
    }
}
