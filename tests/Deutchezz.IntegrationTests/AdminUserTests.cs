using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AdminUserTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<Guid> GetUserIdAsync(HttpClient client)
    {
        var me = await ReadJsonAsync(await client.GetAsync("/api/v1/auth/me"));
        return me.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ListUsersAsync(HttpClient admin, string query = "")
    {
        var response = await admin.GetAsync($"/api/v1/admin/users{query}");
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    [Fact]
    public async Task Users_list_defaults_to_customers_and_excludes_staff()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var body = await ListUsersAsync(client, "?pageSize=100");
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.All(items, item => Assert.Equal("Customer", item.GetProperty("role").GetString()));
        Assert.Contains(items, item => item.GetProperty("email").GetString() == customer.Email);

        // The seeded administrator must never appear in the default (customer) list.
        Assert.DoesNotContain(items, item => item.GetProperty("role").GetString() == "Administrator");
    }

    [Fact]
    public async Task Users_list_can_search_by_email_and_name()
    {
        var email = $"user-search-{Guid.NewGuid():N}@example.com";
        var customer = await RegisterCustomerAsync(email: email, firstName: "Zeb", lastName: "Quillon");
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var byEmail = await ListUsersAsync(client, $"?search={Uri.EscapeDataString(email)}&pageSize=100");
        var emailItems = byEmail.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(emailItems);
        Assert.Equal(customer.Email, emailItems[0].GetProperty("email").GetString());

        var byName = await ListUsersAsync(client, "?search=Quillon&pageSize=100");
        Assert.Contains(byName.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("email").GetString() == customer.Email);
    }

    [Fact]
    public async Task Users_list_exposes_required_fields()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var body = await ListUsersAsync(client, $"?search={Uri.EscapeDataString(customer.Email)}");
        var item = body.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(customer.Email, item.GetProperty("email").GetString());
        Assert.Equal("Customer", item.GetProperty("role").GetString());
        Assert.True(item.GetProperty("isActive").GetBoolean());
        Assert.True(item.GetProperty("emailConfirmed").GetBoolean());
        Assert.True(item.TryGetProperty("bookingsCount", out _));
        Assert.True(item.TryGetProperty("createdAt", out _));
        Assert.True(item.TryGetProperty("lastLoginAt", out _));
        Assert.True(item.TryGetProperty("phoneNumber", out _));
        Assert.True(item.TryGetProperty("firstName", out _));
        Assert.True(item.TryGetProperty("lastName", out _));
    }

    [Fact]
    public async Task Staff_list_excludes_customers_and_every_row_has_a_staff_role()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync("/api/v1/admin/staff?pageSize=100");
        response.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();

        Assert.DoesNotContain(items, item => item.GetProperty("email").GetString() == customer.Email);
        Assert.All(items, item =>
        {
            var role = item.GetProperty("role").GetString();
            Assert.True(role is "Receptionist" or "Administrator", $"Unexpected staff role '{role}'.");
        });
    }

    [Fact]
    public async Task Promoting_a_customer_moves_them_from_users_to_staff()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var userId = await GetUserIdAsync(customerClient);

        var change = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role",
            new { role = "Receptionist" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal("Receptionist", (await ReadJsonAsync(change)).GetProperty("role").GetString());

        var users = await ListUsersAsync(adminClient, "?pageSize=100");
        Assert.DoesNotContain(users.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == userId);

        var staff = await adminClient.GetAsync("/api/v1/admin/staff?pageSize=100");
        Assert.Contains((await ReadJsonAsync(staff)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == userId);
    }

    [Fact]
    public async Task Demoting_a_staff_member_returns_them_to_the_customer_list()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var create = await client.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Temp",
            lastName = "Staff",
            email = $"temp-staff-{Guid.NewGuid():N}@example.com",
            password = "Reception-Test-123!",
            role = "Receptionist",
            phoneNumber = UniquePhone()
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var staffId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var demote = await client.PatchAsJsonAsync($"/api/v1/admin/users/{staffId}/role",
            new { role = "Customer" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Equal("Customer", (await ReadJsonAsync(demote)).GetProperty("role").GetString());

        var users = await ListUsersAsync(client, "?pageSize=100");
        Assert.Contains(users.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == staffId);

        var staff = await client.GetAsync("/api/v1/admin/staff?pageSize=100");
        Assert.DoesNotContain((await ReadJsonAsync(staff)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == staffId);
    }

    [Fact]
    public async Task Changing_a_role_revokes_refresh_tokens()
    {
        var email = $"revoke-{Guid.NewGuid():N}@example.com";
        var customer = await RegisterCustomerAsync(email: email);
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var userId = await GetUserIdAsync(customerClient);

        // The freshly issued refresh token works before the role change.
        var before = await customerClient.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = customer.RefreshToken });
        before.EnsureSuccessStatusCode();

        var change = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role",
            new { role = "Receptionist" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // The original refresh token is now revoked.
        using var anon = CreateClient();
        var after = await anon.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = customer.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Administrator_cannot_change_own_role()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);
        var selfId = await GetUserIdAsync(client);

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/users/{selfId}/role",
            new { role = "Customer" });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "user.self_role_change");
    }

    [Fact]
    public async Task Inactive_user_cannot_be_promoted()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var userId = await GetUserIdAsync(customerClient);

        var deactivate = await client.PatchAsJsonAsync($"/api/v1/admin/staff/{userId}/status",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var promote = await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role",
            new { role = "Receptionist" });

        await AssertProblemAsync(promote, (int)HttpStatusCode.Conflict, "user.inactive");
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_deactivated()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);
        var selfId = await GetUserIdAsync(client);

        // Normalise: deactivate every other active administrator so the caller is the last one.
        var admins = await ListUsersAsync(client, "?role=Administrator&pageSize=100");
        foreach (var row in admins.GetProperty("items").EnumerateArray())
        {
            var id = row.GetProperty("id").GetGuid();
            if (id == selfId || !row.GetProperty("isActive").GetBoolean())
                continue;

            var deactivateOther = await client.PatchAsJsonAsync($"/api/v1/admin/staff/{id}/status",
                new { isActive = false });
            Assert.Equal(HttpStatusCode.NoContent, deactivateOther.StatusCode);
        }

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/staff/{selfId}/status",
            new { isActive = false });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "user.last_administrator");
    }

    [Fact]
    public async Task Changing_a_role_writes_an_activity_log_and_emails_the_user()
    {
        var email = $"audited-{Guid.NewGuid():N}@example.com";
        var customer = await RegisterCustomerAsync(email: email);
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var userId = await GetUserIdAsync(customerClient);

        var change = await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role",
            new { role = "Administrator" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var logs = await client.GetAsync(
            $"/api/v1/admin/activity-logs?action=user.role_changed&entityType=User&pageSize=100");
        logs.EnsureSuccessStatusCode();
        Assert.Contains((await ReadJsonAsync(logs)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("entityId").GetGuid() == userId);

        var notice = EmailSender.LatestFor(email);
        Assert.NotNull(notice);
        Assert.Contains("role", notice!.Subject, StringComparison.OrdinalIgnoreCase);

        // Clean up so the suite never accumulates extra administrators.
        await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role", new { role = "Customer" });
    }

    [Fact]
    public async Task Changing_an_unknown_user_role_returns_not_found()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/role",
            new { role = "Receptionist" });

        await AssertProblemAsync(response, (int)HttpStatusCode.NotFound, "user.not_found");
    }

    [Fact]
    public async Task Invalid_role_is_rejected()
    {
        var customer = await RegisterCustomerAsync();
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var userId = await GetUserIdAsync(customerClient);

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/users/{userId}/role",
            new { role = "Wizard" });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "user.invalid_role");
    }

    [Fact]
    public async Task Receptionist_cannot_access_admin_users()
    {
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        var create = await adminClient.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Recep",
            lastName = "Only",
            email = $"recep-only-{Guid.NewGuid():N}@example.com",
            password = "Reception-Test-123!",
            role = "Receptionist",
            phoneNumber = UniquePhone()
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var loginClient = CreateClient();
        var login = await loginClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = (await ReadJsonAsync(create)).GetProperty("email").GetString(),
            password = "Reception-Test-123!"
        });
        login.EnsureSuccessStatusCode();
        var token = (await ReadJsonAsync(login)).GetProperty("accessToken").GetString()!;

        using var client = CreateClientWithToken(token);
        var list = await client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var change = await client.PatchAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/role",
            new { role = "Administrator" });
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
    }
}
