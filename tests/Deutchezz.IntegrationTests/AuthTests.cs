using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AuthTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_issues_customer_token()
    {
        using var client = CreateClient();
        var email = $"customer-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = "Customer-Test-123!",
            phoneNumber = "08010000000"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Customer", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Register_sends_verification_email()
    {
        using var client = CreateClient();
        var email = $"verify-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = "Customer-Test-123!",
            phoneNumber = "08010000000"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var verification = EmailSender.Sent.FirstOrDefault(e => e.To == email);
        Assert.NotNull(verification);
        Assert.Contains("confirm", verification!.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/verify-email", verification.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_problem_details()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = customer.Email,
            password = "Wrong-Password-123!"
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Register_with_existing_email_returns_conflict()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email = customer.Email,
            password = "Customer-Test-123!",
            phoneNumber = "08010000000"
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "auth.email_in_use");
    }

    [Fact]
    public async Task Me_without_token_returns_unauthorized_problem()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_with_token_returns_current_user()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClientWithToken(customer.AccessToken);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(customer.Email, body.GetProperty("email").GetString());
        Assert.Equal("Customer", body.GetProperty("role").GetString());
    }
}
