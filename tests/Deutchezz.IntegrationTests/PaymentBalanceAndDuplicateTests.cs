using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// SoT v1.10 coverage: 7l (a payment may only be recorded against an outstanding balance and the
/// reception booking list exposes/filters by balance due) and 7m / BR-041 (the public duplicate
/// booking warning with the <c>acknowledgeDuplicate</c> override).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PaymentBalanceAndDuplicateTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    // ===================== PART A: payments require a balance (7l) =====================

    [Fact]
    public async Task Recording_a_payment_on_a_fully_paid_booking_is_rejected()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var booking = await CreateWalkInAsync(staff, roomTypeId);
        var bookingId = booking.GetProperty("id").GetGuid();
        var total = booking.GetProperty("totalAmount").GetDecimal();

        var first = await RecordPaymentAsync(staff, bookingId, total);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RecordPaymentAsync(staff, bookingId, 1m);
        await AssertProblemAsync(second, (int)HttpStatusCode.Conflict, "payment.no_balance_due");
    }

    [Fact]
    public async Task Recording_a_payment_on_a_cancelled_booking_is_rejected()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var booking = await CreateWalkInAsync(staff, roomTypeId);
        var bookingId = booking.GetProperty("id").GetGuid();

        var cancel = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel", new
        {
            reason = "BookedInError",
            notes = (string?)null,
            refundPaidAmounts = false
        });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var payment = await RecordPaymentAsync(staff, bookingId, 100m);
        await AssertProblemAsync(payment, (int)HttpStatusCode.Conflict, "payment.no_balance_due");
    }

    [Fact]
    public async Task Recording_a_payment_above_the_balance_is_rejected_as_overpayment()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var booking = await CreateWalkInAsync(staff, roomTypeId);
        var bookingId = booking.GetProperty("id").GetGuid();
        var total = booking.GetProperty("totalAmount").GetDecimal();

        var payment = await RecordPaymentAsync(staff, bookingId, total + 100m);
        await AssertProblemAsync(payment, (int)HttpStatusCode.Conflict, "payment.overpayment");
    }

    [Fact]
    public async Task Booking_list_items_expose_a_balance_due_that_tracks_payments()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var booking = await CreateWalkInAsync(staff, roomTypeId);
        var bookingId = booking.GetProperty("id").GetGuid();
        var reference = booking.GetProperty("bookingReference").GetString();
        var total = booking.GetProperty("totalAmount").GetDecimal();

        var before = await FindBookingListItemAsync(staff, reference!);
        Assert.Equal(total, before.GetProperty("balanceDue").GetDecimal());

        (await RecordPaymentAsync(staff, bookingId, 250m)).EnsureSuccessStatusCode();

        var after = await FindBookingListItemAsync(staff, reference!);
        Assert.Equal(total - 250m, after.GetProperty("balanceDue").GetDecimal());
    }

    [Fact]
    public async Task Has_balance_due_filter_excludes_fully_paid_and_cancelled_bookings()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var unpaid = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var paid = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var cancelled = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());

        var paidId = paid.GetProperty("id").GetGuid();
        var paidTotal = paid.GetProperty("totalAmount").GetDecimal();
        (await RecordPaymentAsync(staff, paidId, paidTotal)).EnsureSuccessStatusCode();

        var cancelledId = cancelled.GetProperty("id").GetGuid();
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{cancelledId}/cancel", new
        {
            reason = "BookedInError",
            notes = (string?)null,
            refundPaidAmounts = false
        })).EnsureSuccessStatusCode();

        var ids = await ListBookingIdsAsync(staff, "hasBalanceDue=true&pageSize=100");

        Assert.Contains(unpaid.GetProperty("id").GetGuid(), ids);
        Assert.DoesNotContain(paidId, ids);
        Assert.DoesNotContain(cancelledId, ids);
    }

    [Fact]
    public async Task Reception_booking_search_matches_guest_name_or_reference()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var booking = await CreateWalkInAsync(staff, roomTypeId, firstName: "Searchable", lastName: "Guest");
        var reference = booking.GetProperty("bookingReference").GetString();

        var byName = await ListBookingIdsAsync(staff, $"search=searchable%20guest&pageSize=100");
        Assert.Contains(booking.GetProperty("id").GetGuid(), byName);

        var byReference = await ListBookingIdsAsync(staff, $"search={reference}&pageSize=100");
        Assert.Contains(booking.GetProperty("id").GetGuid(), byReference);
    }

    // ===================== PART B: duplicate booking warning (7m, BR-041) =====================

    [Fact]
    public async Task Duplicate_anonymous_booking_within_the_window_is_warned_with_a_masked_reference()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var phone = UniquePhone();

        var first = await PostPublicBookingAsync(client, roomTypeId, email, phone, acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstReference = (await ReadJsonAsync(first)).GetProperty("booking")
            .GetProperty("bookingReference").GetString()!;

        var second = await PostPublicBookingAsync(client, roomTypeId, email, phone, acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await ReadProblemAsync(second);
        Assert.Equal("booking.possible_duplicate", problem.GetProperty("code").GetString());

        Assert.True(problem.TryGetProperty("minutesAgo", out var minutesAgo));
        Assert.True(minutesAgo.GetInt32() >= 0);

        var masked = problem.GetProperty("reference").GetString()!;
        Assert.Matches(@"^DHI-\d{4}-\*+.{2}$", masked);
        Assert.NotEqual(firstReference, masked);
        Assert.DoesNotContain("bookingId", problem.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Duplicate_anonymous_booking_succeeds_when_acknowledged()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");
        var email = $"dup-ack-{Guid.NewGuid():N}@example.com";
        var phone = UniquePhone();

        var first = await PostPublicBookingAsync(client, roomTypeId, email, phone, acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var acknowledged = await PostPublicBookingAsync(client, roomTypeId, email, phone, acknowledgeDuplicate: true);
        Assert.Equal(HttpStatusCode.Created, acknowledged.StatusCode);
    }

    [Fact]
    public async Task Duplicate_authenticated_booking_warns_with_the_booking_id_and_full_reference()
    {
        var customer = await RegisterCustomerAsync();
        using var owner = CreateClientWithToken(customer.AccessToken);
        var roomTypeId = await GetRoomTypeIdAsync(owner, "Standard");

        var first = await PostPublicBookingAsync(
            owner, roomTypeId, customer.Email, UniquePhone(), acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBooking = (await ReadJsonAsync(first)).GetProperty("booking");
        var firstId = firstBooking.GetProperty("id").GetGuid();
        var firstReference = firstBooking.GetProperty("bookingReference").GetString()!;

        var second = await PostPublicBookingAsync(
            owner, roomTypeId, customer.Email, UniquePhone(), acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await ReadProblemAsync(second);
        Assert.Equal("booking.possible_duplicate", problem.GetProperty("code").GetString());
        Assert.Equal(firstId, problem.GetProperty("bookingId").GetGuid());
        Assert.Equal(firstReference, problem.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Duplicate_warning_does_not_apply_to_a_different_guest()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var first = await PostPublicBookingAsync(
            client, roomTypeId, $"a-{Guid.NewGuid():N}@example.com", UniquePhone(), acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostPublicBookingAsync(
            client, roomTypeId, $"b-{Guid.NewGuid():N}@example.com", UniquePhone(), acknowledgeDuplicate: false);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Reception_walk_in_create_is_never_flagged_as_a_duplicate()
    {
        var (staff, roomTypeId) = await CreateStaffAsync();
        var phone = UniquePhone();

        var first = await CreateWalkInAsync(staff, roomTypeId, phone: phone);
        Assert.Equal("WalkIn", first.GetProperty("source").GetString());

        var second = await CreateWalkInAsync(staff, roomTypeId, phone: phone);
        Assert.Equal("WalkIn", second.GetProperty("source").GetString());
    }

    // ===================== Helpers =====================

    private async Task<(HttpClient Staff, Guid RoomTypeId)> CreateStaffAsync()
    {
        var admin = await LoginAsAdminAsync();
        var staff = CreateClientWithToken(admin.AccessToken);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");
        return (staff, roomTypeId);
    }

    private static async Task<HttpResponseMessage> RecordPaymentAsync(HttpClient staff, Guid bookingId, decimal amount) =>
        await staff.PostAsJsonAsync("/api/v1/reception/payments", new
        {
            bookingId,
            amount,
            method = "Cash",
            reference = (string?)null,
            notes = (string?)null
        });

    private async Task<HttpResponseMessage> PostPublicBookingAsync(
        HttpClient client, Guid roomTypeId, string email, string phone, bool acknowledgeDuplicate)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = Clock.Today.AddDays(500),
                checkOutDate = Clock.Today.AddDays(502),
                adults = 1,
                children = 0,
                guest = new { firstName = "Dup", lastName = "Guest", email, phoneNumber = phone },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = (string?)null,
                acknowledgeDuplicate
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(message);
    }

    private async Task<JsonElement> CreateWalkInAsync(
        HttpClient staff, Guid roomTypeId, string? phone = null,
        string? firstName = "Walk", string? lastName = "In")
    {
        var start = Clock.Today.AddDays(60 + Random.Shared.Next(0, 300));
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = start,
                checkOutDate = start.AddDays(2),
                adults = 1,
                children = 0,
                guest = new { firstName, lastName, phoneNumber = phone ?? UniquePhone() },
                rooms = new[] { new { roomTypeId, quantity = 1 } }
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await staff.SendAsync(message);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("booking");
    }

    private async Task<JsonElement> FindBookingListItemAsync(HttpClient staff, string reference)
    {
        var response = await staff.GetAsync($"/api/v1/reception/bookings?search={Uri.EscapeDataString(reference)}&pageSize=100");
        response.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        return items.Single(i => i.GetProperty("bookingReference").GetString() == reference);
    }

    private static async Task<List<Guid>> ListBookingIdsAsync(HttpClient staff, string queryString)
    {
        var response = await staff.GetAsync($"/api/v1/reception/bookings?{queryString}");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();
    }
}
