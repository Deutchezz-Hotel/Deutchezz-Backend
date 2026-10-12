using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AuthTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Password = "Customer-Test-123!";
    private const string NewPassword = "New-Customer-456!";

    [Fact]
    public async Task Register_returns_accepted_neutral_body_and_sends_code_without_creating_account()
    {
        using var client = CreateClient();
        var email = $"register-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Contains("verification code", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(60, body.GetProperty("resendAvailableInSeconds").GetInt32());

        var verification = EmailSender.LatestFor(email);
        Assert.NotNull(verification);
        Assert.Contains("code", verification!.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(EmailSender.CodeFor(email));

        // No account exists yet, so login must fail with the generic credentials error.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        await AssertProblemAsync(login, (int)HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Verify_email_creates_customer_account_and_returns_tokens()
    {
        using var client = CreateClient();
        var email = $"verify-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        var code = EmailSender.CodeFor(email);
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var body = await ReadJsonAsync(verify);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Customer", body.GetProperty("role").GetString());

        // The pending registration is gone: replaying the same code fails generically.
        var replay = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });
        await AssertProblemAsync(replay, (int)HttpStatusCode.BadRequest, "auth.invalid_or_expired_code");
    }

    [Fact]
    public async Task Register_with_existing_email_returns_neutral_body_and_notifies_owner()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email = customer.Email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Contains("verification code", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        var notice = EmailSender.LatestFor(customer.Email);
        Assert.NotNull(notice);
        Assert.Contains("already", notice!.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_with_existing_phone_returns_neutral_body_and_creates_nothing()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClient();
        var newEmail = $"phone-clash-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email = newEmail,
            password = Password,
            phoneNumber = customer.Phone
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Null(EmailSender.CodeFor(newEmail));
    }

    [Fact]
    public async Task Phone_number_formats_collide_under_normalization()
    {
        using var client = CreateClient();
        var emailOne = $"phone-one-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email = emailOne,
            password = Password,
            phoneNumber = "09133554267"
        });
        var code = EmailSender.CodeFor(emailOne);
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email = emailOne, code });
        verify.EnsureSuccessStatusCode();

        var emailTwo = $"phone-two-{Guid.NewGuid():N}@example.com";
        var second = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email = emailTwo,
            password = Password,
            phoneNumber = "+234 913 355 4267"
        });

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Null(EmailSender.CodeFor(emailTwo));
    }

    [Fact]
    public async Task Five_wrong_codes_invalidate_the_pending_registration()
    {
        using var client = CreateClient();
        var email = $"lockout-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        var code = EmailSender.CodeFor(email)!;
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code = wrong });
            await AssertProblemAsync(attempt, (int)HttpStatusCode.BadRequest, "auth.invalid_or_expired_code");
        }

        var correct = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });
        await AssertProblemAsync(correct, (int)HttpStatusCode.BadRequest, "auth.invalid_or_expired_code");
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        using var client = CreateClient();
        var email = $"expired-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        var code = EmailSender.CodeFor(email)!;
        Clock.Advance(TimeSpan.FromMinutes(16));

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });
        await AssertProblemAsync(verify, (int)HttpStatusCode.BadRequest, "auth.invalid_or_expired_code");
    }

    [Fact]
    public async Task Resend_respects_cooldown_then_issues_a_new_code()
    {
        using var client = CreateClient();
        var email = $"resend-{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Ada",
            lastName = "Obi",
            email,
            password = Password,
            phoneNumber = UniquePhone()
        });

        var sendsAfterRegister = EmailSender.Sent.Count(e => e.To == email);
        var firstCode = EmailSender.CodeFor(email)!;

        // Within the cooldown window nothing new is sent, but the response stays neutral.
        var throttled = await client.PostAsJsonAsync("/api/v1/auth/resend-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, throttled.StatusCode);
        Assert.Equal(sendsAfterRegister, EmailSender.Sent.Count(e => e.To == email));

        Clock.Advance(TimeSpan.FromSeconds(61));

        var resent = await client.PostAsJsonAsync("/api/v1/auth/resend-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        Assert.Equal(sendsAfterRegister + 1, EmailSender.Sent.Count(e => e.To == email));

        var newCode = EmailSender.CodeFor(email)!;
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code = newCode });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.NotEqual(firstCode, newCode);
    }

    [Fact]
    public async Task Forgot_and_reset_password_by_code_changes_the_password()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClient();

        var forgot = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = customer.Email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

        var code = EmailSender.CodeFor(customer.Email);
        Assert.NotNull(code);

        var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email = customer.Email, code, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var oldLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = customer.Email, password = Password });
        await AssertProblemAsync(oldLogin, (int)HttpStatusCode.Unauthorized, "auth.invalid_credentials");

        var newLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = customer.Email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Login_failures_are_generic()
    {
        using var client = CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@example.com", password = Password });
        await AssertProblemAsync(unknown, (int)HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Profile_rejects_a_phone_number_owned_by_another_account()
    {
        var owner = await RegisterCustomerAsync();
        var other = await RegisterCustomerAsync();

        using var client = CreateClientWithToken(other.AccessToken);
        var update = await client.PutAsJsonAsync("/api/v1/me/profile", new { phoneNumber = owner.Phone });
        await AssertProblemAsync(update, (int)HttpStatusCode.BadRequest, "auth.phone_in_use");
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
