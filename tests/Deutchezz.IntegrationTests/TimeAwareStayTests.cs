using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

/// <summary>Integration coverage for the v1.12 time-aware stays surface.</summary>
[Collection(IntegrationCollection.Name)]
public class TimeAwareStayTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Stay_policy_route_returns_the_effective_policy()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/v1/stay-policy");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        Assert.Equal("14:00", body.GetProperty("defaultCheckInTime").GetString());
        Assert.Equal("12:00", body.GetProperty("defaultCheckOutTime").GetString());
        Assert.Equal(30, body.GetProperty("timeSlotMinutes").GetInt32());
        Assert.Equal(0, body.GetProperty("turnaroundMinutes").GetInt32());
    }

    [Fact]
    public async Task Availability_returns_local_times_and_lagos_offset_instants()
    {
        using var client = CreateClient();
        var checkIn = Clock.Today.AddDays(Random.Shared.Next(500, 3000));

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = checkIn,
            checkInTime = "15:00",
            checkOutDate = checkIn.AddDays(2),
            checkOutTime = "11:30",
            adults = 1,
            children = 0,
            rooms = 1
        });
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        Assert.Equal("15:00", body.GetProperty("checkInTime").GetString());
        Assert.Equal("11:30", body.GetProperty("checkOutTime").GetString());

        var checkInAt = body.GetProperty("checkInAt").GetString()!;
        var checkOutAt = body.GetProperty("checkOutAt").GetString()!;
        Assert.EndsWith("+01:00", checkInAt);
        Assert.EndsWith("+01:00", checkOutAt);
        Assert.Contains($"T15:00:00", checkInAt);
        Assert.Contains($"T11:30:00", checkOutAt);
    }

    [Fact]
    public async Task Availability_defaults_times_to_the_stay_policy()
    {
        using var client = CreateClient();
        var checkIn = Clock.Today.AddDays(Random.Shared.Next(500, 3000));

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = checkIn,
            checkOutDate = checkIn.AddDays(2),
            adults = 1,
            children = 0,
            rooms = 1
        });
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        Assert.Equal("14:00", body.GetProperty("checkInTime").GetString());
        // v1.15: an omitted check-out time mirrors the check-in time (check-in + 24h × nights).
        Assert.Equal("14:00", body.GetProperty("checkOutTime").GetString());
    }

    [Fact]
    public async Task Availability_rejects_a_time_off_the_slot_grid()
    {
        using var client = CreateClient();
        var checkIn = Clock.Today.AddDays(Random.Shared.Next(500, 3000));

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = checkIn,
            checkInTime = "14:07",
            checkOutDate = checkIn.AddDays(2),
            checkOutTime = "11:30",
            adults = 1,
            children = 0,
            rooms = 1
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "stay.time_not_allowed");
    }

    [Fact]
    public async Task Availability_allows_a_stay_longer_than_its_nights()
    {
        using var client = CreateClient();
        var checkIn = Clock.Today.AddDays(Random.Shared.Next(500, 3000));

        // v1.14: an overstay is now billable, not rejected.
        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = checkIn,
            checkInTime = "00:00",
            checkOutDate = checkIn.AddDays(1),
            checkOutTime = "23:30",
            adults = 1,
            children = 0,
            rooms = 1
        });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Availability_rejects_a_past_check_in()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/availability/search", new
        {
            checkInDate = Clock.Today.AddDays(-2),
            checkOutDate = Clock.Today.AddDays(-1),
            adults = 1,
            children = 0,
            rooms = 1
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "stay.in_the_past");
    }

    [Fact]
    public async Task Room_board_at_an_instant_exposes_time_aware_fields()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var at = Clock.Today.AddDays(120).ToString("yyyy-MM-dd");
        var response = await client.GetAsync(
            $"/api/v1/reception/rooms?at={at}T12:00&pageSize=100");
        response.EnsureSuccessStatusCode();

        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.NotEqual(JsonValueKind.Undefined, item.GetProperty("effectiveStatus").ValueKind);
            Assert.NotEqual(JsonValueKind.Undefined, item.GetProperty("occupiedUntil").ValueKind);
            Assert.NotEqual(JsonValueKind.Undefined, item.GetProperty("availableAgainAt").ValueKind);
            Assert.NotEqual(JsonValueKind.Undefined, item.GetProperty("isOverdueCheckOut").ValueKind);
        });
    }

    [Fact]
    public async Task Room_board_still_accepts_a_legacy_bare_date()
    {
        var admin = await LoginAsAdminAsync();
        using var client = CreateClientWithToken(admin.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/reception/rooms?date={Clock.Today:yyyy-MM-dd}&pageSize=5");
        response.EnsureSuccessStatusCode();

        Assert.NotEmpty((await ReadJsonAsync(response)).GetProperty("items").EnumerateArray());
    }
}
