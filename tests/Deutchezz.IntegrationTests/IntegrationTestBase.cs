using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deutchezz.IntegrationTests.TestFakes;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// Base class for integration tests: exposes the shared host plus helpers for authenticated
/// requests and RFC 7807 problem assertions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTestBase(HotelWebApplicationFactory factory)
{
    protected const string AdminEmail = "admin@deutchezz.test";
    protected const string AdminPassword = "Admin-Test-123!";

    protected HotelWebApplicationFactory Factory { get; } = factory;

    protected HttpClient CreateClient() => Factory.CreateClient();

    protected FakeClock Clock => Factory.Clock;

    protected FakeEmailSender EmailSender => Factory.EmailSender;

    protected HttpClient CreateClientWithToken(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected async Task<LoginResult> RegisterCustomerAsync()
    {
        var email = $"customer-{Guid.NewGuid():N}@example.com";
        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Test",
            lastName = "Customer",
            email,
            password = "Customer-Test-123!",
            phoneNumber = "08010000000"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResult>()
            ?? throw new InvalidOperationException("Empty login response");
        return body with { Email = email };
    }

    protected async Task<LoginResult> LoginAsAdminAsync()
    {
        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AdminEmail,
            password = AdminPassword
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>()
            ?? throw new InvalidOperationException("Empty login response");
    }

    protected async Task<Guid> GetRoomTypeIdAsync(HttpClient client, string name)
    {
        var response = await client.GetAsync("/api/v1/room-types?pageSize=100");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            if (string.Equals(item.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                return item.GetProperty("id").GetGuid();
        }

        throw new InvalidOperationException($"Seeded room type '{name}' was not found.");
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected static async Task AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.TryGetProperty("code", out var codeElement),
            $"Expected a 'code' extension on the problem details: {problem}");
        Assert.Equal(code, codeElement.GetString());
    }

    public record LoginResult(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, string Role)
    {
        public string Email { get; init; } = string.Empty;
    }
}
