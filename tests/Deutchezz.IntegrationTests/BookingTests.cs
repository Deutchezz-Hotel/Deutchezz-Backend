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
    public async Task Create_rejects_a_check_out_before_the_booked_nights()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // 2 nights booked, but an explicit check-out of 15:00 -> 11:30 is 21.5h short of 48h (v1.15).
        var checkIn = Clock.Today.AddDays(700);
        var response = await SendCreateAsync(client, NewKey(), new
        {
            checkInDate = checkIn,
            checkInTime = "15:00",
            checkOutDate = checkIn.AddDays(2),
            checkOutTime = "11:30",
            adults = 1,
            children = 0,
            guest = new
            {
                firstName = "Early",
                lastName = "Out",
                email = $"early-out-{Guid.NewGuid():N}@example.com",
                phoneNumber = UniquePhone()
            },
            rooms = new[] { new { roomTypeId, quantity = 1 } },
            specialRequests = (string?)null
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "booking.checkout_too_early");
    }

    [Fact]
    public async Task Create_rejects_a_past_check_in()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        // Well outside the 5-minute tolerance, so the policy rejects it (v1.15, rule 8).
        var checkIn = Clock.Today.AddDays(-3);
        var response = await SendCreateAsync(client, NewKey(), new
        {
            checkInDate = checkIn,
            checkInTime = "14:00",
            checkOutDate = checkIn.AddDays(1),
            checkOutTime = "14:00",
            adults = 1,
            children = 0,
            guest = new
            {
                firstName = "Pat",
                lastName = "Past",
                email = $"past-{Guid.NewGuid():N}@example.com",
                phoneNumber = UniquePhone()
            },
            rooms = new[] { new { roomTypeId, quantity = 1 } },
            specialRequests = (string?)null
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "booking.checkin_in_past");
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

    [Fact]
    public async Task Create_sends_guest_confirmation_and_staff_alert()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var email = $"email-{Guid.NewGuid():N}@example.com";

        var response = await SendCreateAsync(client, NewKey(), BuildRequest(roomTypeId, guestEmail: email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reference = (await ReadJsonAsync(response)).GetProperty("booking").GetProperty("bookingReference").GetString();

        var confirmation = EmailSender.Sent.FirstOrDefault(e => e.To == email);
        Assert.NotNull(confirmation);
        Assert.Contains(reference!, confirmation!.Subject);

        var alert = EmailSender.Sent.FirstOrDefault(
            e => e.To == "desk@deutchezz.test" && e.Subject.Contains(reference!, StringComparison.Ordinal));
        Assert.NotNull(alert);
        Assert.Contains(email, alert!.HtmlBody);
    }

    [Fact]
    public async Task Authenticated_customer_booking_appears_in_customer_bookings()
    {
        var customer = await RegisterCustomerAsync();

        using var client = CreateClientWithToken(customer.AccessToken);
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var created = await SendCreateAsync(client, NewKey(), BuildRequest(roomTypeId, guestEmail: customer.Email));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reference = (await ReadJsonAsync(created)).GetProperty("booking").GetProperty("bookingReference").GetString();

        Assert.Contains(reference, await ListCustomerBookingReferencesAsync(client));
    }

    [Fact]
    public async Task Anonymous_booking_with_registered_email_appears_in_customer_bookings()
    {
        // Regression for the reported bug: a booking made with a registered account's email but
        // no bearer token must still show up in that customer's dashboard.
        var customer = await RegisterCustomerAsync();

        using var anonymous = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(anonymous, "Standard");
        var created = await SendCreateAsync(anonymous, NewKey(), BuildRequest(roomTypeId, guestEmail: customer.Email));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reference = (await ReadJsonAsync(created)).GetProperty("booking").GetProperty("bookingReference").GetString();

        using var owner = CreateClientWithToken(customer.AccessToken);
        Assert.Contains(reference, await ListCustomerBookingReferencesAsync(owner));
    }

    [Fact]
    public async Task Registering_with_prior_anonymous_booking_email_links_it()
    {
        // A visitor books anonymously, then registers with the same email: the booking must
        // be adopted into the new account without any further action.
        var email = $"prior-{Guid.NewGuid():N}@example.com";

        using var anonymous = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(anonymous, "Standard");
        var created = await SendCreateAsync(anonymous, NewKey(), BuildRequest(roomTypeId, guestEmail: email));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reference = (await ReadJsonAsync(created)).GetProperty("booking").GetProperty("bookingReference").GetString();

        var customer = await RegisterCustomerAsync(email: email);
        using var owner = CreateClientWithToken(customer.AccessToken);
        Assert.Contains(reference, await ListCustomerBookingReferencesAsync(owner));
    }

    [Fact]
    public async Task Profile_update_syncs_linked_guest_name()
    {
        var customer = await RegisterCustomerAsync();

        using var owner = CreateClientWithToken(customer.AccessToken);
        var roomTypeId = await GetRoomTypeIdAsync(owner, "Standard");
        var created = await SendCreateAsync(owner, NewKey(), BuildRequest(roomTypeId, guestEmail: customer.Email));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var update = await owner.PutAsJsonAsync("/api/v1/me/profile", new
        {
            firstName = "Renamed",
            lastName = "Guest",
            phoneNumber = "08099999999"
        });
        update.EnsureSuccessStatusCode();

        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);
        var response = await adminClient.GetAsync($"/api/v1/admin/guests?search={Uri.EscapeDataString(customer.Email)}&pageSize=10");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        var guest = body.GetProperty("items").EnumerateArray().First();
        Assert.Equal("Renamed", guest.GetProperty("firstName").GetString());
        Assert.Equal("Guest", guest.GetProperty("lastName").GetString());
        Assert.Equal("+2348099999999", guest.GetProperty("phoneNumber").GetString());
    }

    private static async Task<List<string?>> ListCustomerBookingReferencesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me/bookings?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        return body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("bookingReference").GetString())
            .ToList();
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
                phoneNumber = UniquePhone()
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
