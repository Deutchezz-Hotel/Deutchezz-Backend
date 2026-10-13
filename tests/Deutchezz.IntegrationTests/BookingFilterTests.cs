using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// Issue 3: the dashboard cards must drill down into a list that reports the exact same totals.
/// These tests create a genuinely overdue stay and assert the dashboard count and the bookings
/// list filter agree.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BookingFilterTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Overdue_check_out_filter_matches_the_dashboard_count()
    {
        var admin = await LoginAsAdminAsync();
        using var staff = CreateClientWithToken(admin.AccessToken);
        using var publicClient = CreateClient();

        var roomTypeId = await GetRoomTypeIdAsync(publicClient, "Standard");
        var bookingId = await CreateAndCheckInAsync(publicClient, staff, roomTypeId);

        // Move past the scheduled check-out while the guest is still checked in.
        Clock.Advance(TimeSpan.FromDays(3));
        try
        {
            var dashboard = await staff.GetAsync("/api/v1/admin/dashboard");
            dashboard.EnsureSuccessStatusCode();
            var overdueOnCard = (await ReadJsonAsync(dashboard)).GetProperty("overdueCheckOuts").GetInt32();
            Assert.True(overdueOnCard >= 1, "The overdue stay should be counted on the dashboard card.");

            var list = await staff.GetAsync("/api/v1/admin/bookings?overdueCheckOuts=true&pageSize=100");
            list.EnsureSuccessStatusCode();
            var body = await ReadJsonAsync(list);

            Assert.Equal(overdueOnCard, body.GetProperty("totalCount").GetInt32());
            Assert.Contains(
                body.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == bookingId);
        }
        finally
        {
            Clock.Advance(TimeSpan.FromDays(-3));
        }
    }

    [Fact]
    public async Task Active_only_filter_returns_only_live_bookings()
    {
        var admin = await LoginAsAdminAsync();
        using var staff = CreateClientWithToken(admin.AccessToken);

        var response = await staff.GetAsync("/api/v1/admin/bookings?activeOnly=true&pageSize=100");
        response.EnsureSuccessStatusCode();

        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, item =>
        {
            var status = item.GetProperty("status").GetString();
            Assert.Contains(status, new[] { "Pending", "Confirmed", "CheckedIn" });
        });
    }

    private async Task<Guid> CreateAndCheckInAsync(HttpClient publicClient, HttpClient staff, Guid roomTypeId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = Clock.Today,
                checkOutDate = Clock.Today.AddDays(2),
                adults = 1,
                children = 0,
                guest = new
                {
                    firstName = "Overdue",
                    lastName = "Guest",
                    email = $"overdue-{Guid.NewGuid():N}@example.com",
                    phoneNumber = UniquePhone()
                },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = (string?)null
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var created = await publicClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = (await ReadJsonAsync(created)).GetProperty("booking");
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = booking.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        var roomId = await FindAvailableRoomAsync(staff, roomTypeId);

        var assign = await staff.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{bookingId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = lineId, roomId } } });
        assign.EnsureSuccessStatusCode();

        var checkIn = await staff.PostAsync(
            $"/api/v1/reception/bookings/{bookingId}/check-in", content: null);
        checkIn.EnsureSuccessStatusCode();

        return bookingId;
    }

    private static async Task<Guid> FindAvailableRoomAsync(HttpClient staff, Guid roomTypeId)
    {
        var response = await staff.GetAsync($"/api/v1/admin/rooms?roomTypeId={roomTypeId}&pageSize=100");
        response.EnsureSuccessStatusCode();

        foreach (var item in (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("status").GetString() == "Available")
                return item.GetProperty("id").GetGuid();
        }

        throw new InvalidOperationException("No available Standard room was found for check-in.");
    }
}
