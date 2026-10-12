using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deutchezz.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class WalkInBookingTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    // ---------- Walk-in create ----------

    [Fact]
    public async Task Walk_in_booking_stores_the_client_and_the_staff_member_separately()
    {
        var (token, staffId, staffEmail) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: "08031234567", firstName: "Ngozi", lastName: "Ade");
        var bookingId = booking.GetProperty("id").GetGuid();

        Assert.Equal("WalkIn", booking.GetProperty("source").GetString());
        Assert.Equal("Ngozi Ade", booking.GetProperty("guestFullName").GetString());
        Assert.Equal("+2348031234567", booking.GetProperty("guestPhoneNumber").GetString());
        Assert.Null(booking.GetProperty("guestEmail").GetString());
        Assert.Equal("Rita Reception", booking.GetProperty("createdByName").GetString());
        Assert.Equal("Confirmed", booking.GetProperty("status").GetString());

        var entity = await GetBookingEntityAsync(bookingId);
        Assert.Equal(BookingSource.WalkIn, entity.Source);
        Assert.Equal(staffId, entity.CreatedByUserId);
        Assert.Null(entity.SpecialRequests);

        // The guest is account-less, not the staff member.
        var guest = await GetGuestEntityAsync(entity.GuestId);
        Assert.Null(guest.UserId);
        Assert.Equal("+2348031234567", guest.PhoneNumber);
        Assert.Null(guest.Email);

        // No guest row was created against the receptionist's account or email.
        var guests = await staff.GetAsync($"/api/v1/reception/guests?search={Uri.EscapeDataString(staffEmail)}&pageSize=10");
        guests.EnsureSuccessStatusCode();
        Assert.Empty((await ReadJsonAsync(guests)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Walk_in_booking_ignores_email_and_special_requests_fields()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = Clock.Today.AddDays(20),
                checkOutDate = Clock.Today.AddDays(22),
                adults = 1,
                children = 0,
                guest = new
                {
                    firstName = "Extra",
                    lastName = "Fields",
                    phoneNumber = "08030000009",
                    email = "ignored@example.com"
                },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = "Please ignore this"
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await staff.SendAsync(message);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await ReadJsonAsync(response)).GetProperty("booking");

        Assert.Null(booking.GetProperty("guestEmail").GetString());
        Assert.Equal(JsonValueKind.Null, booking.GetProperty("specialRequests").ValueKind);

        var entity = await GetBookingEntityAsync(booking.GetProperty("id").GetGuid());
        Assert.Null(entity.SpecialRequests);
    }

    [Fact]
    public async Task Walk_in_sends_exactly_one_email_to_the_creating_staff_member()
    {
        var (token, _, staffEmail) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var before = EmailSender.Sent.Count;
        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: "08030000021", firstName: "Ada", lastName: "Musa");
        var reference = booking.GetProperty("bookingReference").GetString();

        var sent = EmailSender.Sent.Skip(before).ToList();
        Assert.Single(sent);
        Assert.Equal(staffEmail, sent[0].To);
        Assert.Contains(reference!, sent[0].Subject);
        Assert.Contains("+2348030000021", sent[0].HtmlBody);
        Assert.Contains("Ada Musa", sent[0].HtmlBody);
    }

    [Fact]
    public async Task Walk_in_creates_no_notification_for_the_account_less_guest()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: "08030000022");
        var bookingId = booking.GetProperty("id").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var guestId = db.Bookings.Where(b => b.Id == bookingId).Select(b => b.GuestId).First();
        Assert.False(await db.Notifications.AnyAsync(n => n.GuestId == guestId));
    }

    // ---------- Staff cancellation / no-show ----------

    [Theory]
    [InlineData("GuestRequest", true)]
    [InlineData("GuestRequest", false)]
    [InlineData("BookedInError", true)]
    [InlineData("BookedInError", false)]
    [InlineData("Other", true)]
    [InlineData("Other", false)]
    public async Task Reception_cancel_supports_every_reason_and_refund_choice(string reason, bool refund)
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();

        var pay = await staff.PostAsJsonAsync("/api/v1/reception/payments", new
        {
            bookingId,
            amount = 1000,
            method = "Cash",
            reference = (string?)null,
            notes = (string?)null
        });
        pay.EnsureSuccessStatusCode();

        var cancel = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel", new
        {
            reason,
            notes = reason == "Other" ? "Duplicate entry" : (string?)null,
            refundPaidAmounts = refund
        });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = await ReadJsonAsync(cancel);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        var payment = await FirstPaymentAsync(staff, bookingId);
        Assert.Equal(refund ? "Refunded" : "Paid", payment.GetProperty("status").GetString());
        Assert.Equal(refund ? "Refunded" : "PartiallyPaid", cancelled.GetProperty("paymentStatus").GetString());

        var entity = await GetBookingEntityAsync(bookingId);
        Assert.Equal(BookingStatus.Cancelled, entity.Status);
        Assert.NotNull(entity.CancelledAt);
        Assert.Equal(reason == "Other" ? "Other: Duplicate entry" : reason, entity.CancellationReason);
    }

    [Fact]
    public async Task Reception_cancel_no_show_is_allowed_on_the_check_in_date()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, checkIn: Clock.Today, phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();

        var cancel = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel", new
        {
            reason = "NoShow",
            notes = (string?)null,
            refundPaidAmounts = false
        });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("NoShow", (await GetBookingEntityAsync(bookingId)).CancellationReason);
    }

    [Fact]
    public async Task Reception_cancel_no_show_before_the_check_in_date_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, checkIn: Clock.Today.AddDays(5), phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();

        var cancel = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel", new
        {
            reason = "NoShow",
            notes = (string?)null,
            refundPaidAmounts = false
        });

        await AssertProblemAsync(cancel, (int)HttpStatusCode.Conflict, "booking.noshow_too_early");
    }

    [Fact]
    public async Task Reception_cancel_other_requires_notes()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();

        var cancel = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel", new
        {
            reason = "Other",
            notes = (string?)null,
            refundPaidAmounts = false
        });

        await AssertProblemAsync(cancel, (int)HttpStatusCode.BadRequest, "booking.notes_required");
    }

    [Fact]
    public async Task Reception_cancel_is_blocked_for_checked_in_checked_out_and_cancelled_bookings()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        // Checked-in: assign then check in.
        var booking = await CreateWalkInAsync(staff, roomTypeId, checkIn: Clock.Today, phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = booking.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();
        var (roomId, _) = await FindAvailableRoomAsync(staff, roomTypeId, Clock.Today);
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = lineId, roomId } } })).EnsureSuccessStatusCode();
        (await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-in", content: null)).EnsureSuccessStatusCode();

        var cancelCheckedIn = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        await AssertProblemAsync(cancelCheckedIn, (int)HttpStatusCode.Conflict, "booking.not_cancellable");

        // Checked-out (the stay ends in the future, so consent is required).
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out",
            new { earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true } }))
            .EnsureSuccessStatusCode();
        var cancelCheckedOut = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        await AssertProblemAsync(cancelCheckedOut, (int)HttpStatusCode.Conflict, "booking.not_cancellable");

        // Cancelled.
        var cancellable = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var cancellableId = cancellable.GetProperty("id").GetGuid();
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{cancellableId}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false })).EnsureSuccessStatusCode();

        var cancelTwice = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{cancellableId}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        await AssertProblemAsync(cancelTwice, (int)HttpStatusCode.Conflict, "booking.not_cancellable");
    }

    [Fact]
    public async Task Reception_cancel_releases_room_assignments_and_frees_availability()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var checkIn = Clock.Today.AddDays(40);
        var first = await CreateWalkInAsync(staff, roomTypeId, checkIn: checkIn, phone: UniquePhone());
        var firstId = first.GetProperty("id").GetGuid();
        var firstLine = first.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        var (roomId, _) = await FindAvailableRoomAsync(staff, roomTypeId, checkIn);
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{firstId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = firstLine, roomId } } })).EnsureSuccessStatusCode();

        var second = await CreateWalkInAsync(staff, roomTypeId, checkIn: checkIn, phone: UniquePhone());
        var secondId = second.GetProperty("id").GetGuid();
        var secondLine = second.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        // The same physical room cannot be assigned to two overlapping live bookings.
        var conflict = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{secondId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = secondLine, roomId } } });
        await AssertProblemAsync(conflict, (int)HttpStatusCode.Conflict, "booking.room_unavailable");

        // Cancelling the first releases its assignment…
        (await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{firstId}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false })).EnsureSuccessStatusCode();

        // …so the room can now be assigned to the second booking.
        var retry = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{secondId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = secondLine, roomId } } });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Reception_cancel_is_allowed_for_receptionist_and_admin_but_not_customers()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        // Receptionist.
        var viaReceptionist = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var receptionistResponse = await staff.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{viaReceptionist.GetProperty("id").GetGuid()}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        Assert.Equal(HttpStatusCode.OK, receptionistResponse.StatusCode);

        // Administrator.
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);
        var viaAdmin = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var adminResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{viaAdmin.GetProperty("id").GetGuid()}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        // Customer.
        var customer = await RegisterCustomerAsync();
        using var customerClient = CreateClientWithToken(customer.AccessToken);
        var viaCustomer = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone());
        var customerResponse = await customerClient.PostAsJsonAsync(
            $"/api/v1/reception/bookings/{viaCustomer.GetProperty("id").GetGuid()}/cancel",
            new { reason = "GuestRequest", notes = (string?)null, refundPaidAmounts = false });
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
    }

    // ---------- List / detail / filters ----------

    [Fact]
    public async Task Reception_list_and_detail_expose_source_phone_email_and_created_by()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: "08030000031", firstName: "List", lastName: "Row");
        var bookingId = booking.GetProperty("id").GetGuid();

        var detail = await staff.GetAsync($"/api/v1/reception/bookings/{bookingId}");
        detail.EnsureSuccessStatusCode();
        var detailBody = await ReadJsonAsync(detail);
        Assert.Equal("WalkIn", detailBody.GetProperty("source").GetString());
        Assert.Equal("+2348030000031", detailBody.GetProperty("guestPhoneNumber").GetString());
        Assert.Equal("Rita Reception", detailBody.GetProperty("createdByName").GetString());

        var list = await staff.GetAsync($"/api/v1/reception/bookings?source=WalkIn&pageSize=100");
        list.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(list)).GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, item => Assert.Equal("WalkIn", item.GetProperty("source").GetString()));
        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == bookingId);
    }

    [Fact]
    public async Task Overdue_arrivals_filter_returns_confirmed_bookings_past_their_check_in_date()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, checkIn: Clock.Today, phone: UniquePhone());
        var bookingId = booking.GetProperty("id").GetGuid();

        // Shift the stay into the past so it becomes an overdue arrival.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var entity = await db.Bookings.FirstAsync(b => b.Id == bookingId);
            entity.CheckInDate = entity.CheckInDate.AddDays(-1);
            entity.CheckOutDate = entity.CheckOutDate.AddDays(-1);
            await db.SaveChangesAsync(default);
        }

        var overdue = await staff.GetAsync("/api/v1/reception/bookings?overdueArrivals=true&pageSize=100");
        overdue.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(overdue)).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == bookingId);
        Assert.All(items, item => Assert.Equal("Confirmed", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Payment_list_shows_the_walk_in_client_name()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, roomTypeId, phone: UniquePhone(), firstName: "Payer", lastName: "Client");
        var bookingId = booking.GetProperty("id").GetGuid();

        var pay = await staff.PostAsJsonAsync("/api/v1/reception/payments", new
        {
            bookingId,
            amount = 2000,
            method = "POS",
            reference = "POS-WALKIN-1",
            notes = (string?)null
        });
        pay.EnsureSuccessStatusCode();

        var payment = await FirstPaymentAsync(staff, bookingId);
        Assert.Equal("Payer Client", payment.GetProperty("guestName").GetString());
    }

    [Fact]
    public async Task Online_bookings_still_default_to_online_source_with_no_creator()
    {
        using var client = CreateClient();
        var roomTypeId = await GetRoomTypeIdAsync(client, "Standard");

        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = Clock.Today.AddDays(60),
                checkOutDate = Clock.Today.AddDays(62),
                adults = 1,
                children = 0,
                guest = new
                {
                    firstName = "Online",
                    lastName = "Guest",
                    email = $"online-{Guid.NewGuid():N}@example.com",
                    phoneNumber = UniquePhone()
                },
                rooms = new[] { new { roomTypeId, quantity = 1 } },
                specialRequests = (string?)null
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await ReadJsonAsync(response)).GetProperty("booking");

        Assert.Equal("Online", booking.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, booking.GetProperty("createdByName").ValueKind);
    }

    // ---------- Helpers ----------

    private async Task<(string Token, Guid UserId, string Email)> CreateReceptionistAsync()
    {
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        var email = $"recep-{Guid.NewGuid():N}@example.com";
        var create = await adminClient.PostAsJsonAsync("/api/v1/admin/staff", new
        {
            firstName = "Rita",
            lastName = "Reception",
            email,
            password = "Reception-Test-123!",
            role = "Receptionist",
            phoneNumber = UniquePhone()
        });
        create.EnsureSuccessStatusCode();
        var userId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        using var loginClient = CreateClient();
        var login = await loginClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Reception-Test-123!"
        });
        login.EnsureSuccessStatusCode();
        var token = (await ReadJsonAsync(login)).GetProperty("accessToken").GetString()!;

        return (token, userId, email);
    }

    private async Task<JsonElement> CreateWalkInAsync(
        HttpClient staff, Guid roomTypeId, DateOnly? checkIn = null, string? phone = null,
        string? firstName = "Walk", string? lastName = "In")
    {
        var start = checkIn ?? Clock.Today.AddDays(30);
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

    private static async Task<JsonElement> FirstPaymentAsync(HttpClient staff, Guid bookingId)
    {
        var response = await staff.GetAsync($"/api/v1/reception/payments?bookingId={bookingId}&pageSize=10");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().First();
    }

    private static async Task<(Guid RoomId, string RoomNumber)> FindAvailableRoomAsync(
        HttpClient staff, Guid roomTypeId, DateOnly date)
    {
        var response = await staff.GetAsync(
            $"/api/v1/reception/rooms?roomTypeId={roomTypeId}&date={date:yyyy-MM-dd}&pageSize=100");
        response.EnsureSuccessStatusCode();

        foreach (var item in (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("effectiveStatus").GetString() == "Available")
                return (item.GetProperty("id").GetGuid(), item.GetProperty("roomNumber").GetString()!);
        }

        throw new InvalidOperationException("No available room was found for the requested date.");
    }

    private async Task<Booking> GetBookingEntityAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == id);
    }

    private async Task<Guest> GetGuestEntityAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Guests.AsNoTracking().FirstAsync(g => g.Id == id);
    }
}
