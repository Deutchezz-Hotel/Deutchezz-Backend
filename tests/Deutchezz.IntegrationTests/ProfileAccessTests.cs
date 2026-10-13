using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ProfileAccessTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string StaffPassword = "Reception-Test-123!";

    [Fact]
    public async Task Receptionist_can_read_and_update_own_profile()
    {
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        var email = $"reception-profile-{Guid.NewGuid():N}@example.com";
        var staffId = await CreateStaffAsync(adminClient, email, "Receptionist");

        var staff = await LoginAsync(email, StaffPassword);
        using var client = CreateClientWithToken(staff.AccessToken);

        var get = await client.GetAsync("/api/v1/me/profile");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var profile = await ReadJsonAsync(get);
        Assert.Equal(email, profile.GetProperty("email").GetString());
        Assert.Equal("Front", profile.GetProperty("firstName").GetString());

        var put = await client.PutAsJsonAsync("/api/v1/me/profile", new
        {
            firstName = "Renamed",
            lastName = "Receptionist",
            phoneNumber = UniquePhone()
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await ReadJsonAsync(put);
        Assert.Equal("Renamed", updated.GetProperty("firstName").GetString());
        Assert.Equal("Receptionist", updated.GetProperty("lastName").GetString());

        // The change lands on the owner's own account only.
        var again = await ReadJsonAsync(await client.GetAsync("/api/v1/me/profile"));
        Assert.Equal("Renamed", again.GetProperty("firstName").GetString());
        Assert.Equal(email, again.GetProperty("email").GetString());

        var me = await ReadJsonAsync(await client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(staffId, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Administrator_can_read_and_update_own_profile()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var get = await client.GetAsync("/api/v1/me/profile");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(AdminEmail, (await ReadJsonAsync(get)).GetProperty("email").GetString());

        var put = await client.PutAsJsonAsync("/api/v1/me/profile", new
        {
            firstName = "Owner",
            lastName = "Admin"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Owner", (await ReadJsonAsync(put)).GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Profile_update_rejects_an_email_in_the_body()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClientWithToken(customer.AccessToken);

        var response = await client.PutAsJsonAsync("/api/v1/me/profile", new
        {
            firstName = "No",
            email = "changed-address@example.com"
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "profile.email_readonly");
    }

    [Fact]
    public async Task Staff_edit_endpoint_is_gone()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var email = $"reception-gone-{Guid.NewGuid():N}@example.com";
        var staffId = await CreateStaffAsync(client, email, "Receptionist");

        var put = await client.PutAsJsonAsync($"/api/v1/admin/staff/{staffId}", new { firstName = "Hacked" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Profile_update_records_an_activity_log_with_owner_as_actor_and_target()
    {
        var customer = await RegisterCustomerAsync();
        using var owner = CreateClientWithToken(customer.AccessToken);
        var userId = (await ReadJsonAsync(await owner.GetAsync("/api/v1/auth/me"))).GetProperty("id").GetGuid();

        var update = await owner.PutAsJsonAsync("/api/v1/me/profile", new { firstName = "Logged" });
        update.EnsureSuccessStatusCode();

        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);
        var response = await adminClient.GetAsync(
            $"/api/v1/admin/activity-logs?action=profile.updated&userId={userId}&pageSize=100");
        response.EnsureSuccessStatusCode();
        var logs = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();

        var entry = Assert.Single(logs);
        Assert.Equal(userId, entry.GetProperty("userId").GetGuid());
        Assert.Equal(userId, entry.GetProperty("entityId").GetGuid());
        Assert.Equal("User", entry.GetProperty("entityType").GetString());
    }

    [Fact]
    public async Task Customer_only_me_routes_reject_staff()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me/notifications")).StatusCode);
    }

    [Fact]
    public async Task Profile_requires_authentication()
    {
        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/profile")).StatusCode);
    }

    private async Task<Guid> CreateStaffAsync(HttpClient adminClient, string email, string role)
    {
        var response = await adminClient.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Front",
            lastName = "Desk",
            email,
            password = StaffPassword,
            role,
            phoneNumber = UniquePhone()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<LoginResult> LoginAsync(string email, string password)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>()
            ?? throw new InvalidOperationException("Empty login response");
    }
}
