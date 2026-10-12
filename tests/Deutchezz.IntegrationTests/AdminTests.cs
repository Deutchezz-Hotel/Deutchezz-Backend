using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AdminTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task List_rooms_returns_paged_envelope()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync("/api/v1/admin/rooms?page=1&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(5, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
        Assert.True(body.GetProperty("items").EnumerateArray().Count() <= 5);
        Assert.True(body.TryGetProperty("totalPages", out _));
    }

    [Fact]
    public async Task List_rooms_honours_sort_direction()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync("/api/v1/admin/rooms?sort=roomNumber&direction=desc&pageSize=100");
        response.EnsureSuccessStatusCode();

        var rooms = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(rooms);

        var roomNumbers = rooms.Select(r => r.GetProperty("roomNumber").GetString()!).ToList();
        Assert.Equal(roomNumbers.OrderByDescending(n => n, StringComparer.Ordinal), roomNumbers);
    }

    [Theory]
    [InlineData("/api/v1/admin/bookings")]
    [InlineData("/api/v1/admin/payments")]
    [InlineData("/api/v1/admin/staff")]
    [InlineData("/api/v1/admin/guests")]
    [InlineData("/api/v1/admin/activity-logs")]
    [InlineData("/api/v1/admin/rooms?sort=roomNumber&direction=desc")]
    [InlineData("/api/v1/admin/room-types?sort=name&direction=desc")]
    public async Task Admin_list_endpoints_return_a_paged_envelope(string url)
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out _));
        Assert.True(body.TryGetProperty("page", out _));
        Assert.True(body.TryGetProperty("pageSize", out _));
        Assert.True(body.TryGetProperty("totalCount", out _));
        Assert.True(body.TryGetProperty("totalPages", out _));
    }

    [Theory]
    [InlineData("/api/v1/admin/activity-logs?from=2020-01-01&to=2030-01-01")]
    [InlineData("/api/v1/admin/guests?from=2020-01-01&to=2030-01-01")]
    public async Task Admin_date_series_reports_accept_a_date_filter(string url)
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_access_admin_endpoints()
    {
        var customer = await RegisterCustomerAsync();
        using var client = CreateClientWithToken(customer.AccessToken);

        var response = await client.GetAsync("/api/v1/admin/rooms");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Anonymous_cannot_access_admin_endpoints()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/admin/rooms");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Settings_can_be_read_and_updated()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var initial = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.True((await ReadJsonAsync(initial)).TryGetProperty("maxRoomsPerBooking", out _));

        var update = await client.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            maxRoomsPerBooking = 7,
            checkInTime = "15:00"
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJsonAsync(update);
        Assert.Equal(7, updated.GetProperty("maxRoomsPerBooking").GetInt32());
        Assert.Equal("15:00", updated.GetProperty("checkInTime").GetString());
    }

    [Fact]
    public async Task Dashboard_returns_room_counts()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync("/api/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("totalRooms").GetInt32() >= 1);
        Assert.True(body.TryGetProperty("availableRooms", out _));
        Assert.True(body.TryGetProperty("occupancyPercentage", out _));
    }

    [Fact]
    public async Task Created_staff_can_log_in_with_their_role()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);
        var email = $"receptionist-{Guid.NewGuid():N}@example.com";
        const string password = "Reception-Test-123!";

        var create = await client.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Recep",
            lastName = "Tionist",
            email,
            password,
            role = "Receptionist",
            phoneNumber = UniquePhone()
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        Assert.Equal("Receptionist", created.GetProperty("role").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        using var loginClient = CreateClient();
        var login = await loginClient.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("Receptionist", (await ReadJsonAsync(login)).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Creating_staff_with_a_phone_number_already_in_use_is_rejected()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);
        var phone = UniquePhone();

        var first = await client.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Recep",
            lastName = "One",
            email = $"staff-one-{Guid.NewGuid():N}@example.com",
            password = "Reception-Test-123!",
            role = "Receptionist",
            phoneNumber = phone
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Recep",
            lastName = "Two",
            email = $"staff-two-{Guid.NewGuid():N}@example.com",
            password = "Reception-Test-123!",
            role = "Receptionist",
            phoneNumber = phone
        });

        await AssertProblemAsync(second, (int)HttpStatusCode.BadRequest, "auth.phone_in_use");
    }
}
