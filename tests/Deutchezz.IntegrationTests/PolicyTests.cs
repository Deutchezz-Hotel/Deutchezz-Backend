using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// SoT v1.5 policies: cancellation window (BR-024), refund of paid amounts (BR-025),
/// the configurable check-in/check-out payment gates (7f). Settings toggled here are reset
/// to their default (off) within the test so shared-suite state does not leak.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PolicyTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Cancelling_within_window_returns_conflict()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var booking = await CreateBookingAsync(client, roomTypeId, Clock.Today);

        var response = await client.PostAsJsonAsync("/api/v1/bookings/cancel", new
        {
            bookingReference = booking.GetProperty("bookingReference").GetString(),
            email = booking.GetProperty("guestEmail").GetString()
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.cancellation_window_closed");
    }

    [Fact]
    public async Task Cancelling_outside_window_refunds_paid_amounts()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var booking = await CreateBookingAsync(client, roomTypeId, Clock.Today.AddDays(400));
        var bookingId = booking.GetProperty("id").GetGuid();
        var reference = booking.GetProperty("bookingReference").GetString();
        var email = booking.GetProperty("guestEmail").GetString();
        var total = booking.GetProperty("totalAmount").GetDecimal();

        var admin = await LoginAsAdminAsync();
        using var staff = CreateClientWithToken(admin.AccessToken);
        await RecordPaymentAsync(staff, bookingId, total);

        var response = await client.PostAsJsonAsync("/api/v1/bookings/cancel", new
        {
            bookingReference = reference,
            email
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal("Refunded", body.GetProperty("paymentStatus").GetString());

        Assert.Contains(
            EmailSender.Sent,
            e => e.Subject == $"Booking cancelled {reference}" && e.HtmlBody.Contains("Refunded"));
    }

    [Fact]
    public async Task Checkin_and_checkout_obey_configured_payment_gates()
    {
        using var publicClient = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(publicClient, "Standard");

        var admin = await LoginAsAdminAsync();
        using var staff = CreateClientWithToken(admin.AccessToken);

        // Defaults off: a no-payment booking can pass both gates.
        await SetGateAsync(staff, "requireFullPaymentAtCheckIn", false);
        await SetGateAsync(staff, "requirePaymentBeforeCheckOut", false);

        var booking = await CreateBookingAsync(publicClient, roomTypeId, Clock.Today);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = booking.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();
        var (roomId, _) = await FindAvailableRoomAsync(staff, roomTypeId);

        var assign = await staff.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{bookingId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = lineId, roomId } } });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        // Check-in gate on -> still confirmed booking, so it can be retried after disabling.
        await SetGateAsync(staff, "requireFullPaymentAtCheckIn", true);
        var blockedCheckIn = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-in", content: null);
        await AssertProblemAsync(blockedCheckIn, (int)HttpStatusCode.Conflict, "booking.balance_outstanding");

        await SetGateAsync(staff, "requireFullPaymentAtCheckIn", false);
        var checkIn = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-in", content: null);
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);

        // Check-out gate on -> blocked while balance outstanding.
        await SetGateAsync(staff, "requirePaymentBeforeCheckOut", true);
        var blockedCheckOut = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-out", content: null);
        await AssertProblemAsync(blockedCheckOut, (int)HttpStatusCode.Conflict, "booking.balance_outstanding");

        await SetGateAsync(staff, "requirePaymentBeforeCheckOut", false);
        var checkOut = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
        });
        Assert.Equal(HttpStatusCode.OK, checkOut.StatusCode);
    }

    private async Task<JsonElement> CreateBookingAsync(HttpClient client, Guid roomTypeId, DateOnly checkIn)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = checkIn,
                checkOutDate = checkIn.AddDays(2),
                adults = 1,
                children = 0,
                guest = new
                {
                    firstName = "Policy",
                    lastName = "Test",
                    email = $"policy-{Guid.NewGuid():N}@example.com",
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

    private static async Task RecordPaymentAsync(HttpClient staff, Guid bookingId, decimal amount)
    {
        var response = await staff.PostAsJsonAsync("/api/v1/reception/payments", new
        {
            bookingId,
            amount,
            method = "Cash",
            reference = $"REF-{Guid.NewGuid():N}",
            notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SetGateAsync(HttpClient staff, string key, bool value)
    {
        var response = await staff.PutAsJsonAsync("/api/v1/admin/settings", new Dictionary<string, bool> { [key] = value });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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