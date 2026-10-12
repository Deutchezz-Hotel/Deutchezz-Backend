using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
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
    IOptions<EmailOptions> emailOptions) : IBookingService
{
    private const int MaxNights = 30;

    private static readonly Dictionary<string, Expression<Func<Booking, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = b => b.CreatedAt,
            ["checkInDate"] = b => b.CheckInDate,
            ["checkOutDate"] = b => b.CheckOutDate,
            ["bookingReference"] = b => b.BookingReference,
            ["totalAmount"] = b => b.TotalAmount,
            ["status"] = b => b.Status
        };

    public async Task<Result<CreateBookingResponse>> CreateAsync(
        CreateBookingRequest request, Guid? userId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Error.Validation("booking.idempotency_key_required", "The Idempotency-Key header is required");

        var today = clock.Today;

        if (request.CheckInDate < today)
            return Error.Validation("booking.checkin_past", "Check-in date cannot be in the past");
        if (request.CheckOutDate <= request.CheckInDate)
            return Error.Validation("booking.invalid_range", "Check-out must be after check-in");
        if (request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber > MaxNights)
            return Error.Validation("booking.too_long", $"Stay cannot exceed {MaxNights} nights");
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
                request, userId, idempotencyKey, requestHash, roomTypeIds, cancellationToken);
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

    private async Task<Result<CreateBookingResponse>> CreateInTransactionAsync(
        CreateBookingRequest request, Guid? userId, string idempotencyKey, string requestHash,
        List<Guid> roomTypeIds, CancellationToken cancellationToken)
    {
        var nights = request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SoT 7c: one transaction with SELECT ... FOR UPDATE row locks on the requested
        // RoomType rows in ascending Id order (deadlock avoidance). Concurrent bookings for
        // the same room types serialize, so the availability check cannot overbook.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"RoomTypes\" WHERE \"Id\" = ANY({0}) ORDER BY \"Id\" FOR UPDATE",
            new object[] { roomTypeIds }, cancellationToken);

        var lockedRoomTypes = await db.RoomTypes
            .Where(rt => roomTypeIds.Contains(rt.Id))
            .ToListAsync(cancellationToken);

        if (lockedRoomTypes.Count != roomTypeIds.Count || lockedRoomTypes.Any(rt => !rt.IsActive))
            return Error.Validation("booking.roomtype_invalid", "One or more room types are invalid or inactive");

        var roomTypes = lockedRoomTypes.ToDictionary(rt => rt.Id);

        foreach (var line in request.Rooms)
        {
            var capacity = await db.Rooms.CountAsync(
                r => r.RoomTypeId == line.RoomTypeId && r.IsActive
                    && r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive,
                cancellationToken);

            var booked = await db.BookingRoomTypes
                .Where(brt => brt.RoomTypeId == line.RoomTypeId)
                .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                        && b.CheckInDate < request.CheckOutDate && b.CheckOutDate > request.CheckInDate),
                    brt => brt.BookingId, b => b.Id, (brt, b) => brt.Quantity)
                .SumAsync(q => (int?)q, cancellationToken) ?? 0;

            if (capacity - booked < line.Quantity)
                return Error.Conflict("booking.unavailable",
                    $"Only {Math.Max(0, capacity - booked)} room(s) available for '{roomTypes[line.RoomTypeId].Name}' for the selected dates");
        }

        var guest = await FindOrCreateGuestAsync(request.Guest, userId, cancellationToken);

        var booking = new Booking
        {
            BookingReference = referenceGenerator.Generate(),
            GuestId = guest.Id,
            CheckInDate = request.CheckInDate,
            CheckOutDate = request.CheckOutDate,
            Nights = nights,
            Adults = request.Adults,
            Children = request.Children,
            SpecialRequests = request.SpecialRequests,
            // Pay-at-hotel (SoT v1.1): new bookings are created Confirmed with payment Pending.
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            CreatedByUserId = userId,
            IdempotencyKey = idempotencyKey,
            IdempotencyRequestHash = requestHash,
            ConcurrencyToken = Guid.NewGuid().ToByteArray()
        };

        var total = 0m;
        foreach (var line in request.Rooms)
        {
            var rate = roomTypes[line.RoomTypeId].BasePrice;
            var subtotal = rate * line.Quantity * nights;
            total += subtotal;
            db.BookingRoomTypes.Add(new BookingRoomType
            {
                BookingId = booking.Id,
                RoomTypeId = line.RoomTypeId,
                Quantity = line.Quantity,
                RatePerNight = rate,
                NumberOfNights = nights,
                Subtotal = subtotal
            });
        }
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
                Message = $"Your booking {booking.BookingReference} is confirmed. Check-in: {request.CheckInDate:yyyy-MM-dd}.",
                CreatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.created", "Booking", booking.Id,
            $"Booking {booking.BookingReference} created for {request.Guest.Email}", cancellationToken);

        var emailRooms = request.Rooms
            .Select(r => new BookingConfirmationRoom(
                roomTypes[r.RoomTypeId].Name,
                r.Quantity,
                roomTypes[r.RoomTypeId].BasePrice,
                roomTypes[r.RoomTypeId].BasePrice * r.Quantity * nights))
            .ToList();
        var (subject, html, text) = BookingConfirmationEmail.Build(new BookingConfirmationModel(
            booking.BookingReference,
            $"{request.Guest.FirstName} {request.Guest.LastName}".Trim(),
            request.CheckInDate,
            request.CheckOutDate,
            nights,
            settings.CheckInTime,
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
                request.CheckInDate,
                request.CheckOutDate,
                nights,
                emailRooms,
                total));
            await emailSender.SendAsync(deskAddress, alertSubject, alertHtml, alertText, CancellationToken.None);
        }

        var response = await MapAsync(booking.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: userId is null);
    }

    private async Task<Result<CreateBookingResponse>> ReplayOrConflictAsync(Booking existing, string requestHash, CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
            return Error.Conflict("booking.idempotency_mismatch",
                "The Idempotency-Key was already used with a different request payload");

        var response = await MapAsync(existing.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: false, Replay: true);
    }

    private static string ComputeRequestHash(CreateBookingRequest request)
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
                g => g.UserId == null && g.Email.Trim().ToLower() == normalizedEmail, cancellationToken);
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
            .FirstOrDefaultAsync(g => g.UserId == null && g.Email.Trim().ToLower() == normalizedEmail, cancellationToken);
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

        if (booking is null || booking.Guest is null ||
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
                     (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email.Trim().ToLower() == ownerEmail)),
                    cancellationToken);
        }
        else if (lookup is not null)
        {
            var reference = lookup.BookingReference?.Trim();
            var email = lookup.Email?.Trim().ToLowerInvariant();
            booking = await db.Bookings
                .Include(b => b.Guest)
                .FirstOrDefaultAsync(b => b.BookingReference == reference, cancellationToken);

            if (booking is null || booking.Guest is null ||
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

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = clock.UtcNow;
        booking.CancellationReason = reason;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        db.Notifications.Add(new Notification
        {
            GuestId = booking.GuestId,
            UserId = userId,
            Type = NotificationType.BookingCancelled,
            Title = "Booking cancelled",
            Message = refundedAmount > 0
                ? $"Booking {booking.BookingReference} has been cancelled. {refundedAmount} was refunded."
                : $"Booking {booking.BookingReference} has been cancelled.",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.cancelled", "Booking", booking.Id,
            refundedAmount > 0 ? $"{reason ?? "Booking cancelled"} · refunded {refundedAmount}" : reason ?? "Booking cancelled",
            cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var (subject, html, text) = BookingCancellationEmail.Build(new BookingCancellationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                booking.CheckInDate,
                booking.CheckOutDate,
                reason,
                refundedAmount > 0 ? refundedAmount : null));
            _ = emailSender.SendAsync(booking.Guest.Email, subject, html, text, CancellationToken.None);
        }

        return await MapAsync(booking.Id, cancellationToken);
    }

    /// <summary>Whether <paramref name="booking"/> is currently before the free-cancellation
    /// deadline (BR-024): now &lt; check-in day start in Africa/Lagos minus the cancellation window.</summary>
    private bool IsWithinCancellationWindow(Booking booking, int windowHours)
    {
        var nowUtc = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var lagosOffset = businessClock.ConvertFromUtc(new DateTimeOffset(nowUtc)).Offset;
        var checkInStartLagos = new DateTimeOffset(booking.CheckInDate.ToDateTime(TimeOnly.MinValue), lagosOffset);
        var deadlineUtc = checkInStartLagos.UtcDateTime.AddHours(-windowHours);
        return nowUtc < deadlineUtc;
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
    private async Task<bool> PaymentGateAsync(string key, bool fallback, CancellationToken cancellationToken)
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
                 (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email.Trim().ToLower() == ownerEmail)),
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

        if (!string.IsNullOrWhiteSpace(query.Reference))
            q = q.Where(b => b.BookingReference.Contains(query.Reference));

        if (query.From is not null)
            q = q.Where(b => b.CheckOutDate >= query.From);

        if (query.To is not null)
            q = q.Where(b => b.CheckInDate <= query.To);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(b => b.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new BookingListItemResponse(
                b.Id, b.BookingReference,
                b.Guest != null ? (b.Guest.FirstName + " " + b.Guest.LastName).Trim() : "",
                b.CheckInDate, b.CheckOutDate, b.Status.ToString(), b.PaymentStatus.ToString(), b.TotalAmount))
            .ToListAsync(cancellationToken);

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
                 (b.Guest.UserId == null && ownerEmail != null && b.Guest.Email.Trim().ToLower() == ownerEmail)));

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<BookingStatus>(query.Status, true, out var status))
            q = q.Where(b => b.Status == status);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(b => b.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new BookingListItemResponse(
                b.Id, b.BookingReference,
                b.Guest != null ? (b.Guest.FirstName + " " + b.Guest.LastName).Trim() : "",
                b.CheckInDate, b.CheckOutDate, b.Status.ToString(), b.PaymentStatus.ToString(), b.TotalAmount))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<BookingListItemResponse>(items, totalCount, query.Page, query.PageSize);
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

        // A room cannot be assigned to two overlapping live bookings. Look at the rooms already
        // held by another Confirmed/CheckedIn booking whose dates overlap this booking's stay.
        var conflictingRoomIds = await db.RoomAssignments
            .Where(ra => roomIds.Contains(ra.RoomId) && ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b => b.Id != id
                    && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                    && b.CheckInDate < booking.CheckOutDate && b.CheckOutDate > booking.CheckInDate),
                ra => ra.BookingId, b => b.Id, (ra, b) => ra.RoomId)
            .Distinct()
            .ToListAsync(cancellationToken);

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
            if (conflictingRoomIds.Contains(assignment.RoomId))
                return Error.Conflict("booking.room_unavailable", $"Room {room.RoomNumber} is already booked for these dates");
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
            $"Assigned {assignments.Count} room(s)", cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<CheckInResponse>> CheckInAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Only confirmed bookings can be checked in (current: {booking.Status})");

        if (booking.CheckInDate > clock.Today)
            return Error.Conflict("booking.not_yet_arrivable", "Booking check-in date has not been reached");

        // 7f: optional gate requiring full payment before check-in (off by default).
        if (await PaymentGateAsync("RequireFullPaymentAtCheckIn", settings.RequireFullPaymentAtCheckIn, cancellationToken))
        {
            var paid = await PaidAmountAsync(booking.Id, cancellationToken);
            if (paid < booking.TotalAmount)
                return Error.Conflict("booking.balance_outstanding",
                    "The outstanding balance for this booking must be settled before check-in");
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

        foreach (var room in rooms)
        {
            if (!room.IsActive || room.Status is RoomStatus.Maintenance or RoomStatus.Inactive)
                return Error.Conflict("booking.room_unavailable", $"Room {room.RoomNumber} is not available for check-in");
            room.Status = RoomStatus.Occupied;
        }

        booking.Status = BookingStatus.CheckedIn;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        db.Notifications.Add(new Notification
        {
            GuestId = booking.GuestId,
            UserId = booking.CreatedByUserId,
            Type = NotificationType.CheckInCompleted,
            Title = "Checked in",
            Message = $"Booking {booking.BookingReference} has been checked in.",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.checked_in", "Booking", booking.Id,
            $"Checked in {rooms.Count} room(s)", cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var (subject, html, text) = StayNotificationEmail.BuildCheckIn(new StayNotificationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                rooms.Select(r => r.RoomNumber).ToList(),
                clock.UtcNow));
            _ = emailSender.SendAsync(booking.Guest.Email, subject, html, text, CancellationToken.None);
        }

        return new CheckInResponse(
            booking.Id,
            booking.BookingReference,
            booking.Status.ToString(),
            clock.UtcNow,
            rooms.Select(r => r.RoomNumber).ToList());
    }

    public async Task<Result<CheckOutResponse>> CheckOutAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.CheckedIn)
            return Error.Conflict("booking.invalid_state", $"Only checked-in bookings can be checked out (current: {booking.Status})");

        // 7f: optional gate blocking check-out while a balance is outstanding (off by default).
        if (await PaymentGateAsync("RequirePaymentBeforeCheckOut", settings.RequirePaymentBeforeCheckOut, cancellationToken))
        {
            var paid = await PaidAmountAsync(booking.Id, cancellationToken);
            if (paid < booking.TotalAmount)
                return Error.Conflict("booking.balance_outstanding",
                    "The outstanding balance for this booking must be settled before check-out");
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
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        db.Notifications.Add(new Notification
        {
            GuestId = booking.GuestId,
            UserId = booking.CreatedByUserId,
            Type = NotificationType.CheckOutCompleted,
            Title = "Checked out",
            Message = $"Booking {booking.BookingReference} has been checked out.",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.checked_out", "Booking", booking.Id, null, cancellationToken);

        if (booking.Guest is not null && !string.IsNullOrWhiteSpace(booking.Guest.Email))
        {
            var (subject, html, text) = StayNotificationEmail.BuildCheckOut(new StayNotificationModel(
                booking.BookingReference,
                $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim(),
                rooms.Select(r => r.RoomNumber).ToList(),
                clock.UtcNow));
            _ = emailSender.SendAsync(booking.Guest.Email, subject, html, text, CancellationToken.None);
        }

        return new CheckOutResponse(
            booking.Id,
            booking.BookingReference,
            booking.Status.ToString(),
            clock.UtcNow,
            rooms.Select(r => r.RoomNumber).ToList());
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

        var assignments = await db.RoomAssignments.Where(a => a.BookingId == id && a.ReleasedAt == null).ToListAsync(cancellationToken);
        var assignedRoomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => assignedRoomIds.Contains(r.Id)).ToListAsync(cancellationToken);
        var roomById = rooms.ToDictionary(r => r.Id, r => r.RoomNumber);

        var paid = await db.Payments
            .Where(p => p.BookingId == id && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new BookingDetailResponse(
            booking.Id,
            booking.BookingReference,
            booking.Guest is not null ? $"{booking.Guest.FirstName} {booking.Guest.LastName}".Trim() : "",
            booking.Guest?.Email ?? "",
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.Nights,
            booking.Adults,
            booking.Children,
            booking.SpecialRequests,
            booking.Status.ToString(),
            booking.PaymentStatus.ToString(),
            booking.TotalAmount,
            lines.Select(l => new BookingRoomTypeLineResponse(
                l.Id,
                l.RoomTypeId,
                roomTypeNameById.GetValueOrDefault(l.RoomTypeId, ""),
                l.Quantity,
                l.RatePerNight,
                l.NumberOfNights,
                l.Subtotal)).ToList(),
            assignments.Select(a => new RoomAssignmentResponse(
                a.RoomId,
                roomById.GetValueOrDefault(a.RoomId, ""),
                a.AssignedAt,
                a.ReleasedAt)).ToList(),
            new PaymentSummary(paid, Math.Max(0, booking.TotalAmount - paid)),
            booking.CreatedAt);
    }
}
