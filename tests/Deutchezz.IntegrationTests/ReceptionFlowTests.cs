using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ReceptionFlowTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Full_reception_flow_assign_checkin_checkout()
    {
        using var publicClient = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(publicClient, "Standard");

        var booking = await CreateBookingAsync(publicClient, roomTypeId);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = booking.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        var admin = await LoginAsAdminAsync();
        using var staff = CreateClientWithToken(admin.AccessToken);

        var (roomId, roomNumber) = await FindAvailableRoomAsync(staff, roomTypeId);

        var assignResponse = await staff.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{bookingId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = lineId, roomId } } });

        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        var assigned = await ReadJsonAsync(assignResponse);
        Assert.Single(assigned.GetProperty("assignments").EnumerateArray());

        var checkInResponse = await staff.PostAsync(
            $"/api/v1/reception/bookings/{bookingId}/check-in", content: null);

        Assert.Equal(HttpStatusCode.OK, checkInResponse.StatusCode);
        var checkIn = await ReadJsonAsync(checkInResponse);
        Assert.Equal("CheckedIn", checkIn.GetProperty("status").GetString());
        Assert.Contains(roomNumber, checkIn.GetProperty("roomNumbers").EnumerateArray().Select(r => r.GetString()));

        // The stay ends in the future, so this is an early check-out and requires recorded consent.
        var checkOutResponse = await staff.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{bookingId}/check-out",
            new { earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true } });

        Assert.Equal(HttpStatusCode.OK, checkOutResponse.StatusCode);
        var checkOut = await ReadJsonAsync(checkOutResponse);
        Assert.Equal("CheckedOut", checkOut.GetProperty("status").GetString());
        Assert.Contains(roomNumber, checkOut.GetProperty("releasedRoomNumbers").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Reception_requires_staff_authorization()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/reception/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<JsonElement> CreateBookingAsync(HttpClient client, Guid roomTypeId)
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
                    firstName = "Reception",
                    lastName = "Flow",
                    email = $"reception-{Guid.NewGuid():N}@example.com",
                    phoneNumber = UniquePhone()
                },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = (string?)null
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("booking");
    }

    private static async Task<(Guid RoomId, string RoomNumber)> FindAvailableRoomAsync(HttpClient staff, Guid roomTypeId)
    {
        var response = await staff.GetAsync($"/api/v1/admin/rooms?roomTypeId={roomTypeId}&pageSize=100");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("status").GetString() == "Available")
                return (item.GetProperty("id").GetGuid(), item.GetProperty("roomNumber").GetString()!);
        }

        throw new InvalidOperationException("No available Standard room was found for check-in.");
    }
}
