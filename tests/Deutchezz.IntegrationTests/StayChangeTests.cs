using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deutchezz.IntegrationTests;

/// <summary>
/// SoT v1.9 (7h–7k): early check-out with recorded consent, changing room type, room
/// reassignment/unassignment and the room availability range filter.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StayChangeTests(HotelWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    // ===================== Early check-out (7h) =====================

    [Fact]
    public async Task Early_checkout_without_a_body_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);

        var response = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-out", content: null);

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.early_checkout_requires_consent");

        // Release the room for later tests in this collection.
        await CheckoutEarlyAsync(staff, bookingId);
    }

    [Fact]
    public async Task Early_checkout_without_guest_consent_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = false }
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.early_checkout_requires_consent");

        // Release the room for later tests in this collection.
        await CheckoutEarlyAsync(staff, bookingId);
    }

    [Fact]
    public async Task Early_checkout_with_reason_and_consent_succeeds_and_is_audited()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, reference) = await CreateCheckedInBookingAsync(staff);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "Emergency", notes = "Family emergency", guestConsented = true }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("CheckedOut", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isEarly").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("scheduledCheckOutAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("actualCheckOutAt").ValueKind);

        var entity = await GetBookingEntityAsync(bookingId);
        Assert.NotNull(entity.ActualCheckOutAt);
        Assert.Equal("Emergency", entity.EarlyCheckOutReason);
        Assert.Equal("Family emergency", entity.EarlyCheckOutNotes);

        var log = await LatestActivityLogAsync(bookingId);
        Assert.NotNull(log);
        Assert.Equal("booking.checked_out", log!.Action);
        Assert.Contains("Emergency", log.Description);
        Assert.Contains("guest consented: yes", log.Description);
        Assert.Contains(reference, log.Description);

        // Rooms are released immediately (BR-015).
        Assert.False(await HasLiveAssignmentAsync(bookingId));
    }

    [Fact]
    public async Task Early_checkout_with_other_reason_requires_notes()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "Other", notes = (string?)null, guestConsented = true }
        });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "booking.notes_required");

        // Release the room for later tests in this collection.
        await CheckoutEarlyAsync(staff, bookingId);
    }

    [Fact]
    public async Task Checkout_on_or_after_the_scheduled_moment_needs_no_consent()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);

        // Move the scheduled check-out moment into the past (yesterday 12:00 Africa/Lagos).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var entity = await db.Bookings.FirstAsync(b => b.Id == bookingId);
            var offset = Clock.ConvertFromUtc(Clock.UtcNow).Offset;
            entity.SetStay(Clock.Today.AddDays(-3), new TimeOnly(14, 0),
                Clock.Today.AddDays(-1), new TimeOnly(12, 0), offset);
            await db.SaveChangesAsync(default);
        }

        var response = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-out", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("isEarly").GetBoolean());

        var entity2 = await GetBookingEntityAsync(bookingId);
        Assert.NotNull(entity2.ActualCheckOutAt);
        Assert.Null(entity2.EarlyCheckOutReason);
    }

    [Fact]
    public async Task Early_checkout_is_blocked_for_receptionist_when_admin_only_but_allowed_for_admin()
    {
        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);

        await SetSettingAsync(adminClient, "earlyCheckOutRequiresAdmin", true);
        try
        {
            var (receptionistBookingId, _) = await CreateCheckedInBookingAsync(staff);
            var blocked = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{receptionistBookingId}/check-out", new
            {
                earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
            });
            await AssertProblemAsync(blocked, (int)HttpStatusCode.Forbidden, "booking.early_checkout_requires_admin");

            // Release the receptionist's room: the administrator may perform the early check-out.
            var release = await adminClient.PostAsJsonAsync($"/api/v1/reception/bookings/{receptionistBookingId}/check-out", new
            {
                earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
            });
            Assert.Equal(HttpStatusCode.OK, release.StatusCode);

            var (adminBookingId, _) = await CreateCheckedInBookingAsync(staff);
            var allowed = await adminClient.PostAsJsonAsync($"/api/v1/reception/bookings/{adminBookingId}/check-out", new
            {
                earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
            });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        finally
        {
            await SetSettingAsync(adminClient, "earlyCheckOutRequiresAdmin", false);
        }
    }

    [Fact]
    public async Task Rooms_become_available_again_after_an_early_checkout()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");

        var checkIn = Clock.Today;
        var checkOut = checkIn.AddDays(2);
        var booking = await CreateWalkInAsync(staff, roomTypeId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);
        var (roomId, _) = await FindFreeRoomAsync(staff, roomTypeId, checkIn, checkOut);
        await AssignRoomAsync(staff, bookingId, lineId, roomId);
        await CheckInAsync(staff, bookingId);

        Assert.DoesNotContain(roomId, await FreeRoomIdsAsync(staff, roomTypeId, checkIn, checkOut));

        var checkout = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
        });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);

        Assert.Contains(roomId, await FreeRoomIdsAsync(staff, roomTypeId, checkIn, checkOut));
    }

    // ===================== Change room type (7i) =====================

    [Fact]
    public async Task Change_room_type_re_snapshots_the_rate_and_recomputes_the_total()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var booking = await CreateWalkInAsync(staff, standardId, Clock.Today.AddDays(80), Clock.Today.AddDays(82), quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);
        Assert.Equal(60000m, booking.GetProperty("totalAmount").GetDecimal());

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, deluxeId, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var detail = body.GetProperty("booking");
        var lines = detail.GetProperty("lines").EnumerateArray().ToList();
        Assert.Single(lines);
        Assert.Equal(deluxeId, lines[0].GetProperty("roomTypeId").GetGuid());
        Assert.Equal(40000m, lines[0].GetProperty("ratePerNight").GetDecimal());
        Assert.Equal(80000m, lines[0].GetProperty("subtotal").GetDecimal());
        Assert.Equal(80000m, detail.GetProperty("totalAmount").GetDecimal());

        var entity = await GetBookingEntityAsync(bookingId);
        Assert.Equal(80000m, entity.TotalAmount);
    }

    [Fact]
    public async Task Change_room_type_supports_a_partial_quantity_split()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var booking = await CreateWalkInAsync(staff, standardId, Clock.Today.AddDays(85), Clock.Today.AddDays(87), quantity: 2);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, deluxeId, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lines = (await ReadJsonAsync(response)).GetProperty("booking").GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);

        var standardLine = lines.Single(l => l.GetProperty("roomTypeId").GetGuid() == standardId);
        var deluxeLine = lines.Single(l => l.GetProperty("roomTypeId").GetGuid() == deluxeId);
        Assert.Equal(1, standardLine.GetProperty("quantity").GetInt32());
        Assert.Equal(1, deluxeLine.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Change_room_type_releases_the_moved_rooms_assignments()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var checkIn = Clock.Today.AddDays(90);
        var checkOut = checkIn.AddDays(2);
        var booking = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);
        var (roomId, roomNumber) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        await AssignRoomAsync(staff, bookingId, lineId, roomId);
        Assert.True(await HasLiveAssignmentAsync(bookingId));

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, deluxeId, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cleared = (await ReadJsonAsync(response)).GetProperty("assignmentsCleared")
            .EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(roomNumber, cleared);
        Assert.False(await HasLiveAssignmentAsync(bookingId));
    }

    [Fact]
    public async Task Change_room_type_is_blocked_when_paid_amount_would_exceed_the_new_total()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var booking = await CreateWalkInAsync(staff, deluxeId, Clock.Today.AddDays(95), Clock.Today.AddDays(97), quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);

        var total = booking.GetProperty("totalAmount").GetDecimal();
        var pay = await staff.PostAsJsonAsync("/api/v1/reception/payments", new
        {
            bookingId,
            amount = total,
            method = "Cash",
            reference = (string?)null,
            notes = (string?)null
        });
        pay.EnsureSuccessStatusCode();

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, standardId, 1);
        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.paid_exceeds_new_total");
    }

    [Fact]
    public async Task Change_room_type_is_blocked_after_check_in()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);
        var lineId = (await GetDetailAsync(staff, bookingId)).GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, deluxeId, 1);
        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.invalid_state");
    }

    [Fact]
    public async Task Change_room_type_accounts_for_the_bookings_own_units_on_the_new_type()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        // Within one date range the owner already holds 2 Deluxe and 1 Standard, and another
        // booking holds 4 Deluxe. The owner may move its 1 Standard into Deluxe only because the
        // allowance counts the owner's own 2 Deluxe (7 - 4 others - 2 own = 1), filling Deluxe exactly.
        var okIn = Clock.Today.AddDays(100);
        var okOut = okIn.AddDays(2);
        var okOwner = await CreateWalkInMultiAsync(staff, okIn, okOut, (deluxeId, 2), (standardId, 1));
        var okOwnerId = okOwner.GetProperty("id").GetGuid();
        var okStandardLine = okOwner.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("roomTypeId").GetGuid() == standardId).GetProperty("id").GetGuid();
        await CreateWalkInAsync(staff, deluxeId, okIn, okOut, quantity: 4);

        var ok = await ChangeRoomTypeAsync(staff, okOwnerId, okStandardLine, deluxeId, 1);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Same shape but with 6 Deluxe held by others: the owner's own 1 Deluxe makes the allowance
        // 0, so the change must be rejected (ignoring own units would wrongly allow it).
        var noIn = Clock.Today.AddDays(110);
        var noOut = noIn.AddDays(2);
        var noOwner = await CreateWalkInMultiAsync(staff, noIn, noOut, (deluxeId, 1), (standardId, 1));
        var noOwnerId = noOwner.GetProperty("id").GetGuid();
        var noStandardLine = noOwner.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("roomTypeId").GetGuid() == standardId).GetProperty("id").GetGuid();
        await CreateWalkInAsync(staff, deluxeId, noIn, noOut, quantity: 6);

        var blocked = await ChangeRoomTypeAsync(staff, noOwnerId, noStandardLine, deluxeId, 1);
        await AssertProblemAsync(blocked, (int)HttpStatusCode.Conflict, "booking.unavailable");
    }

    [Fact]
    public async Task Change_room_type_is_rejected_when_the_new_type_is_full()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var checkIn = Clock.Today.AddDays(120);
        var checkOut = checkIn.AddDays(2);

        await CreateWalkInAsync(staff, deluxeId, checkIn, checkOut, quantity: 7);
        var booking = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);

        var response = await ChangeRoomTypeAsync(staff, bookingId, lineId, deluxeId, 1);
        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.unavailable");
    }

    [Fact]
    public async Task Concurrent_change_room_type_requests_never_exceed_capacity()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var checkIn = Clock.Today.AddDays(300);
        var checkOut = checkIn.AddDays(2);

        // 8 Standard bookings (Standard capacity is 8); Deluxe capacity is 7.
        var bookings = new List<(Guid BookingId, Guid LineId)>();
        for (var i = 0; i < 8; i++)
        {
            var b = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
            bookings.Add((b.GetProperty("id").GetGuid(), FirstLineId(b)));
        }

        var responses = await Task.WhenAll(bookings.Select(b =>
            staff.PostAsJsonAsync($"/api/v1/reception/bookings/{b.BookingId}/change-room-type",
                new { bookingRoomTypeId = b.LineId, newRoomTypeId = deluxeId, quantity = 1 })));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, succeeded);

        var bookingIds = bookings.Select(b => b.BookingId).ToList();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var deluxeQuantity = await db.BookingRoomTypes
            .Where(l => l.RoomTypeId == deluxeId && bookingIds.Contains(l.BookingId))
            .SumAsync(l => (int?)l.Quantity) ?? 0;
        Assert.Equal(7, deluxeQuantity);
    }

    // ===================== Reassign / unassign (7j) =====================

    [Fact]
    public async Task Reassign_room_to_a_different_room_type_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(staff, "Deluxe");

        var checkIn = Clock.Today.AddDays(400);
        var checkOut = checkIn.AddDays(2);
        var booking = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);
        var (roomId, _) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        await AssignRoomAsync(staff, bookingId, lineId, roomId);
        var assignmentId = await FirstAssignmentIdAsync(staff, bookingId);

        var deluxeRoomId = await AnyRoomOfTypeAsync(staff, deluxeId);
        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/reassign-room",
            new { roomAssignmentId = assignmentId, newRoomId = deluxeRoomId, reason = (string?)null });

        await AssertProblemAsync(response, (int)HttpStatusCode.BadRequest, "booking.wrong_roomtype");
    }

    [Fact]
    public async Task Reassign_room_to_an_overlapping_room_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");

        var checkIn = Clock.Today.AddDays(405);
        var checkOut = checkIn.AddDays(2);

        var first = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var firstId = first.GetProperty("id").GetGuid();
        var (firstRoomId, _) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        await AssignRoomAsync(staff, firstId, FirstLineId(first), firstRoomId);

        var second = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var secondId = second.GetProperty("id").GetGuid();
        var (secondRoomId, _) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        Assert.NotEqual(firstRoomId, secondRoomId);
        await AssignRoomAsync(staff, secondId, FirstLineId(second), secondRoomId);
        var secondAssignmentId = await FirstAssignmentIdAsync(staff, secondId);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{secondId}/reassign-room",
            new { roomAssignmentId = secondAssignmentId, newRoomId = firstRoomId, reason = "Quieter" });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "room.occupied_until");
    }

    [Fact]
    public async Task Reassign_room_while_checked_in_swaps_the_room_statuses()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");

        var booking = await CreateWalkInAsync(staff, standardId, Clock.Today, Clock.Today.AddDays(2), quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var (oldRoomId, _) = await FindFreeRoomAsync(staff, standardId, Clock.Today, Clock.Today.AddDays(2));
        await AssignRoomAsync(staff, bookingId, FirstLineId(booking), oldRoomId);
        await CheckInAsync(staff, bookingId);
        var assignmentId = await FirstAssignmentIdAsync(staff, bookingId);

        var (newRoomId, _) = await FindFreeRoomAsync(staff, standardId, Clock.Today, Clock.Today.AddDays(2), exclude: oldRoomId);
        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/reassign-room",
            new { roomAssignmentId = assignmentId, newRoomId, reason = "Guest request" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(RoomStatus.Available, await RoomStatusAsync(oldRoomId));
        Assert.Equal(RoomStatus.Occupied, await RoomStatusAsync(newRoomId));
        Assert.Equal(newRoomId, await LiveAssignedRoomIdAsync(bookingId));

        var log = await LatestActivityLogAsync(bookingId);
        Assert.Equal("booking.room_reassigned", log!.Action);

        // Release both rooms so later tests in this collection can use them.
        await CheckoutEarlyAsync(staff, bookingId);
    }

    [Fact]
    public async Task Unassign_room_before_check_in_releases_the_assignment()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");

        var checkIn = Clock.Today.AddDays(410);
        var checkOut = checkIn.AddDays(2);
        var booking = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var (roomId, _) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        await AssignRoomAsync(staff, bookingId, FirstLineId(booking), roomId);
        var assignmentId = await FirstAssignmentIdAsync(staff, bookingId);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/unassign-room",
            new { roomAssignmentId = assignmentId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HasLiveAssignmentAsync(bookingId));

        var log = await LatestActivityLogAsync(bookingId);
        Assert.Equal("booking.room_unassigned", log!.Action);
    }

    [Fact]
    public async Task Unassign_room_after_check_in_is_rejected()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var (bookingId, _) = await CreateCheckedInBookingAsync(staff);
        var assignmentId = await FirstAssignmentIdAsync(staff, bookingId);

        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/unassign-room",
            new { roomAssignmentId = assignmentId });

        await AssertProblemAsync(response, (int)HttpStatusCode.Conflict, "booking.invalid_state");

        // Release the room for later tests in this collection.
        await CheckoutEarlyAsync(staff, bookingId);
    }

    // ===================== Room availability range filter (7k) =====================

    [Fact]
    public async Task Room_availability_range_filter_excludes_assigned_rooms()
    {
        var (token, _, _) = await CreateReceptionistAsync();
        using var staff = CreateClientWithToken(token);
        var standardId = await GetRoomTypeIdAsync(staff, "Standard");

        var checkIn = Clock.Today.AddDays(500);
        var checkOut = checkIn.AddDays(3);

        var booking = await CreateWalkInAsync(staff, standardId, checkIn, checkOut, quantity: 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var (roomId, _) = await FindFreeRoomAsync(staff, standardId, checkIn, checkOut);
        await AssignRoomAsync(staff, bookingId, FirstLineId(booking), roomId);

        var free = await FreeRoomIdsAsync(staff, standardId, checkIn, checkOut);
        Assert.DoesNotContain(roomId, free);
        Assert.NotEmpty(free);

        var missingTo = await staff.GetAsync(
            $"/api/v1/reception/rooms?roomTypeId={standardId}&availableFrom={checkIn:yyyy-MM-dd}&pageSize=100");
        await AssertProblemAsync(missingTo, (int)HttpStatusCode.BadRequest, "room.invalid_availability_range");
    }

    [Fact]
    public async Task Change_room_type_sends_an_updated_booking_email_to_a_guest_with_an_email()
    {
        using var publicClient = CreateClient();
        var standardId = await GetRoomTypeIdAsync(publicClient, "Standard");
        var deluxeId = await GetRoomTypeIdAsync(publicClient, "Deluxe");

        var email = $"change-{Guid.NewGuid():N}@example.com";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = Clock.Today.AddDays(600),
                checkOutDate = Clock.Today.AddDays(602),
                adults = 1,
                children = 0,
                guest = new { firstName = "Change", lastName = "Guest", email, phoneNumber = UniquePhone() },
                rooms = new[] { new { roomTypeId = standardId, quantity = 1 } },
                specialRequests = (string?)null
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var create = await publicClient.SendAsync(request);
        create.EnsureSuccessStatusCode();
        var booking = (await ReadJsonAsync(create)).GetProperty("booking");
        var bookingId = booking.GetProperty("id").GetGuid();
        var lineId = FirstLineId(booking);
        var reference = booking.GetProperty("bookingReference").GetString();

        var admin = await LoginAsAdminAsync();
        using var adminClient = CreateClientWithToken(admin.AccessToken);

        var response = await ChangeRoomTypeAsync(adminClient, bookingId, lineId, deluxeId, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(EmailSender.Sent, e => e.To == email && e.Subject.Contains(reference!));
    }

    // ===================== Helpers =====================

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
        HttpClient staff, Guid roomTypeId, DateOnly checkIn, DateOnly checkOut, int quantity,
        string? firstName = "Stay", string? lastName = "Change")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = checkIn,
                checkOutDate = checkOut,
                adults = 1,
                children = 0,
                guest = new { firstName, lastName, phoneNumber = UniquePhone() },
                rooms = new[] { new { roomTypeId, quantity } }
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await staff.SendAsync(message);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("booking");
    }

    private async Task<JsonElement> CreateWalkInMultiAsync(
        HttpClient staff, DateOnly checkIn, DateOnly checkOut, params (Guid RoomTypeId, int Quantity)[] lines)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reception/bookings")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = checkIn,
                checkOutDate = checkOut,
                adults = 1,
                children = 0,
                guest = new { firstName = "Multi", lastName = "Line", phoneNumber = UniquePhone() },
                rooms = lines.Select(l => new { roomTypeId = l.RoomTypeId, quantity = l.Quantity }).ToArray()
            })
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await staff.SendAsync(message);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("booking");
    }

    private async Task<(Guid BookingId, string Reference)> CreateCheckedInBookingAsync(HttpClient staff)
    {
        var roomTypeId = await GetRoomTypeIdAsync(staff, "Standard");
        var booking = await CreateWalkInAsync(staff, roomTypeId, Clock.Today, Clock.Today.AddDays(2), 1);
        var bookingId = booking.GetProperty("id").GetGuid();
        var reference = booking.GetProperty("bookingReference").GetString()!;
        var lineId = FirstLineId(booking);
        var (roomId, _) = await FindFreeRoomAsync(staff, roomTypeId, Clock.Today, Clock.Today.AddDays(2));
        await AssignRoomAsync(staff, bookingId, lineId, roomId);
        await CheckInAsync(staff, bookingId);
        return (bookingId, reference);
    }

    private static Guid FirstLineId(JsonElement booking) =>
        booking.GetProperty("lines").EnumerateArray().First().GetProperty("id").GetGuid();

    private static async Task AssignRoomAsync(HttpClient staff, Guid bookingId, Guid lineId, Guid roomId)
    {
        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/assign-rooms",
            new { assignments = new[] { new { bookingRoomTypeId = lineId, roomId } }, acknowledgeLateAvailability = true });
        response.EnsureSuccessStatusCode();
    }

    private static async Task CheckInAsync(HttpClient staff, Guid bookingId)
    {
        var response = await staff.PostAsync($"/api/v1/reception/bookings/{bookingId}/check-in", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task CheckoutEarlyAsync(HttpClient staff, Guid bookingId)
    {
        var response = await staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/check-out", new
        {
            earlyCheckOut = new { reason = "GuestRequest", notes = (string?)null, guestConsented = true }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> ChangeRoomTypeAsync(
        HttpClient staff, Guid bookingId, Guid lineId, Guid newRoomTypeId, int quantity) =>
        staff.PostAsJsonAsync($"/api/v1/reception/bookings/{bookingId}/change-room-type",
            new { bookingRoomTypeId = lineId, newRoomTypeId, quantity });

    private static async Task<JsonElement> GetDetailAsync(HttpClient staff, Guid bookingId)
    {
        var response = await staff.GetAsync($"/api/v1/reception/bookings/{bookingId}");
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private static async Task<Guid> FirstAssignmentIdAsync(HttpClient staff, Guid bookingId)
    {
        var detail = await GetDetailAsync(staff, bookingId);
        return detail.GetProperty("assignments").EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> FreeRoomIdsAsync(
        HttpClient staff, Guid roomTypeId, DateOnly from, DateOnly to)
    {
        var response = await staff.GetAsync(
            $"/api/v1/reception/rooms?roomTypeId={roomTypeId}&availableFrom={from:yyyy-MM-dd}&availableTo={to:yyyy-MM-dd}&pageSize=100");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<(Guid RoomId, string RoomNumber)> FindFreeRoomAsync(
        HttpClient staff, Guid roomTypeId, DateOnly from, DateOnly to, Guid? exclude = null)
    {
        var response = await staff.GetAsync(
            $"/api/v1/reception/rooms?roomTypeId={roomTypeId}&availableFrom={from:yyyy-MM-dd}&availableTo={to:yyyy-MM-dd}&pageSize=100");
        response.EnsureSuccessStatusCode();

        foreach (var item in (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetGuid();
            if (exclude is not null && id == exclude)
                continue;
            return (id, item.GetProperty("roomNumber").GetString()!);
        }

        throw new InvalidOperationException("No free room was found for the requested range.");
    }

    private static async Task<Guid> AnyRoomOfTypeAsync(HttpClient staff, Guid roomTypeId)
    {
        var response = await staff.GetAsync($"/api/v1/reception/rooms?roomTypeId={roomTypeId}&pageSize=100");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .First().GetProperty("id").GetGuid();
    }

    private async Task<Booking> GetBookingEntityAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == id);
    }

    private async Task<bool> HasLiveAssignmentAsync(Guid bookingId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.RoomAssignments.AnyAsync(ra => ra.BookingId == bookingId && ra.ReleasedAt == null);
    }

    private async Task<Guid> LiveAssignedRoomIdAsync(Guid bookingId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.RoomAssignments.Where(ra => ra.BookingId == bookingId && ra.ReleasedAt == null)
            .Select(ra => ra.RoomId).FirstAsync();
    }

    private async Task<RoomStatus> RoomStatusAsync(Guid roomId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Rooms.Where(r => r.Id == roomId).Select(r => r.Status).FirstAsync();
    }

    private async Task<ActivityLog?> LatestActivityLogAsync(Guid bookingId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.ActivityLogs
            .Where(a => a.EntityId == bookingId && a.EntityType == "Booking")
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task SetSettingAsync(HttpClient admin, string key, bool value)
    {
        var response = await admin.PutAsJsonAsync("/api/v1/admin/settings", new Dictionary<string, bool> { [key] = value });
        response.EnsureSuccessStatusCode();
    }
}
