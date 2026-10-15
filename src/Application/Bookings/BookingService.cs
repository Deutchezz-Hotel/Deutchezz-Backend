using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
using Deutchezz.Application.Stays;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Deutchezz.Application.Bookings;

public class BookingService(
    IAppDbContext db,
    IBookingReferenceGenerator referenceGenerator,
    IClock clock,
    IBusinessClock businessClock,
    IEmailSender emailSender,
    IActivityLogger activityLogger,
    ISettingsProvider settings,
    IStayPolicyService stays,
    IPricingService pricing,
    IStayConflictService conflicts,
    IOptions<EmailOptions> emailOptions,
    IOptions<BookingRetentionOptions> retention) : IBookingService
{

    private static readonly Dictionary<string, Expression<Func<Booking, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = b => b.CreatedAt,
            ["checkInDate"] = b => b.CheckInDate,
            ["checkOutDate"] = b => b.CheckOutDate,
            ["checkInAt"] = b => b.CheckInAt,
            ["checkOutAt"] = b => b.CheckOutAt,
            ["bookingReference"] = b => b.BookingReference,
            ["totalAmount"] = b => b.TotalAmount,
            ["status"] = b => b.Status
        };

    private TimeSpan LagosOffset() => businessClock.ConvertFromUtc(businessClock.UtcNow).Offset;

    /// <summary>
    /// Defensive guard for the legacy <c>-infinity</c> sentinel (see the
    /// <c>FixLegacyStayInstants</c> migration): a stay instant that is unset (year &lt;= 1) is
    /// resolved from its scheduled date and the historical 14:00/12:00 Lagos defaults so the API
    /// can never render the bogus "01:00" that <c>MinValue + Lagos offset</c> produced.
    /// </summary>
    private DateTime ResolveInstant(DateTime instant, DateOnly date, TimeOnly fallback, TimeSpan offset)
    {
        if (instant.Year > 1)
            return instant;

        return new DateTimeOffset(date.ToDateTime(fallback), offset).UtcDateTime;
    }

    public async Task<Result<CreateBookingResponse>> CreateAsync(
        CreateBookingRequest request, Guid? userId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("booking.idempotency_key_required", "The Idempotency-Key header is required");

        var stay = await stays.ResolveAsync(
            request.CheckInDate, request.CheckInTime, request.CheckOutDate, request.CheckOutTime, false, cancellationToken);
        if (stay.IsFailure)
            return RemapPastCheckIn(stay.Error);
        var s = stay.Value;

        var minimumCheckOut = CheckMinimumCheckOut(s);
        if (minimumCheckOut is not null)
            return minimumCheckOut;

        if (s.Nights > s.Policy.MaxStayNights)
            return Error.Validation("booking.too_long", $"Stay cannot exceed {s.Policy.MaxStayNights} nights");
        if (request.Rooms is null || request.Rooms.Count == 0)
            return Error.Validation("booking.no_rooms", "At least one room type is required");
        if (request.Rooms.Any(r => r.Quantity < 1))
            return Error.Validation("booking.invalid_quantity", "Room quantity must be at least 1");
        if (request.Rooms.Select(r => r.RoomTypeId).Distinct().Count() != request.Rooms.Count)
            return Error.Validation("booking.duplicate_roomtype", "Room types must be unique within a booking");
        if (request.Adults < 1)
            return Error.Validation("booking.invalid_adults", "At least one adult is required");
        if (request.Children < 0)
            return Error.Validation("booking.invalid_children", "Children cannot be negative");
        if (string.IsNullOrWhiteSpace(request.Guest.Email))
            return Error.Validation("booking.email_required", "Guest email is required");
        if (string.IsNullOrWhiteSpace(request.Guest.FirstName) || string.IsNullOrWhiteSpace(request.Guest.LastName))
            return Error.Validation("booking.name_required", "Guest first and last name are required");
        if (string.IsNullOrWhiteSpace(request.Guest.PhoneNumber))
            return Error.Validation("booking.phone_required", "Guest phone number is required");
        if (request.Guest.Email.IndexOf('@') < 0)
            return Error.Validation("booking.invalid_email", "Guest email is invalid");

        var requestHash = ComputeRequestHash(request);

        var existing = await db.Bookings.FirstOrDefaultAsync(
            b => b.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return await ReplayOrConflictAsync(existing, requestHash, cancellationToken);

        var roomTypeIds = request.Rooms.Select(r => r.RoomTypeId).ToList();

        try
        {
            return await CreateInTransactionAsync(
                request, userId, idempotencyKey, requestHash, roomTypeIds, s, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyConflict(ex))
        {
            // A concurrent request with the same key won the race inside the transaction.
            var winner = await db.Bookings.FirstOrDefaultAsync(
                b => b.IdempotencyKey == idempotencyKey, cancellationToken);
            return winner is not null
                ? await ReplayOrConflictAsync(winner, requestHash, cancellationToken)
                : Error.Conflict("booking.unavailable",
                    "The booking could not be completed due to concurrent activity. Please try again.");
        }
    }

    /// <summary>
    /// Rule 8 (SoT v1.15): a nightly stay cannot check out before check-in + 24h × nights. The
    /// default check-out already mirrors the check-in time, so this rejects an explicit earlier one.
    /// </summary>
    private static Error? CheckMinimumCheckOut(StayWindow stay)
    {
        var minimumCheckOutAt = stay.CheckInAt.AddHours(24L * stay.Nights);
        if (stay.CheckOutAt >= minimumCheckOutAt)
            return null;

        return Error.Validation("booking.checkout_too_early",
            "Check-out must be at least 24 hours after check-in for each booked night. "
            + "Choose a later check-out time or add a night.");
    }

    /// <summary>
    /// Rule 8 (SoT v1.15): a past check-in is rejected by the stay policy as <c>stay.in_the_past</c>;
    /// booking creation surfaces it under the booking-specific code so the UI can point at the
    /// check-in field.
    /// </summary>
    private static Error RemapPastCheckIn(Error error) =>
        error.Code == "stay.in_the_past"
            ? Error.Validation("booking.checkin_in_past",
                "Check-in time cannot be in the past. Choose the next available time.")
            : error;

    public async Task<Result<CreateBookingResponse>> ReceptionCreateAsync(
        ReceptionCreateBookingRequest request, Guid staffUserId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("booking.idempotency_key_required", "The Idempotency-Key header is required");

        var stay = await stays.ResolveAsync(
            request.CheckInDate, request.CheckInTime, request.CheckOutDate, request.CheckOutTime, true, cancellationToken);
        if (stay.IsFailure)
            return RemapPastCheckIn(stay.Error);
        var s = stay.Value;

        var minimumCheckOut = CheckMinimumCheckOut(s);
        if (minimumCheckOut is not null)
            return minimumCheckOut;

        if (s.Nights > s.Policy.MaxStayNights)
            return Error.Validation("booking.too_long", $"Stay cannot exceed {s.Policy.MaxStayNights} nights");
        if (request.Rooms is null || request.Rooms.Count == 0)
            return Error.Validation("booking.no_rooms", "At least one room type is required");
        if (request.Rooms.Any(r => r.Quantity < 1))
            return Error.Validation("booking.invalid_quantity", "Room quantity must be at least 1");
        if (request.Rooms.Select(r => r.RoomTypeId).Distinct().Count() != request.Rooms.Count)
            return Error.Validation("booking.duplicate_roomtype", "Room types must be unique within a booking");
        if (request.Adults < 1)
            return Error.Validation("booking.invalid_adults", "At least one adult is required");
        if (request.Children < 0)
            return Error.Validation("booking.invalid_children", "Children cannot be negative");
        if (request.Guest is null)
            return Error.Validation("booking.guest_required", "Guest details are required");
        if (string.IsNullOrWhiteSpace(request.Guest.FirstName) || string.IsNullOrWhiteSpace(request.Guest.LastName))
            return Error.Validation("booking.name_required", "Guest first and last name are required");
        if (string.IsNullOrWhiteSpace(request.Guest.PhoneNumber))
            return Error.Validation("booking.phone_required", "Guest phone number is required");
        if (!PhoneNumber.TryNormalize(request.Guest.PhoneNumber, out var normalizedPhone) || normalizedPhone is null)
            return Error.Validation("booking.invalid_phone",
                "Enter a valid phone number, for example 0803 123 4567");

        var requestHash = ComputeRequestHash(request);

        var existing = await db.Bookings.FirstOrDefaultAsync(
            b => b.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return await ReplayOrConflictAsync(existing, requestHash, cancellationToken);

        var roomTypeIds = request.Rooms.Select(r => r.RoomTypeId).ToList();

        try
        {
            return await ReceptionCreateInTransactionAsync(
                request, staffUserId, normalizedPhone, idempotencyKey, requestHash, roomTypeIds, s, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyConflict(ex))
        {
            var winner = await db.Bookings.FirstOrDefaultAsync(
                b => b.IdempotencyKey == idempotencyKey, cancellationToken);
            return winner is not null
                ? await ReplayOrConflictAsync(winner, requestHash, cancellationToken)
                : Error.Conflict("booking.unavailable",
                    "The booking could not be completed due to concurrent activity. Please try again.");
        }
    }

    private async Task<Result<CreateBookingResponse>> ReceptionCreateInTransactionAsync(
        ReceptionCreateBookingRequest request, Guid staffUserId, string normalizedPhone, string idempotencyKey,
        string requestHash, List<Guid> roomTypeIds, StayWindow s, CancellationToken cancellationToken)
    {
        var nights = s.Nights;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var availability = await ReserveAvailabilityAsync(
            roomTypeIds, request.Rooms, s.CheckInAt, s.CheckOutAt, s.Policy.TurnaroundMinutes, cancellationToken);
        if (availability.IsFailure)
            return availability.Error;
        var roomTypes = availability.Value;

        var guest = await FindOrCreateWalkInGuestAsync(request.Guest, normalizedPhone, cancellationToken);

        var booking = new Booking
        {
            BookingReference = referenceGenerator.Generate(),
            GuestId = guest.Id,
            Adults = request.Adults,
            Children = request.Children,
            SpecialRequests = null,
            Source = BookingSource.WalkIn,
            // Pay-at-hotel: walk-in bookings are created Confirmed with payment Pending.
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            CreatedByUserId = staffUserId,
            IdempotencyKey = idempotencyKey,
            IdempotencyRequestHash = requestHash,
            ConcurrencyToken = Guid.NewGuid().ToByteArray()
        };
        booking.SetStay(s.CheckInDate, s.CheckInTime, s.CheckOutDate, s.CheckOutTime, LagosOffset());

        var tiers = await pricing.GetTiersAsync(roomTypeIds, cancellationToken);

        var total = 0m;
        var extraTotal = 0m;
        foreach (var line in request.Rooms)
        {
            var rate = roomTypes[line.RoomTypeId].BasePrice;
            var priced = StayPricing.ComputeLine(
                rate, line.Quantity, s.Nights, s.Duration, tiers[line.RoomTypeId], s.Policy.GraceHours);
            var subtotal = rate * line.Quantity * priced.Nights;
            total += subtotal + priced.OverageAmount;
            extraTotal += priced.OverageAmount;
            db.BookingRoomTypes.Add(new BookingRoomType
            {
                BookingId = booking.Id,
                RoomTypeId = line.RoomTypeId,
                Quantity = line.Quantity,
                RatePerNight = rate,
                NumberOfNights = priced.Nights,
                Subtotal = subtotal,
                ExtraCharge = priced.OverageAmount,
                SurchargeTier = priced.TierLabel
            });
        }
        booking.ExtraChargeAmount = extraTotal;
        booking.TotalAmount = total;

        db.Bookings.Add(booking);

        // SoT 7g / BR-036: the walk-in guest has no account, so no Notification row is created.

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var guestName = $"{request.Guest.FirstName} {request.Guest.LastName}".Trim();
        await activityLogger.LogAsync(staffUserId, "booking.created", "Booking", booking.Id,
            $"Walk-in booking {booking.BookingReference} created for {guestName} ({normalizedPhone})", cancellationToken);

        // BR-036: exactly one booking-summary email, sent to the creating staff member's own address.
        var staffEmail = await db.Users
            .Where(u => u.Id == staffUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(staffEmail))
        {
            var summaryRooms = request.Rooms
                .Select(r =>
                {
                    var priced = StayPricing.ComputeLine(
                        roomTypes[r.RoomTypeId].BasePrice, r.Quantity, s.Nights, s.Duration,
                        tiers[r.RoomTypeId], s.Policy.GraceHours);
                    return new BookingConfirmationRoom(
                        roomTypes[r.RoomTypeId].Name,
                        r.Quantity,
                        roomTypes[r.RoomTypeId].BasePrice,
                        roomTypes[r.RoomTypeId].BasePrice * r.Quantity * priced.Nights,
                        priced.OverageAmount);
                })
                .ToList();
            var (subject, html, text) = ReceptionBookingSummaryEmail.Build(new ReceptionBookingSummaryModel(
                booking.BookingReference,
                guestName,
                normalizedPhone,
                s.CheckInDate,
                s.CheckInTime,
                s.CheckOutDate,
                s.CheckOutTime,
                nights,
                summaryRooms,
                total));
            await emailSender.SendAsync(staffEmail, subject, html, text, CancellationToken.None);
        }

        var response = await MapAsync(booking.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: false);
    }

    /// <summary>
    /// BR-035: a walk-in guest is stored without an email and never linked to an account. A prior
    /// account-less, email-less guest with the same normalized phone is reused to avoid duplicates.
    /// </summary>
    private async Task<Guest> FindOrCreateWalkInGuestAsync(
        ReceptionGuestRequest info, string normalizedPhone, CancellationToken cancellationToken)
    {
        var existing = await db.Guests.FirstOrDefaultAsync(
            g => g.UserId == null && g.Email == null && g.PhoneNumber == normalizedPhone, cancellationToken);
        if (existing is not null)
            return existing;

        var guest = new Guest
        {
            UserId = null,
            FirstName = info.FirstName.Trim(),
            LastName = info.LastName.Trim(),
            Email = null,
            PhoneNumber = normalizedPhone
        };
        db.Guests.Add(guest);
        return guest;
    }

    private async Task<Result<CreateBookingResponse>> CreateInTransactionAsync(
        CreateBookingRequest request, Guid? userId, string idempotencyKey, string requestHash,
        List<Guid> roomTypeIds, StayWindow s, CancellationToken cancellationToken)
    {
        var nights = s.Nights;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SoT v1.10, 7m / BR-041: warn on a likely duplicate public booking before reserving stock.
        var duplicate = await FindDuplicateWarningAsync(request, userId, cancellationToken);
        if (duplicate is not null && !request.AcknowledgeDuplicate)
        {
            var minutesAgo = (int)Math.Max(0, Math.Floor((clock.UtcNow - duplicate.CreatedAt).TotalMinutes));
            var extensions = new Dictionary<string, object?>
            {
                ["minutesAgo"] = minutesAgo,
                ["reference"] = userId is not null
                    ? duplicate.BookingReference
                    : MaskReference(duplicate.BookingReference)
            };
            if (userId is not null)
                extensions["bookingId"] = duplicate.Id;

            return Error.Conflict(
                "booking.possible_duplicate",
                "A booking for this guest was created a short time ago. Review the existing booking, "
                + "then resubmit with acknowledgeDuplicate set to true to continue.")
                .WithExtensions(extensions);
        }

        var availability = await ReserveAvailabilityAsync(
            roomTypeIds, request.Rooms, s.CheckInAt, s.CheckOutAt, s.Policy.TurnaroundMinutes, cancellationToken);
        if (availability.IsFailure)
            return availability.Error;
        var roomTypes = availability.Value;

        var guest = await FindOrCreateGuestAsync(request.Guest, userId, cancellationToken);

        var booking = new Booking
        {
            BookingReference = referenceGenerator.Generate(),
            GuestId = guest.Id,
            Adults = request.Adults,
            Children = request.Children,
            SpecialRequests = request.SpecialRequests,
            Source = BookingSource.Online,
            // Pay-at-hotel (SoT v1.1): new bookings are created Confirmed with payment Pending.
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            CreatedByUserId = userId,
            IdempotencyKey = idempotencyKey,
            IdempotencyRequestHash = requestHash,
            ConcurrencyToken = Guid.NewGuid().ToByteArray()
        };
        booking.SetStay(s.CheckInDate, s.CheckInTime, s.CheckOutDate, s.CheckOutTime, LagosOffset());

        var tiers = await pricing.GetTiersAsync(roomTypeIds, cancellationToken);

        var total = 0m;
        var extraTotal = 0m;
        foreach (var line in request.Rooms)
        {
            var rate = roomTypes[line.RoomTypeId].BasePrice;
            var priced = StayPricing.ComputeLine(
                rate, line.Quantity, s.Nights, s.Duration, tiers[line.RoomTypeId], s.Policy.GraceHours);
            var subtotal = rate * line.Quantity * priced.Nights;
            total += subtotal + priced.OverageAmount;
            extraTotal += priced.OverageAmount;
            db.BookingRoomTypes.Add(new BookingRoomType
            {
                BookingId = booking.Id,
                RoomTypeId = line.RoomTypeId,
                Quantity = line.Quantity,
                RatePerNight = rate,
                NumberOfNights = priced.Nights,
                Subtotal = subtotal,
                ExtraCharge = priced.OverageAmount,
                SurchargeTier = priced.TierLabel
            });
        }
        booking.ExtraChargeAmount = extraTotal;
        booking.TotalAmount = total;

        db.Bookings.Add(booking);

        // SoT v1.1: the confirmation Notification is only created when the guest has an account.
        if (guest.UserId is not null)
        {
            db.Notifications.Add(new Notification
            {
                GuestId = guest.Id,
                UserId = guest.UserId,
                Type = NotificationType.BookingConfirmed,
                Title = "Booking confirmed",
                Message = $"Your booking {booking.BookingReference} is confirmed. "
                    + StayFormat.ScheduleLine("Check-in", s.CheckInDate, s.CheckInTime, true) + ".",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.created", "Booking", booking.Id,
            $"Booking {booking.BookingReference} created for {request.Guest.Email}", cancellationToken);

        var emailRooms = request.Rooms
            .Select(r =>
            {
                var priced = StayPricing.ComputeLine(
                    roomTypes[r.RoomTypeId].BasePrice, r.Quantity, s.Nights, s.Duration,
                    tiers[r.RoomTypeId], s.Policy.GraceHours);
                return new BookingConfirmationRoom(
                    roomTypes[r.RoomTypeId].Name,
                    r.Quantity,
                    roomTypes[r.RoomTypeId].BasePrice,
                    roomTypes[r.RoomTypeId].BasePrice * r.Quantity * priced.Nights,
                    priced.OverageAmount);
            })
            .ToList();
        var (subject, html, text) = BookingConfirmationEmail.Build(new BookingConfirmationModel(
            booking.BookingReference,
            $"{request.Guest.FirstName} {request.Guest.LastName}".Trim(),
            s.CheckInDate,
            s.CheckInTime,
            s.CheckOutDate,
            s.CheckOutTime,
            nights,
            emailRooms,
            total));

        // Await the sends sequentially and with CancellationToken.None. Fire-and-forget sends
        // could be aborted when the request scope is torn down, and two concurrent logins to the
        // same SMTP account can be throttled (one message silently dropped). Awaiting makes the
        // result deterministic and observable.
        await emailSender.SendAsync(request.Guest.Email, subject, html, text, CancellationToken.None);

        var deskAddress = emailOptions.Value.AdminNotificationAddress;
        if (!string.IsNullOrWhiteSpace(deskAddress))
        {
            var (alertSubject, alertHtml, alertText) = StaffNewBookingAlertEmail.Build(new StaffNewBookingAlertModel(
                booking.BookingReference,
                $"{request.Guest.FirstName} {request.Guest.LastName}".Trim(),
                request.Guest.Email,
                request.Guest.PhoneNumber ?? string.Empty,
                s.CheckInDate,
                s.CheckInTime,
                s.CheckOutDate,
                s.CheckOutTime,
                nights,
                emailRooms,
                total));
            await emailSender.SendAsync(deskAddress, alertSubject, alertHtml, alertText, CancellationToken.None);
        }

        var response = await MapAsync(booking.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: userId is null);
    }

    /// <summary>
    /// SoT 7c: inside the caller's transaction, take <c>SELECT … FOR UPDATE</c> row locks on the
    /// requested RoomType rows in ascending Id order (deadlock avoidance) and verify capacity.
    /// Concurrent bookings for the same room types serialize, so the availability check cannot
    /// overbook. Returns the locked room types keyed by Id, or a validation/conflict error.
    /// </summary>
    private async Task<Result<Dictionary<Guid, RoomType>>> ReserveAvailabilityAsync(
        List<Guid> roomTypeIds, IReadOnlyList<BookingRoomRequest> rooms, DateTime checkInAt, DateTime checkOutAt,
        int turnaroundMinutes, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"RoomTypes\" WHERE \"Id\" = ANY({0}) ORDER BY \"Id\" FOR UPDATE",
            new object[] { roomTypeIds }, cancellationToken);

        var lockedRoomTypes = await db.RoomTypes
            .Where(rt => roomTypeIds.Contains(rt.Id))
            .ToListAsync(cancellationToken);

        if (lockedRoomTypes.Count != roomTypeIds.Count || lockedRoomTypes.Any(rt => !rt.IsActive))
            return Error.Validation("booking.roomtype_invalid", "One or more room types are invalid or inactive");

        var roomTypes = lockedRoomTypes.ToDictionary(rt => rt.Id);

        foreach (var line in rooms)
        {
            var capacity = await db.Rooms.CountAsync(
                r => r.RoomTypeId == line.RoomTypeId && r.IsActive
                    && r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive,
                cancellationToken);

            var booked = await conflicts.CountOverlappingQuantityAsync(
                line.RoomTypeId, null, checkInAt, checkOutAt, turnaroundMinutes, cancellationToken);

            if (capacity - booked < line.Quantity)
                return Error.Conflict("booking.unavailable",
                    $"Only {Math.Max(0, capacity - booked)} room(s) available for '{roomTypes[line.RoomTypeId].Name}' for the selected dates");
        }

        return roomTypes;
    }

    private async Task<Result<CreateBookingResponse>> ReplayOrConflictAsync(Booking existing, string requestHash, CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
            return Error.Conflict("booking.idempotency_mismatch",
                "The Idempotency-Key was already used with a different request payload");

        var response = await MapAsync(existing.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: false, Replay: true);
    }

    private static string ComputeRequestHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);

    private async Task<Guest> FindOrCreateGuestAsync(GuestInfoRequest info, Guid? userId, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(info.Email);

        // Resolve the account that owns this booking: the authenticated caller, or — for a
        // guest checkout — the registered account with the same email. Without this second
        // lookup a logged-in user whose booking request arrives without a bearer token (only a
        // valid email) gets an orphaned anonymous guest, so the booking never shows in their
        // dashboard and reuses a stale guest name.
        var owner = userId is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : await db.Users.FirstOrDefaultAsync(
                u => u.Email != null && u.Email.Trim().ToLower() == normalizedEmail, cancellationToken);

        if (owner is not null)
        {
            var existing = await db.Guests.FirstOrDefaultAsync(g => g.UserId == owner.Id, cancellationToken);
            if (existing is not null)
                return existing;

            // Adopt a pre-existing anonymous guest with the same email instead of duplicating it,
            // and refresh its name from the account so reports show the correct guest.
            var orphan = await db.Guests.FirstOrDefaultAsync(
                g => g.UserId == null && g.Email != null && g.Email.Trim().ToLower() == normalizedEmail, cancellationToken);
            if (orphan is not null)
            {
                orphan.UserId = owner.Id;
                if (!string.IsNullOrWhiteSpace(owner.FirstName)) orphan.FirstName = owner.FirstName;
                if (!string.IsNullOrWhiteSpace(owner.LastName)) orphan.LastName = owner.LastName;
                return orphan;
            }

            var created = new Guest
            {
                UserId = owner.Id,
                FirstName = !string.IsNullOrWhiteSpace(owner.FirstName) ? owner.FirstName : info.FirstName,
                LastName = !string.IsNullOrWhiteSpace(owner.LastName) ? owner.LastName : info.LastName,
                Email = info.Email,
                PhoneNumber = info.PhoneNumber ?? owner.PhoneNumber
            };
            db.Guests.Add(created);
            return created;
        }

        // Anonymous booking with no matching account: reuse an existing anonymous guest by email.
        var existingAnonymous = await db.Guests
            .FirstOrDefaultAsync(g => g.UserId == null && g.Email != null && g.Email.Trim().ToLower() == normalizedEmail, cancellationToken);
        if (existingAnonymous is not null)
            return existingAnonymous;

        var anonymous = new Guest
        {
            FirstName = info.FirstName,
            LastName = info.LastName,
            Email = info.Email,
            PhoneNumber = info.PhoneNumber
        };
        db.Guests.Add(anonymous);
        return anonymous;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>SoT v1.10, 7m / BR-041. Returns the most recent not-yet-checked-in booking by the
    /// same guest within the configured window, or null. Matching is by owning account for an
    /// authenticated caller, otherwise by the anonymous guest's normalized email or phone.</summary>
    private async Task<Booking?> FindDuplicateWarningAsync(
        CreateBookingRequest request, Guid? userId, CancellationToken cancellationToken)
    {
        var windowMinutes = settings.DuplicateWarningMinutes;
        if (windowMinutes <= 0)
            return null;

        var cutoff = clock.UtcNow.AddMinutes(-windowMinutes);

        var candidates = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Guest)
            .Where(b => (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed)
                        && b.CreatedAt >= cutoff)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        if (userId is not null)
            return candidates.FirstOrDefault(b => b.Guest is not null && b.Guest.UserId == userId);

        var normalizedEmail = NormalizeEmail(request.Guest.Email);
        var rawPhone = string.IsNullOrWhiteSpace(request.Guest.PhoneNumber)
            ? null
            : request.Guest.PhoneNumber.Trim();
        var normalizedPhone = PhoneNumber.TryNormalize(rawPhone, out var np) ? np : null;

        return candidates.FirstOrDefault(b =>
        {
            var guest = b.Guest;
            if (guest is null || guest.UserId is not null)
                return false;

            var emailMatch = guest.Email is not null && NormalizeEmail(guest.Email) == normalizedEmail;

            var phoneMatch = false;
            if (rawPhone is not null && guest.PhoneNumber is not null)
            {
                var guestRaw = guest.PhoneNumber.Trim();
                var guestNormalized = PhoneNumber.TryNormalize(guest.PhoneNumber, out var gn) ? gn : null;
                phoneMatch = guestRaw == rawPhone
                    || (normalizedPhone is not null && guestNormalized == normalizedPhone);
            }

            return emailMatch || phoneMatch;
        });
    }

    /// <summary>SoT v1.10, 7m. Masks a booking reference for an anonymous caller, keeping only the
    /// <c>DHI-&lt;year&gt;-</c> prefix and the last two suffix characters (e.g. <c>DHI-2026-****34</c>).</summary>
    private static string MaskReference(string reference)
    {
        var lastDash = reference.LastIndexOf('-');
        if (lastDash < 0 || lastDash == reference.Length - 1)
            return new string('*', reference.Length);

        var prefix = reference[..(lastDash + 1)];
        var suffix = reference[(lastDash + 1)..];
        return suffix.Length <= 2
            ? prefix + new string('*', suffix.Length)
            : prefix + new string('*', suffix.Length - 2) + suffix[^2..];
    }

    private async Task<string?> GetUserEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken)
            is { Length: > 0 } email
            ? NormalizeEmail(email)
            : null;

    public async Task<Result<BookingDetailResponse>> LookupAsync(BookingLookupRequest request, CancellationToken cancellationToken)
    {
        var reference = request.BookingReference?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();

        var booking = await db.Bookings
            .Include(b => b.Guest)
            .FirstOrDefaultAsync(b => b.BookingReference == reference, cancellationToken);

        if (booking is null || booking.Guest is null || string.IsNullOrWhiteSpace(booking.Guest.Email) ||
            !string.Equals(booking.Guest.Email.Trim().ToLowerInvariant(), email, StringComparison.Ordinal))
        {
            // Generic not-found: never reveal which part was wrong.
            return Error.NotFound("booking.not_found", "Booking not found");
        }

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingDetailResponse>> CancelAsync(Guid? id, BookingLookupRequest? lookup, string? reason, Guid? userId, CancellationToken cancellationToken)
    {
        Booking? booking;

        if (userId is not null)
        {
            var ownerEmail = await GetUserEmailAsync(userId.Value, cancellationToken);
            booking = await db.Bookings
                .Include(b => b.Guest)
                .FirstOrDefaultAsync(b => (id == null || b.Id == id) && b.Guest != null &&
                    (b.Guest.UserId == userId ||
                     (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email != null && b.Guest.Email.Trim().ToLower() == ownerEmail)),
                    cancellationToken);
        }
        else if (lookup is not null)
        {
            var reference = lookup.BookingReference?.Trim();
            var email = lookup.Email?.Trim().ToLowerInvariant();
            booking = await db.Bookings
                .Include(b => b.Guest)
                .FirstOrDefaultAsync(b => b.BookingReference == reference, cancellationToken);

            if (booking is null || booking.Guest is null || string.IsNullOrWhiteSpace(booking.Guest.Email) ||
                !string.Equals(booking.Guest.Email.Trim().ToLowerInvariant(), email, StringComparison.Ordinal))
            {
                return Error.NotFound("booking.not_found", "Booking not found");
            }
        }
        else
        {
            return Error.NotFound("booking.not_found", "Booking not found");
        }

        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.CheckedIn or BookingStatus.CheckedOut)
            return Error.Conflict("booking.not_cancellable", $"Booking in status '{booking.Status}' cannot be cancelled");

        // BR-024: a booking can only be cancelled freely until CancellationWindowHours before the
        // check-in day starts (Africa/Lagos). Cancellations are rejected once the deadline has passed.
        var windowHours = await CancellationWindowHoursAsync(cancellationToken);
        if (!IsWithinCancellationWindow(booking, windowHours))
            return Error.Conflict("booking.cancellation_window_closed",
                "This booking can no longer be cancelled (the free cancellation window has closed)");

        // BR-025: any amount already paid is refunded in full on a permitted cancellation.
        var refundedAmount = await RefundPaidPaymentsAsync(booking, cancellationToken);
        if (refundedAmount > 0)
            booking.PaymentStatus = PaymentStatus.Refunded;

        // BR-018: a pre-check-in cancellation releases any live room assignments immediately so
        // availability is freed; otherwise the rooms stay held by a cancelled booking.
        var liveAssignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == booking.Id && ra.ReleasedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var assignment in liveAssignments)
            assignment.ReleasedAt = clock.UtcNow;

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = clock.UtcNow;
        booking.CancellationReason = reason;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        // BR-036: only notify a guest who has an account (anonymous/customer cancels alike).
        if (booking.Guest?.UserId is not null)
        {
            db.Notifications.Add(new Notification
            {
                GuestId = booking.GuestId,
                UserId = booking.Guest.UserId,
                Type = NotificationType.BookingCancelled,
                Title = "Booking cancelled",
                Message = refundedAmount > 0
                    ? $"Booking {booking.BookingReference} has been cancelled. {refundedAmount} was refunded."
                    : $"Booking {booking.BookingReference} has been cancelled.",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.cancelled", "Booking", booking.Id,
            refundedAmount > 0 ? $"{reason ?? "Booking cancelled"} · refunded {refundedAmount}" : reason ?? "Booking cancelled",
            cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var email = booking.Guest.Email;
            var (subject, html, text) = BookingCancellationEmail.Build(new BookingCancellationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                booking.CheckInDate,
                booking.CheckInLocalTime(LagosOffset()),
                booking.CheckOutDate,
                booking.CheckOutLocalTime(LagosOffset()),
                reason,
                refundedAmount > 0 ? refundedAmount : null));
            _ = emailSender.SendAsync(email, subject, html, text, CancellationToken.None);
        }

        return await MapAsync(booking.Id, cancellationToken);
    }

    /// <summary>
    /// BR-034 / SoT 7g: a receptionist or administrator cancels a Pending/Confirmed booking with an
    /// explicit reason and refund choice. Not bound by the guest cancellation window.
    /// </summary>
    public async Task<Result<BookingDetailResponse>> ReceptionCancelAsync(
        Guid id, ReceptionCancelBookingRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(b => b.Guest)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Error.Conflict("booking.not_cancellable", $"Booking in status '{booking.Status}' cannot be cancelled");

        if (request.Reason == StaffCancellationReason.Other && string.IsNullOrWhiteSpace(request.Notes))
            return Error.Validation("booking.notes_required", "Notes are required when the reason is 'Other'");

        if (request.Reason == StaffCancellationReason.NoShow && booking.CheckInDate > businessClock.Today)
            return Error.Conflict("booking.noshow_too_early",
                "A no-show can only be recorded on or after the check-in date");

        var refundedAmount = 0m;
        if (request.RefundPaidAmounts)
        {
            refundedAmount = await RefundPaidPaymentsAsync(booking, cancellationToken);
            if (refundedAmount > 0)
                booking.PaymentStatus = PaymentStatus.Refunded;
        }

        // BR-018: release the live assignments so availability is freed immediately.
        var assignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.ReleasedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments)
            assignment.ReleasedAt = clock.UtcNow;

        var guestName = booking.Guest is not null
            ? $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim()
            : string.Empty;

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = clock.UtcNow;
        booking.CancellationReason = string.IsNullOrWhiteSpace(request.Notes)
            ? request.Reason.ToString()
            : $"{request.Reason}: {request.Notes}";
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        // BR-036: only notify a guest who has an account.
        if (booking.Guest?.UserId is not null)
        {
            db.Notifications.Add(new Notification
            {
                GuestId = booking.GuestId,
                UserId = booking.Guest.UserId,
                Type = NotificationType.BookingCancelled,
                Title = "Booking cancelled",
                Message = refundedAmount > 0
                    ? $"Booking {booking.BookingReference} has been cancelled. {refundedAmount} was refunded."
                    : $"Booking {booking.BookingReference} has been cancelled.",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var refundNote = request.RefundPaidAmounts
            ? (refundedAmount > 0 ? $"refunded {refundedAmount}" : "refund requested, nothing paid")
            : "payments kept";
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $" ({request.Notes})";
        await activityLogger.LogAsync(staffUserId, "booking.cancelled", "Booking", booking.Id,
            $"Cancelled {booking.BookingReference} for {guestName} — reason {request.Reason}{notes}, {refundNote}",
            cancellationToken);

        // BR-036: a guest with no email (walk-ins) receives nothing.
        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var email = booking.Guest.Email;
            var (subject, html, text) = BookingCancellationEmail.Build(new BookingCancellationModel(
                booking.BookingReference,
                guestName,
                booking.CheckInDate,
                booking.CheckInLocalTime(LagosOffset()),
                booking.CheckOutDate,
                booking.CheckOutLocalTime(LagosOffset()),
                request.Reason.ToString(),
                request.RefundPaidAmounts && refundedAmount > 0 ? refundedAmount : null));
            await emailSender.SendAsync(email, subject, html, text, CancellationToken.None);
        }

        return await MapAsync(booking.Id, cancellationToken);
    }

    /// <summary>Whether <paramref name="booking"/> is currently before the free-cancellation
    /// deadline (BR-024): now &lt; the check-in instant minus the cancellation window.</summary>
    private bool IsWithinCancellationWindow(Booking booking, int windowHours)
    {
        var nowUtc = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        return nowUtc < booking.CheckInAt.AddHours(-windowHours);
    }

    /// <summary>Marks every Paid payment of the booking as Refunded and returns the total refunded.</summary>
    private async Task<decimal> RefundPaidPaymentsAsync(Booking booking, CancellationToken cancellationToken)
    {
        var payments = await db.Payments
            .Where(p => p.BookingId == booking.Id && p.Status == PaymentStatus.Paid)
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return 0m;

        var total = payments.Sum(p => p.Amount);
        foreach (var payment in payments)
        {
            payment.Status = PaymentStatus.Refunded;
            payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
                ? "Refunded on cancellation"
                : $"{payment.Notes} · Refunded on cancellation";
        }
        return total;
    }

    /// <summary>The cancellation window in hours, honouring the admin-editable DB setting when set.</summary>
    private async Task<int> CancellationWindowHoursAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Settings.FirstOrDefaultAsync(s => s.Key == "CancellationWindowHours", cancellationToken);
        return stored is not null && int.TryParse(stored.Value, out var parsed) && parsed > 0
            ? parsed
            : settings.CancellationWindowHours;
    }

    /// <summary>A DB-backed boolean gate with an appsettings fallback (7f).</summary>
    private async Task<bool> BoolSettingAsync(string key, bool fallback, CancellationToken cancellationToken)
    {
        var stored = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return stored is not null && bool.TryParse(stored.Value, out var parsed) ? parsed : fallback;
    }

    private async Task<decimal> PaidAmountAsync(Guid bookingId, CancellationToken cancellationToken) =>
        await db.Payments
            .Where(p => p.BookingId == bookingId && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

    public async Task<Result<BookingDetailResponse>> GetByIdForOwnerAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        // Ownership is part of the query: someone else's booking is a 404, not a 403.
        var ownerEmail = await GetUserEmailAsync(userId, cancellationToken);
        var exists = await db.Bookings
            .Include(b => b.Guest)
            .AnyAsync(b => b.Id == id && b.Guest != null &&
                (b.Guest.UserId == userId ||
                 (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email != null && b.Guest.Email.Trim().ToLower() == ownerEmail)),
                cancellationToken);

        if (!exists)
            return Error.NotFound("booking.not_found", "Booking not found");

        return await MapAsync(id, cancellationToken);
    }

    public async Task<Result<PaginatedResult<BookingListItemResponse>>> SearchAsync(BookingSearchQuery query, CancellationToken cancellationToken)
    {
        var q = db.Bookings.Include(b => b.Guest).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<BookingStatus>(query.Status, true, out var status))
            q = q.Where(b => b.Status == status);

        if (!string.IsNullOrWhiteSpace(query.PaymentStatus) && Enum.TryParse<PaymentStatus>(query.PaymentStatus, true, out var paymentStatus))
            q = q.Where(b => b.PaymentStatus == paymentStatus);

        if (!string.IsNullOrWhiteSpace(query.Source) && Enum.TryParse<BookingSource>(query.Source, true, out var source))
            q = q.Where(b => b.Source == source);

        if (query.OverdueArrivals)
        {
            var today = businessClock.Today;
            q = q.Where(b => b.Status == BookingStatus.Confirmed && b.CheckInDate < today);
        }

        // SoT 7n: same predicate the dashboard counts, so the card total always equals this list.
        if (query.OverdueCheckOuts)
        {
            var nowUtc = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
            q = BookingQueries.OverdueCheckOuts(q, nowUtc);
        }

        // Dashboard "Active bookings" card: bookings still in play (not cancelled/checked out).
        if (query.ActiveOnly)
            q = q.Where(b => b.Status == BookingStatus.Pending
                || b.Status == BookingStatus.Confirmed
                || b.Status == BookingStatus.CheckedIn);

        // Dashboard "Rooms freeing today" card: stays scheduled to check out today.
        if (query.CheckOutToday)
        {
            var today = businessClock.Today;
            q = q.Where(b => b.CheckOutDate == today
                && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn));
        }

        if (!string.IsNullOrWhiteSpace(query.Reference))
            q = q.Where(b => b.BookingReference.Contains(query.Reference));

        // 7l: free-text search over guest full name or booking reference.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.ToLowerInvariant();
            q = q.Where(b => b.BookingReference.ToLower().Contains(term)
                || (b.Guest != null && (b.Guest.FirstName.ToLower().Contains(term)
                    || b.Guest.LastName.ToLower().Contains(term)
                    || (b.Guest.FirstName + " " + b.Guest.LastName).ToLower().Contains(term))));
        }

        // 7l: only bookings with a positive outstanding balance, never cancelled.
        if (query.HasBalanceDue)
        {
            q = q.Where(b => b.Status != BookingStatus.Cancelled &&
                b.TotalAmount - (db.Payments.Where(p => p.BookingId == b.Id && p.Status == PaymentStatus.Paid)
                    .Sum(p => (decimal?)p.Amount) ?? 0m) > 0m);
        }

        if (query.From is not null)
            q = q.Where(b => b.CheckOutDate >= query.From);

        if (query.To is not null)
            q = q.Where(b => b.CheckInDate <= query.To);

        var totalCount = await q.CountAsync(cancellationToken);

        var rows = await ProjectList(q
                .ApplySort(query, Sortable, qq => qq.OrderByDescending(b => b.CreatedAt))
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToListItem).ToList();

        return new PaginatedResult<BookingListItemResponse>(items, totalCount, query.Page, query.PageSize);
    }

    public async Task<Result<PaginatedResult<BookingListItemResponse>>> ListForCustomerAsync(
        Guid userId, BookingSearchQuery query, CancellationToken cancellationToken)
    {
        // Ownership by user id, plus a fallback for orphaned anonymous guests whose email
        // matches the account (a booking made before the account existed or arriving without a
        // bearer token) so it still appears in the customer's dashboard.
        var ownerEmail = await GetUserEmailAsync(userId, cancellationToken);
        var q = db.Bookings.Include(b => b.Guest)
            .Where(b => b.Guest != null &&
                (b.Guest.UserId == userId ||
                 (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email != null && b.Guest.Email.Trim().ToLower() == ownerEmail)));

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<BookingStatus>(query.Status, true, out var status))
            q = q.Where(b => b.Status == status);

        var totalCount = await q.CountAsync(cancellationToken);

        var rows = await ProjectList(q
                .ApplySort(query, Sortable, qq => qq.OrderByDescending(b => b.CreatedAt))
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToListItem).ToList();

        return new PaginatedResult<BookingListItemResponse>(items, totalCount, query.Page, query.PageSize);
    }

    private sealed record BookingListItemProjection(
        Guid Id,
        string BookingReference,
        string GuestName,
        string? GuestEmail,
        string? GuestPhone,
        DateOnly CheckInDate,
        DateOnly CheckOutDate,
        DateTime CheckInAt,
        DateTime CheckOutAt,
        DateTime? ActualCheckInAt,
        DateTime? ActualCheckOutAt,
        string Status,
        string PaymentStatus,
        string Source,
        string? CreatedByName,
        decimal TotalAmount,
        decimal BalanceDue);

    /// <summary>
    /// Shared list projection used by every booking list. It also computes <c>balanceDue</c> (7l)
    /// as "TotalAmount − Σ Paid payments" in SQL so the client never derives the balance. The
    /// Lagos-local time strings are derived in memory by <see cref="ToListItem"/>.
    /// </summary>
    private IQueryable<BookingListItemProjection> ProjectList(IQueryable<Booking> q) =>
        q.Select(b => new BookingListItemProjection(
            b.Id, b.BookingReference,
            b.Guest != null ? (b.Guest.FirstName + " " + b.Guest.LastName).Trim() : "",
            b.Guest != null ? b.Guest.Email : null,
            b.Guest != null ? b.Guest.PhoneNumber : null,
            b.CheckInDate, b.CheckOutDate, b.CheckInAt, b.CheckOutAt, b.ActualCheckInAt, b.ActualCheckOutAt,
            b.Status.ToString(), b.PaymentStatus.ToString(), b.Source.ToString(),
            b.CreatedByUserId == null ? null : db.Users.Where(u => u.Id == b.CreatedByUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefault(),
            b.TotalAmount,
            b.TotalAmount - (db.Payments.Where(p => p.BookingId == b.Id && p.Status == PaymentStatus.Paid)
                .Sum(p => (decimal?)p.Amount) ?? 0m)));

    private BookingListItemResponse ToListItem(BookingListItemProjection p)
    {
        var offset = LagosOffset();
        var checkInAt = ResolveInstant(p.CheckInAt, p.CheckInDate, new TimeOnly(14, 0), offset);
        var checkOutAt = ResolveInstant(p.CheckOutAt, p.CheckOutDate, new TimeOnly(12, 0), offset);
        return new BookingListItemResponse(
            p.Id, p.BookingReference, p.GuestName, p.GuestEmail, p.GuestPhone,
            p.CheckInDate, p.CheckOutDate,
            StayFormat.LocalTime(checkInAt, offset), StayFormat.LocalTime(checkOutAt, offset),
            StayFormat.Iso(checkInAt, offset), StayFormat.Iso(checkOutAt, offset),
            p.ActualCheckInAt is null ? null : StayFormat.Iso(p.ActualCheckInAt.Value, offset),
            p.ActualCheckOutAt is null ? null : StayFormat.Iso(p.ActualCheckOutAt.Value, offset),
            p.Status, p.PaymentStatus, p.Source, p.CreatedByName, p.TotalAmount, p.BalanceDue);
    }

    public async Task<Result<BookingDetailResponse>> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        return await MapAsync(id, cancellationToken);
    }

    public async Task<Result<BookingDetailResponse>> AssignRoomsAsync(Guid id, RoomAssignmentRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(b => b.Guest)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledAssign)
            return cancelledAssign;

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Rooms can only be assigned to confirmed bookings (current: {booking.Status})");

        var lines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
        var lineById = lines.ToDictionary(l => l.Id);

        var assignments = request.Assignments ?? new List<RoomAssignmentLineRequest>();
        if (assignments.Count == 0)
            return Error.Validation("booking.no_assignments", "At least one room must be assigned");
        if (assignments.Select(a => a.RoomId).Distinct().Count() != assignments.Count)
            return Error.Validation("booking.duplicate_rooms", "Rooms must be unique in an assignment");

        var roomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToListAsync(cancellationToken);
        if (rooms.Count != roomIds.Count)
            return Error.Validation("booking.room_invalid", "One or more rooms do not exist");
        var roomById = rooms.ToDictionary(r => r.Id);

        var alreadyAssigned = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.ReleasedAt == null)
            .Select(ra => ra.RoomId)
            .ToListAsync(cancellationToken);

        // v1.12 (BR-007): a room is a hard conflict when another counted stay occupies it over
        // this booking's window (extended by the turnaround). Every conflicted room is reported
        // so reception can pick an alternative in one round-trip.
        var stay = await stays.FromBookingAsync(booking, cancellationToken);
        var conflictsFound = await conflicts.FindRoomConflictsAsync(
            roomIds, id, stay.CheckInAt, stay.CheckOutAt, stay.Policy.TurnaroundMinutes, cancellationToken);
        if (conflictsFound.Count > 0)
        {
            var errors = conflictsFound
                .Select(c => new RoomConflictResponse(
                    c.RoomId, c.RoomNumber, c.OccupiedUntil, c.OccupantGuestName, c.OccupantBookingReference, stay.CheckInAt))
                .ToList();
            return Error.Conflict("room.occupied_until",
                "One or more rooms are still occupied over the requested stay.")
                .WithExtensions(new Dictionary<string, object?> { ["errors"] = errors });
        }

        // v1.12: no interval conflict, but a room may not be free right now (a guest is still in
        // house). Assigning it anyway requires acknowledgeLateAvailability = true.
        if (!request.AcknowledgeLateAvailability)
        {
            LateAvailability? late = null;
            foreach (var roomId in roomIds)
            {
                var candidate = await conflicts.FindLateAvailabilityAsync(roomId, id, clock.UtcNow, cancellationToken);
                if (candidate is not null && (late is null || candidate.AvailableFrom < late.AvailableFrom))
                    late = candidate;
            }

            if (late is not null)
                return Error.Conflict("room.available_later",
                    $"Room {late.RoomNumber} is not free right now but will be available from {late.AvailableFrom:yyyy-MM-dd HH:mm} UTC.")
                    .WithExtensions(new Dictionary<string, object?>
                    {
                        ["roomId"] = late.RoomId,
                        ["roomNumber"] = late.RoomNumber,
                        ["availableFrom"] = late.AvailableFrom
                    });
        }

        var perLineCount = new Dictionary<Guid, int>();
        foreach (var assignment in assignments)
        {
            if (!lineById.TryGetValue(assignment.BookingRoomTypeId, out var line))
                return Error.Validation("booking.assignment_invalid", "Assignment references a room type not on this booking");

            var room = roomById[assignment.RoomId];
            if (room.RoomTypeId != line.RoomTypeId)
                return Error.Validation("booking.wrong_roomtype", $"Room {room.RoomNumber} does not belong to the booked room type");
            if (!room.IsActive || room.Status is RoomStatus.Maintenance or RoomStatus.Inactive)
                return Error.Conflict("booking.room_unavailable", $"Room {room.RoomNumber} is not available");
            if (alreadyAssigned.Contains(assignment.RoomId))
                return Error.Conflict("booking.room_already_assigned", $"Room {room.RoomNumber} is already assigned to this booking");

            perLineCount[assignment.BookingRoomTypeId] = perLineCount.GetValueOrDefault(assignment.BookingRoomTypeId) + 1;
            if (perLineCount[assignment.BookingRoomTypeId] > line.Quantity)
                return Error.Validation("booking.over_assigned", "Too many rooms assigned for a room type on this booking");
        }

        foreach (var assignment in assignments)
        {
            db.RoomAssignments.Add(new RoomAssignment
            {
                BookingId = booking.Id,
                BookingRoomTypeId = assignment.BookingRoomTypeId,
                RoomId = assignment.RoomId,
                AssignedAt = clock.UtcNow,
                AssignedByUserId = staffUserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.rooms_assigned", "Booking", booking.Id,
            $"Assigned {assignments.Count} room(s)"
            + (request.AcknowledgeLateAvailability ? " (late availability acknowledged)" : string.Empty),
            cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<CheckInResponse>> CheckInAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledCheckIn)
            return cancelledCheckIn;

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Only confirmed bookings can be checked in (current: {booking.Status})");

        // 7f: optional gate requiring full payment before check-in (off by default).
        if (await BoolSettingAsync("RequireFullPaymentAtCheckIn", settings.RequireFullPaymentAtCheckIn, cancellationToken))
        {
            var paid = await PaidAmountAsync(booking.Id, cancellationToken);
            if (paid < booking.TotalAmount)
                return Error.Conflict("booking.payment_required_before_checkin",
                    "This booking has an outstanding balance. Record the payment, then check the guest in.")
                    .WithExtensions(new Dictionary<string, object?>
                    {
                        ["outstandingBalance"] = booking.TotalAmount - paid
                    });
        }

        var lines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
        var assignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.ReleasedAt == null)
            .ToListAsync(cancellationToken);

        var assignedByLine = assignments
            .GroupBy(a => a.BookingRoomTypeId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var line in lines)
        {
            if (assignedByLine.GetValueOrDefault(line.Id) != line.Quantity)
                return Error.Conflict("booking.not_fully_assigned", "All rooms must be assigned before check-in");
        }

        if (assignments.Count == 0)
            return Error.Conflict("booking.not_fully_assigned", "All rooms must be assigned before check-in");

        var roomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToListAsync(cancellationToken);

        // v1.12 (BR-008): every assigned room must be physically free right now. Early arrival is
        // allowed when nothing conflicts from now until this booking's scheduled check-out.
        var stay = await stays.FromBookingAsync(booking, cancellationToken);
        var nowUtc = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);

        var blocked = new List<Room>();
        foreach (var room in rooms)
        {
            if (!await conflicts.IsRoomPhysicallyFreeNowAsync(room.Id, cancellationToken))
                blocked.Add(room);
        }

        if (blocked.Count > 0)
        {
            var details = await conflicts.FindRoomConflictsAsync(
                roomIds, id, nowUtc, stay.CheckOutAt, stay.Policy.TurnaroundMinutes, cancellationToken);
            var errors = blocked
                .Select(room =>
                {
                    var c = details.FirstOrDefault(d => d.RoomId == room.Id);
                    return new RoomConflictResponse(
                        room.Id, room.RoomNumber, c?.OccupiedUntil ?? nowUtc,
                        c?.OccupantGuestName, c?.OccupantBookingReference, nowUtc);
                })
                .ToList();
            return Error.Conflict("room.occupied_until",
                "One or more rooms are still occupied.")
                .WithExtensions(new Dictionary<string, object?> { ["errors"] = errors });
        }

        foreach (var room in rooms)
            room.Status = RoomStatus.Occupied;

        booking.Status = BookingStatus.CheckedIn;
        booking.ActualCheckInAt = nowUtc;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        // BR-036: only notify a guest who has an account.
        if (booking.Guest?.UserId is not null)
        {
            db.Notifications.Add(new Notification
            {
                GuestId = booking.GuestId,
                UserId = booking.Guest.UserId,
                Type = NotificationType.CheckInCompleted,
                Title = "Checked in",
                Message = $"Booking {booking.BookingReference} has been checked in.",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.checked_in", "Booking", booking.Id,
            $"Checked in {rooms.Count} room(s)", cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var email = booking.Guest.Email;
            var (subject, html, text) = StayNotificationEmail.BuildCheckIn(new StayNotificationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                rooms.Select(r => r.RoomNumber).ToList(),
                booking.CheckInDate,
                booking.CheckInLocalTime(LagosOffset()),
                booking.CheckOutDate,
                booking.CheckOutLocalTime(LagosOffset()),
                nowUtc));
            _ = emailSender.SendAsync(email, subject, html, text, CancellationToken.None);
        }

        return new CheckInResponse(
            booking.Id,
            booking.BookingReference,
            booking.Status.ToString(),
            nowUtc,
            rooms.Select(r => r.RoomNumber).ToList());
    }

    public async Task<Result<CheckOutResponse>> CheckOutAsync(
        Guid id, CheckOutRequest? request, Guid staffUserId, bool actorIsAdministrator, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledCheckOut)
            return cancelledCheckOut;

        if (booking.Status != BookingStatus.CheckedIn)
            return Error.Conflict("booking.invalid_state", $"Only checked-in bookings can be checked out (current: {booking.Status})");

        var nowUtc = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var scheduledUtc = booking.CheckOutAt;
        var isEarly = nowUtc < scheduledUtc;

        // v1.14: an actual late check-out is charged with the same duration-based pricing service as a
        // pre-booked extended stay. The fee is snapshotted onto the lines and folded into the total
        // before the balance gate, then settled through the normal "record payment" flow.
        if (nowUtc > scheduledUtc)
            await ApplyLateCheckOutChargeAsync(booking, nowUtc, cancellationToken);

        // 7f: optional gate blocking check-out while a balance is outstanding (off by default).
        // The balance gate is evaluated after any late-check-out fee is added.
        if (await BoolSettingAsync("RequirePaymentBeforeCheckOut", settings.RequirePaymentBeforeCheckOut, cancellationToken))
        {
            var paid = await PaidAmountAsync(booking.Id, cancellationToken);
            if (paid < booking.TotalAmount)
                return Error.Conflict("booking.payment_required_before_checkout",
                    "This booking has an outstanding balance. Record the payment, then check the guest out.")
                    .WithExtensions(new Dictionary<string, object?>
                    {
                        ["outstandingBalance"] = booking.TotalAmount - paid
                    });
        }

        string? earlyReason = null;
        string? earlyNotes = null;
        if (isEarly)
        {
            var early = request?.EarlyCheckOut;
            if (early is null || !early.GuestConsented)
                return Error.Conflict("booking.early_checkout_requires_consent",
                    "An early check-out requires the guest's recorded consent and a reason");

            if (early.Reason == EarlyCheckOutReason.Other && string.IsNullOrWhiteSpace(early.Notes))
                return Error.Validation("booking.notes_required", "Notes are required when the reason for early check-out is 'Other'");

            if (await BoolSettingAsync("EarlyCheckOutRequiresAdmin", settings.EarlyCheckOutRequiresAdmin, cancellationToken) && !actorIsAdministrator)
                return Error.Forbidden("booking.early_checkout_requires_admin",
                    "Early check-outs must be approved by an administrator");

            earlyReason = early.Reason.ToString();
            earlyNotes = string.IsNullOrWhiteSpace(early.Notes) ? null : early.Notes.Trim();
        }

        var assignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.ReleasedAt == null)
            .ToListAsync(cancellationToken);

        var roomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            assignment.ReleasedAt = clock.UtcNow;
        }

        // BR-015: check-out releases assigned rooms back to Available.
        foreach (var room in rooms)
        {
            room.Status = RoomStatus.Available;
        }

        booking.Status = BookingStatus.CheckedOut;
        booking.ActualCheckOutAt = nowUtc;
        booking.EarlyCheckOutReason = earlyReason;
        booking.EarlyCheckOutNotes = earlyNotes;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        // BR-036: only notify a guest who has an account.
        if (booking.Guest?.UserId is not null)
        {
            db.Notifications.Add(new Notification
            {
                GuestId = booking.GuestId,
                UserId = booking.Guest.UserId,
                Type = NotificationType.CheckOutCompleted,
                Title = "Checked out",
                Message = $"Booking {booking.BookingReference} has been checked out.",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var description = earlyReason is null
            ? null
            : $"Early check-out of {booking.BookingReference} — reason {earlyReason}, guest consented: yes"
              + (earlyNotes is null ? string.Empty : $" ({earlyNotes})")
              + $" (scheduled {scheduledUtc:yyyy-MM-dd HH:mm} UTC)";
        await activityLogger.LogAsync(staffUserId, "booking.checked_out", "Booking", booking.Id, description, cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var email = booking.Guest.Email;
            var (subject, html, text) = StayNotificationEmail.BuildCheckOut(new StayNotificationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                rooms.Select(r => r.RoomNumber).ToList(),
                booking.CheckInDate,
                booking.CheckInLocalTime(LagosOffset()),
                booking.CheckOutDate,
                booking.CheckOutLocalTime(LagosOffset()),
                nowUtc));
            _ = emailSender.SendAsync(email, subject, html, text, CancellationToken.None);
        }

        return new CheckOutResponse(
            booking.Id,
            booking.BookingReference,
            booking.Status.ToString(),
            nowUtc,
            rooms.Select(r => r.RoomNumber).ToList(),
            isEarly,
            scheduledUtc,
            booking.ActualCheckOutAt.Value);
    }

    /// <summary>
    /// Reprices a booking's lines for an actual (late) check-out instant using the duration-based
    /// pricing service, so extra time is charged through the same tier rules as a pre-booked
    /// extended stay. Mutates the tracked lines and booking totals; the caller persists them.
    /// </summary>
    private async Task ApplyLateCheckOutChargeAsync(
        Booking booking, DateTime actualCheckOutAt, CancellationToken cancellationToken)
    {
        var lines = await db.BookingRoomTypes
            .Where(l => l.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return;

        var policy = await stays.GetPolicyAsync(cancellationToken);
        var tiers = await pricing.GetTiersAsync(lines.Select(l => l.RoomTypeId), cancellationToken);
        var duration = actualCheckOutAt - booking.CheckInAt;

        var extraTotal = 0m;
        var grandTotal = 0m;
        foreach (var line in lines)
        {
            var priced = StayPricing.ComputeLine(
                line.RatePerNight, line.Quantity, booking.Nights, duration, tiers[line.RoomTypeId], policy.GraceHours);
            line.NumberOfNights = priced.Nights;
            line.Subtotal = line.RatePerNight * line.Quantity * priced.Nights;
            line.ExtraCharge = priced.OverageAmount;
            line.SurchargeTier = priced.TierLabel;
            extraTotal += priced.OverageAmount;
            grandTotal += line.Subtotal + line.ExtraCharge;
        }

        booking.ExtraChargeAmount = extraTotal;
        booking.TotalAmount = grandTotal;
        booking.PaymentStatus = await RecomputePaymentStatusAsync(booking, cancellationToken);
    }

    private async Task<PaymentStatus> RecomputePaymentStatusAsync(Booking booking, CancellationToken cancellationToken)
    {
        var paid = await PaidAmountAsync(booking.Id, cancellationToken);
        return paid <= 0
            ? PaymentStatus.Pending
            : paid >= booking.TotalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
    }

    public async Task<Result<LateCheckOutQuote>> QuoteLateCheckOutAsync(
        Guid id, DateTime? proposedCheckOutUtc, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");
        if (booking.Status != BookingStatus.CheckedIn)
            return Error.Conflict("booking.invalid_state", "Only checked-in bookings can be quoted for check-out");

        var lines = await db.BookingRoomTypes
            .Where(l => l.BookingId == id)
            .ToListAsync(cancellationToken);
        var roomTypeNames = await db.RoomTypes
            .Where(rt => lines.Select(l => l.RoomTypeId).Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt.Name, cancellationToken);

        var at = proposedCheckOutUtc.HasValue
            ? DateTime.SpecifyKind(proposedCheckOutUtc.Value, DateTimeKind.Utc)
            : DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var scheduledUtc = booking.CheckOutAt;
        var isLate = at > scheduledUtc;
        var effective = isLate ? at : scheduledUtc;
        var duration = effective - booking.CheckInAt;

        var policy = await stays.GetPolicyAsync(cancellationToken);
        var tiers = await pricing.GetTiersAsync(lines.Select(l => l.RoomTypeId), cancellationToken);

        var quoteLines = new List<LateCheckOutQuoteLine>(lines.Count);
        var projectedTotal = 0m;
        foreach (var line in lines)
        {
            var priced = StayPricing.ComputeLine(
                line.RatePerNight, line.Quantity, booking.Nights, duration, tiers[line.RoomTypeId], policy.GraceHours);
            var lineTotal = line.RatePerNight * line.Quantity * priced.Nights + priced.OverageAmount;
            projectedTotal += lineTotal;
            quoteLines.Add(new LateCheckOutQuoteLine(
                line.Id,
                roomTypeNames.TryGetValue(line.RoomTypeId, out var name) ? name : string.Empty,
                line.Quantity,
                line.RatePerNight,
                priced.Nights,
                priced.OverageMinutes,
                priced.TierLabel,
                priced.OverageAmount));
        }

        var additional = projectedTotal - booking.TotalAmount;
        if (additional < 0)
            additional = 0m;

        return new LateCheckOutQuote(
            booking.Id,
            booking.BookingReference,
            scheduledUtc,
            at,
            isLate,
            isLate ? (int)Math.Floor((at - scheduledUtc).TotalMinutes) : 0,
            quoteLines,
            additional,
            booking.TotalAmount,
            booking.TotalAmount + additional);
    }

    public async Task<Result<ChangeRoomTypeResponse>> ChangeRoomTypeAsync(
        Guid id, ChangeRoomTypeRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledChange)
            return cancelledChange;

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Error.Conflict("booking.invalid_state",
                $"Room types can only be changed before check-in (current: {booking.Status})");

        if (request.Quantity < 1)
            return Error.Validation("booking.invalid_quantity", "Quantity must be at least 1");

        var lines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
        var oldLine = lines.FirstOrDefault(l => l.Id == request.BookingRoomTypeId);
        if (oldLine is null)
            return Error.Validation("booking.line_invalid", "The room type line does not belong to this booking");

        if (request.Quantity > oldLine.Quantity)
            return Error.Validation("booking.invalid_quantity", "Cannot change more rooms than the line holds");

        if (request.NewRoomTypeId == oldLine.RoomTypeId)
            return Error.Validation("booking.roomtype_invalid", "Choose a different room type for the change");

        var involvedRoomTypeIds = new[] { oldLine.RoomTypeId, request.NewRoomTypeId }.Distinct().OrderBy(x => x).ToList();

        var paidBefore = await PaidAmountAsync(id, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize concurrent changes: lock the involved RoomType rows by ascending Id so two
        // bookings changing each other's type cannot deadlock a Postgres row lock.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"RoomTypes\" WHERE \"Id\" = ANY({0}) ORDER BY \"Id\" FOR UPDATE",
            new object[] { involvedRoomTypeIds }, cancellationToken);

        var involvedTypes = await db.RoomTypes.Where(rt => involvedRoomTypeIds.Contains(rt.Id)).ToListAsync(cancellationToken);
        var newType = involvedTypes.FirstOrDefault(rt => rt.Id == request.NewRoomTypeId);
        if (newType is null || !newType.IsActive)
            return Error.Validation("booking.roomtype_invalid", "The new room type is invalid or inactive");

        // Availability for the new type over the stay, EXCLUDING this booking's own quantities:
        // capacity - held by other overlapping bookings - this booking's own rooms already on that type.
        var capacity = await db.Rooms.CountAsync(
            r => r.RoomTypeId == newType.Id && r.IsActive && r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive,
            cancellationToken);
        var stay = await stays.FromBookingAsync(booking, cancellationToken);
        var heldByOthers = await conflicts.CountOverlappingQuantityAsync(
            newType.Id, id, stay.CheckInAt, stay.CheckOutAt, stay.Policy.TurnaroundMinutes, cancellationToken);
        var ownOnNewType = lines.Where(l => l.RoomTypeId == newType.Id).Sum(l => l.Quantity);
        var free = capacity - heldByOthers - ownOnNewType;
        if (free < request.Quantity)
            return Error.Conflict("booking.unavailable",
                $"Only {Math.Max(0, free)} room(s) of '{newType.Name}' are available for these dates");

        // Project the new total BEFORE mutating so the paid-exceeds-new-total check can run. The
        // stay's duration and the surcharge tiers drive the price (v1.14), never the calendar nights.
        var tiers = await pricing.GetTiersAsync(
            new[] { oldLine.RoomTypeId, newType.Id }, cancellationToken);
        var graceHours = stay.Policy.GraceHours;
        var duration = stay.Duration;

        var baseRate = newType.BasePrice;
        var newPriced = StayPricing.ComputeLine(baseRate, request.Quantity, stay.Nights, duration, tiers[newType.Id], graceHours);
        var lineSubtotal = baseRate * request.Quantity * newPriced.Nights;

        var keepQty = oldLine.Quantity - request.Quantity;
        var keepPriced = keepQty > 0
            ? StayPricing.ComputeLine(oldLine.RatePerNight, keepQty, stay.Nights, duration, tiers[oldLine.RoomTypeId], graceHours)
            : null;

        var projectedTotal = lines.Where(l => l.Id != oldLine.Id).Sum(l => l.Subtotal + l.ExtraCharge);
        if (keepPriced is not null)
            projectedTotal += oldLine.RatePerNight * keepQty * keepPriced.Nights + keepPriced.OverageAmount;
        projectedTotal += lineSubtotal + newPriced.OverageAmount;

        var projectedExtra = lines.Where(l => l.Id != oldLine.Id).Sum(l => l.ExtraCharge);
        if (keepPriced is not null)
            projectedExtra += keepPriced.OverageAmount;
        projectedExtra += newPriced.OverageAmount;

        if (paidBefore > projectedTotal)
            return Error.Conflict("booking.paid_exceeds_new_total",
                $"Guests have already paid {paidBefore:N2}, which exceeds the new total of {projectedTotal:N2}");

        // Merge into an existing line only when its rate equals the new type's current BasePrice.
        var mergeTarget = lines.FirstOrDefault(l =>
            l.Id != oldLine.Id && l.RoomTypeId == newType.Id && l.RatePerNight == baseRate);

        if (mergeTarget is not null)
        {
            mergeTarget.Quantity += request.Quantity;
            var merged = StayPricing.ComputeLine(
                mergeTarget.RatePerNight, mergeTarget.Quantity, stay.Nights, duration, tiers[newType.Id], graceHours);
            mergeTarget.NumberOfNights = merged.Nights;
            mergeTarget.Subtotal = mergeTarget.RatePerNight * mergeTarget.Quantity * merged.Nights;
            mergeTarget.ExtraCharge = merged.OverageAmount;
            mergeTarget.SurchargeTier = merged.TierLabel;
        }
        else
        {
            db.BookingRoomTypes.Add(new BookingRoomType
            {
                BookingId = id,
                RoomTypeId = newType.Id,
                Quantity = request.Quantity,
                RatePerNight = baseRate,
                NumberOfNights = newPriced.Nights,
                Subtotal = lineSubtotal,
                ExtraCharge = newPriced.OverageAmount,
                SurchargeTier = newPriced.TierLabel
            });
        }

        oldLine.Quantity -= request.Quantity;
        if (oldLine.Quantity == 0)
        {
            db.BookingRoomTypes.Remove(oldLine);
        }
        else
        {
            oldLine.NumberOfNights = keepPriced!.Nights;
            oldLine.Subtotal = oldLine.RatePerNight * oldLine.Quantity * keepPriced.Nights;
            oldLine.ExtraCharge = keepPriced.OverageAmount;
            oldLine.SurchargeTier = keepPriced.TierLabel;
        }

        // Release up to the moved quantity of live assignments belonging to the old line.
        var clearedAssignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.BookingRoomTypeId == oldLine.Id && ra.ReleasedAt == null)
            .OrderBy(ra => ra.AssignedAt)
            .Take(request.Quantity)
            .ToListAsync(cancellationToken);

        var clearedRoomIds = clearedAssignments.Select(a => a.RoomId).ToList();
        foreach (var assignment in clearedAssignments)
        {
            assignment.ReleasedAt = clock.UtcNow;
        }

        var clearedNumbers = await db.Rooms
            .Where(r => clearedRoomIds.Contains(r.Id))
            .Select(r => r.RoomNumber)
            .ToListAsync(cancellationToken);

        booking.TotalAmount = projectedTotal;
        booking.ExtraChargeAmount = projectedExtra;
        booking.PaymentStatus = paidBefore <= 0
            ? PaymentStatus.Pending
            : paidBefore >= projectedTotal ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.room_type_changed", "Booking", booking.Id,
            $"Changed room type line {oldLine.Id}: {request.Quantity} x '{newType.Name}' for {booking.BookingReference}",
            cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            try
            {
                var updatedLines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
                var updatedTypeIds = updatedLines.Select(l => l.RoomTypeId).ToList();
                var updatedTypeNames = await db.RoomTypes.Where(rt => updatedTypeIds.Contains(rt.Id)).ToDictionaryAsync(rt => rt.Id, rt => rt.Name, cancellationToken);
                var roomsForEmail = updatedLines.Select(l => new BookingConfirmationRoom(
                    updatedTypeNames.GetValueOrDefault(l.RoomTypeId, ""), l.Quantity, l.RatePerNight, l.Subtotal, l.ExtraCharge)).ToList();

                var (subject, html, text) = BookingUpdatedEmail.Build(new BookingUpdatedModel(
                    booking.BookingReference,
                    $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                    booking.CheckInDate,
                    booking.CheckInLocalTime(LagosOffset()),
                    booking.CheckOutDate,
                    booking.CheckOutLocalTime(LagosOffset()),
                    booking.Nights,
                    roomsForEmail,
                    booking.TotalAmount));
                _ = emailSender.SendAsync(booking.Guest.Email, subject, html, text, CancellationToken.None);
            }
            catch
            {
                // A notification (email) failure must never fail the room type change.
            }
        }

        return new ChangeRoomTypeResponse(await MapAsync(id, cancellationToken), clearedNumbers);
    }

    public async Task<Result<BookingDetailResponse>> ReassignRoomAsync(
        Guid id, ReassignRoomRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledReassign)
            return cancelledReassign;

        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.CheckedIn))
            return Error.Conflict("booking.invalid_state",
                $"Rooms can only be reassigned before the guest departs (current: {booking.Status})");

        var assignment = await db.RoomAssignments
            .FirstOrDefaultAsync(ra => ra.Id == request.RoomAssignmentId && ra.BookingId == id && ra.ReleasedAt == null, cancellationToken);
        if (assignment is null)
            return Error.NotFound("booking.assignment_not_found",
                "The room assignment does not belong to this booking or has already been released");

        var line = await db.BookingRoomTypes.FirstOrDefaultAsync(l => l.Id == assignment.BookingRoomTypeId, cancellationToken);
        if (line is null)
            return Error.Validation("booking.line_invalid", "The assignment's room type line no longer exists");

        var newRoom = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.NewRoomId, cancellationToken);
        if (newRoom is null)
            return Error.Validation("booking.roomtype_invalid", "The selected room does not exist");

        if (newRoom.RoomTypeId != line.RoomTypeId)
            return Error.Validation("booking.wrong_roomtype",
                $"Room {newRoom.RoomNumber} does not belong to the booked room type");

        if (!newRoom.IsActive || newRoom.Status is RoomStatus.Maintenance or RoomStatus.Inactive)
            return Error.Conflict("booking.room_unavailable", $"Room {newRoom.RoomNumber} is not available");

        if (request.NewRoomId == assignment.RoomId)
            return Error.Validation("booking.roomtype_invalid", $"Room {newRoom.RoomNumber} is already assigned to this booking");

        var alreadyAssigned = await db.RoomAssignments.AnyAsync(
            ra => ra.BookingId == id && ra.ReleasedAt == null && ra.RoomId == request.NewRoomId, cancellationToken);
        if (alreadyAssigned)
            return Error.Conflict("booking.room_unavailable", $"Room {newRoom.RoomNumber} is already assigned to this booking");

        // BR-023 / v1.12: the target room must be free for the whole stay (turnaround-aware).
        var stay = await stays.FromBookingAsync(booking, cancellationToken);
        var conflictsFound = await conflicts.FindRoomConflictsAsync(
            new[] { request.NewRoomId }, id, stay.CheckInAt, stay.CheckOutAt, stay.Policy.TurnaroundMinutes, cancellationToken);
        if (conflictsFound.Count > 0)
        {
            var errors = conflictsFound
                .Select(c => new RoomConflictResponse(
                    c.RoomId, c.RoomNumber, c.OccupiedUntil, c.OccupantGuestName, c.OccupantBookingReference, stay.CheckInAt))
                .ToList();
            return Error.Conflict("room.occupied_until",
                $"Room {newRoom.RoomNumber} is still occupied over the requested stay.")
                .WithExtensions(new Dictionary<string, object?> { ["errors"] = errors });
        }

        if (!request.AcknowledgeLateAvailability)
        {
            var late = await conflicts.FindLateAvailabilityAsync(request.NewRoomId, id, clock.UtcNow, cancellationToken);
            if (late is not null)
            {
                return Error.Conflict("room.available_later",
                    $"Room {late.RoomNumber} is not free right now but will be available from {late.AvailableFrom:yyyy-MM-dd HH:mm} UTC.")
                    .WithExtensions(new Dictionary<string, object?>
                    {
                        ["roomId"] = late.RoomId,
                        ["roomNumber"] = late.RoomNumber,
                        ["availableFrom"] = late.AvailableFrom
                    });
            }
        }

        var oldRoom = await db.Rooms.FirstOrDefaultAsync(r => r.Id == assignment.RoomId, cancellationToken);

        assignment.ReleasedAt = clock.UtcNow;
        db.RoomAssignments.Add(new RoomAssignment
        {
            BookingId = id,
            BookingRoomTypeId = line.Id,
            RoomId = newRoom.Id,
            AssignedAt = clock.UtcNow,
            AssignedByUserId = staffUserId
        });

        if (booking.Status == BookingStatus.CheckedIn)
        {
            if (oldRoom is not null)
                oldRoom.Status = RoomStatus.Available;
            newRoom.Status = RoomStatus.Occupied;
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.room_reassigned", "Booking", booking.Id,
            $"Reassigned room {oldRoom?.RoomNumber} to {newRoom.RoomNumber}"
            + (string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $" ({request.Reason.Trim()})")
            + (request.AcknowledgeLateAvailability ? " (late availability acknowledged)" : string.Empty),
            cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingDetailResponse>> UnassignRoomAsync(
        Guid id, UnassignRoomRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (BookingGuards.EnsureMutable(booking.Status) is { } cancelledUnassign)
            return cancelledUnassign;

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Error.Conflict("booking.invalid_state",
                $"Rooms can only be unassigned before check-in (current: {booking.Status})");

        var assignment = await db.RoomAssignments
            .FirstOrDefaultAsync(ra => ra.Id == request.RoomAssignmentId && ra.BookingId == id && ra.ReleasedAt == null, cancellationToken);
        if (assignment is null)
            return Error.NotFound("booking.assignment_not_found",
                "The room assignment does not belong to this booking or has already been released");

        assignment.ReleasedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.room_unassigned", "Booking", booking.Id, null, cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    private async Task<BookingDetailResponse> MapAsync(Guid id, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(b => b.Guest)
            .FirstAsync(b => b.Id == id, cancellationToken);

        var lines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
        var roomTypeIds = lines.Select(l => l.RoomTypeId).ToList();
        var roomTypes = await db.RoomTypes.Where(rt => roomTypeIds.Contains(rt.Id)).ToListAsync(cancellationToken);
        var roomTypeNameById = roomTypes.ToDictionary(rt => rt.Id, rt => rt.Name);
        var lineRoomTypeById = lines.ToDictionary(l => l.Id, l => l.RoomTypeId);

        var assignments = await db.RoomAssignments.Where(a => a.BookingId == id && a.ReleasedAt == null).ToListAsync(cancellationToken);
        var assignedRoomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => assignedRoomIds.Contains(r.Id)).ToListAsync(cancellationToken);
        var roomById = rooms.ToDictionary(r => r.Id, r => r.RoomNumber);

        var paid = await db.Payments
            .Where(p => p.BookingId == id && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // SoT 7n / BR-042: a cancelled booking without payments is auto-purged after the retention
        // window; one with payments is exempt and flagged for a refund review.
        var hasPayments = await db.Payments.AnyAsync(p => p.BookingId == id, cancellationToken);
        var purgeScheduledAt = booking.Status == BookingStatus.Cancelled && !hasPayments && booking.CancelledAt is not null
            ? booking.CancelledAt.Value.AddDays(retention.Value.CancelledDays)
            : (DateTime?)null;

        var offset = LagosOffset();
        var checkInAt = ResolveInstant(booking.CheckInAt, booking.CheckInDate, new TimeOnly(14, 0), offset);
        var checkOutAt = ResolveInstant(booking.CheckOutAt, booking.CheckOutDate, new TimeOnly(12, 0), offset);

        string? createdByName = null;
        if (booking.CreatedByUserId is not null)
        {
            createdByName = await db.Users
                .Where(u => u.Id == booking.CreatedByUserId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(createdByName))
                createdByName = null;
        }

        return new BookingDetailResponse(
            booking.Id,
            booking.BookingReference,
            booking.Guest is not null ? $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim() : "",
            booking.Guest?.Email,
            booking.Guest?.PhoneNumber,
            booking.CheckInDate,
            booking.CheckOutDate,
            StayFormat.LocalTime(checkInAt, offset),
            StayFormat.LocalTime(checkOutAt, offset),
            StayFormat.Iso(checkInAt, offset),
            StayFormat.Iso(checkOutAt, offset),
            booking.ActualCheckInAt is null ? null : StayFormat.Iso(booking.ActualCheckInAt.Value, offset),
            booking.ActualCheckOutAt is null ? null : StayFormat.Iso(booking.ActualCheckOutAt.Value, offset),
            booking.Nights,
            booking.Adults,
            booking.Children,
            booking.SpecialRequests,
            booking.Status.ToString(),
            booking.PaymentStatus.ToString(),
            booking.Source.ToString(),
            createdByName,
            booking.TotalAmount,
            lines.Select(l => new BookingRoomTypeLineResponse(
                l.Id,
                l.RoomTypeId,
                roomTypeNameById.GetValueOrDefault(l.RoomTypeId, ""),
                l.Quantity,
                l.RatePerNight,
                l.NumberOfNights,
                l.Subtotal,
                l.ExtraCharge)).ToList(),
            assignments.Select(a => new RoomAssignmentResponse(
                a.Id,
                a.RoomId,
                roomById.GetValueOrDefault(a.RoomId, ""),
                a.BookingRoomTypeId,
                lineRoomTypeById.GetValueOrDefault(a.BookingRoomTypeId, Guid.Empty),
                a.AssignedAt,
                a.ReleasedAt)).ToList(),
            new PaymentSummary(paid, Math.Max(0, booking.TotalAmount - paid)),
            booking.CreatedAt,
            booking.CancelledAt,
            purgeScheduledAt,
            hasPayments,
            booking.ExtraChargeAmount);
    }

    /// <summary>
    /// SoT 7n / BR-042: an administrator can restore a cancelled booking (undo the cancellation)
    /// as long as it has not yet been auto-purged. Rooms are not re-assigned; the booking returns
    /// to Confirmed so the desk can re-plan.
    /// </summary>
    public async Task<Result<BookingDetailResponse>> RestoreAsync(Guid id, Guid adminUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.Cancelled)
            return Error.Conflict("booking.not_cancelled", $"Only cancelled bookings can be restored (current: {booking.Status})");

        booking.Status = BookingStatus.Confirmed;
        booking.CancelledAt = null;
        booking.CancellationReason = null;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "booking.restored", "Booking", booking.Id,
            $"Restored cancelled booking {booking.BookingReference}", cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }
}
