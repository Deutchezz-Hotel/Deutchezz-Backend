using System.Net;
using System.Net.Http.Json;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class BookingTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_without_idempotency_key_returns_validation_problem()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var response = await client.PostAsJsonAsync("/api/v1/bookings", BuildRequest(roomTypeId));

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "idempotency_key_required");
    }

    [Fact]
    public async Task Create_anonymous_returns_confirmed_pending_booking()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var response = await SendCreateAsync(client, NewKey(), BuildRequest(roomTypeId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("replay").GetBoolean());
        Assert.True(body.GetProperty("suggestAccount").GetBoolean());

        var booking = body.GetProperty("booking");
        Assert.StartsWith("DHI-", booking.GetProperty("bookingReference").GetString());
        Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
        Assert.Equal("Pending", booking.GetProperty("paymentStatus").GetString());
        Assert.NotEqual(Guid.Empty, booking.GetProperty("id").GetGuid());
        Assert.Single(booking.GetProperty("lines").EnumerateArray());
    }

    [Fact]
    public async Task Replaying_same_key_and_payload_returns_existing_booking()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var key = NewKey();
        var request = BuildRequest(roomTypeId, guestEmail: $"replay-{Guid.NewGuid():N}@example.com");

        var first = await SendCreateAsync(client, key, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstReference = (await ReadJsonAsync(first)).GetProperty("booking").GetProperty("bookingReference").GetString();

        var second = await SendCreateAsync(client, key, request);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await ReadJsonAsync(second);
        Assert.True(secondBody.GetProperty("replay").GetBoolean());
        Assert.Equal(firstReference, secondBody.GetProperty("booking").GetProperty("bookingReference").GetString());
    }

    [Fact]
    public async Task Reusing_key_with_different_payload_returns_conflict()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var key = NewKey();

        var first = await SendCreateAsync(client, key, BuildRequest(roomTypeId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var checkIn = Clock.Today.AddDays(Random.Shared.Next(365, 3650)).AddDays(1);
        var different = BuildRequest(roomTypeId, checkIn: checkIn, checkOut: checkIn.AddDays(3));
        var second = await SendCreateAsync(client, key, different);

        await AssertProblemAsync(second, (int)HttpStatusCode.Conflict, "booking.idempotency_mismatch");
    }

    [Fact]
    public async Task Lookup_with_correct_email_returns_booking()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var email = $"lookup-{Guid.NewGuid():N}@example.com";

        var created = await SendCreateAsync(client, NewKey(), BuildRequest(roomTypeId, guestEmail: email));
        var reference = (await ReadJsonAsync(created)).GetProperty("booking").GetProperty("bookingReference").GetString();

        var response = await client.PostAsJsonAsync("/api/v1/bookings/lookup", new
        {
            bookingReference = reference,
            email
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reference, (await ReadJsonAsync(response)).GetProperty("bookingReference").GetString());
    }

    [Fact]
    public async Task Lookup_with_wrong_email_returns_not_found()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var email = $"lookup-{Guid.NewGuid():N}@example.com";

        var created = await SendCreateAsync(client, NewKey(), BuildRequest(roomTypeId, guestEmail: email));
        var reference = (await ReadJsonAsync(created)).GetProperty("booking").GetProperty("bookingReference").GetString();

        var response = await client.PostAsJsonAsync("/api/v1/bookings/lookup", new
        {
            bookingReference = reference,
            email = "someone-else@example.com"
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.NotFound, "booking.not_found");
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static object BuildRequest(Guid roomTypeId, DateOnly? checkIn = null, DateOnly? checkOut = null, string? guestEmail = null)
    {
        var start = checkIn ?? DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(400 + Random.Shared.Next(0, 2000));
        var end = checkOut ?? start.AddDays(2);

        return new
        {
            checkInDate = start,
            checkOutDate = end,
            adults = 1,
            children = 0,
            guest = new
            {
                firstName = "Ada",
                lastName = "Obi",
                email = guestEmail ?? $"guest-{Guid.NewGuid():N}@example.com",
                phoneNumber = "08010000000"
            },
            rooms = new[] { new { roomTypeId, quantity = 1 } },
            specialRequests = (string?)null
        };
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(HttpClient client, string key, object request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
