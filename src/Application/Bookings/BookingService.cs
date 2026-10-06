using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Deutchezz.Application.Bookings;

public class BookingService(
    IAppDbContext db,
    IBookingReferenceGenerator referenceGenerator,
    IClock clock,
    IEmailSender emailSender,
    IActivityLogger activityLogger,
    IDbConflictDetector conflictDetector) : IBookingService
{
    private const int MaxNights = 30;
    private const int MaxAttempts = 3;

    public async Task<Result<CreateBookingResponse>> CreateAsync(CreateBookingRequest request, Guid? userId, CancellationToken cancellationToken)
    {
        var today = clock.Today;

        if (request.CheckInDate < today)
            return Error.Validation("booking.checkin_past", "Check-in date cannot be in the past");
        if (request.CheckOutDate <= request.CheckInDate)
            return Error.Validation("booking.invalid_range", "Check-out must be after check-in");
        if (request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber > MaxNights)
            return Error.Validation("booking.too_long", $"Stay cannot exceed {MaxNights} nights");
        if (request.Rooms.Count == 0)
            return Error.Validation("booking.no_rooms", "At least one room type is required");
        if (request.Rooms.Any(r => r.Quantity < 1))
            return Error.Validation("booking.invalid_quantity", "Room quantity must be at least 1");
        if (request.Rooms.Select(r => r.RoomTypeId).Distinct().Count() != request.Rooms.Count)
            return Error.Validation("booking.duplicate_roomtype", "Room types must be unique within a booking");
        if (string.IsNullOrWhiteSpace(request.Guest.Email))
            return Error.Validation("booking.email_required", "Guest email is required");

        var roomTypeIds = request.Rooms.Select(r => r.RoomTypeId).ToList();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await CreateInTransactionAsync(request, userId, today, roomTypeIds, cancellationToken);
            }
            catch (Exception ex) when (conflictDetector.IsSerializationFailure(ex))
            {
                if (attempt == MaxAttempts)
                    return Error.Conflict("booking.unavailable", "The booking could not be completed due to concurrent activity. Please try again.");
                await Task.Delay(50 * attempt + Random.Shared.Next(0, 50), cancellationToken);
            }
        }

        return Error.Conflict("booking.unavailable", "The booking could not be completed due to concurrent activity. Please try again.");
    }

    private async Task<Result<CreateBookingResponse>> CreateInTransactionAsync(
        CreateBookingRequest request, Guid? userId, DateOnly today, List<Guid> roomTypeIds, CancellationToken cancellationToken)
    {
        var nights = request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber;

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        var roomTypes = await db.RoomTypes
            .Where(rt => roomTypeIds.Contains(rt.Id) && rt.IsActive && !rt.IsDeleted)
            .ToDictionaryAsync(rt => rt.Id, cancellationToken);

        if (roomTypes.Count != roomTypeIds.Count)
            return Error.Validation("booking.roomtype_invalid", "One or more room types are invalid or inactive");

        foreach (var line in request.Rooms)
        {
            var capacity = await db.Rooms.CountAsync(
                r => r.RoomTypeId == line.RoomTypeId && !r.IsDeleted && r.Status != RoomStatus.OutOfService,
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
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedByUserId = userId,
            ConcurrencyToken = Guid.NewGuid().ToByteArray()
        };

        var total = 0m;
        foreach (var line in request.Rooms)
        {
            var rate = roomTypes[line.RoomTypeId].NightlyRate;
            var lineTotal = rate * line.Quantity * nights;
            total += lineTotal;
            db.BookingRoomTypes.Add(new BookingRoomType
            {
                BookingId = booking.Id,
                RoomTypeId = line.RoomTypeId,
                Quantity = line.Quantity,
                NightlyRateSnapshot = rate,
                LineTotal = lineTotal
            });
        }
        booking.TotalAmount = total;

        db.Bookings.Add(booking);

        db.Notifications.Add(new Notification
        {
            GuestId = guest.Id,
            UserId = userId,
            Type = NotificationType.BookingConfirmation,
            Title = "Booking confirmed",
            Message = $"Your booking {booking.BookingReference} has been confirmed.",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.created", "Booking", booking.Id,
            $"Booking {booking.BookingReference} created for {request.Guest.Email}", cancellationToken);

        _ = emailSender.SendAsync(request.Guest.Email,
            $"Booking confirmation {booking.BookingReference}",
            $"Your booking {booking.BookingReference} is confirmed. Check-in: {request.CheckInDate:yyyy-MM-dd}.",
            CancellationToken.None);

        var response = await MapAsync(booking.Id, cancellationToken);
        return new CreateBookingResponse(response, SuggestAccount: userId is null);
    }

    private async Task<Guest> FindOrCreateGuestAsync(GuestInfoRequest info, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not null)
        {
            var existing = await db.Guests.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
            if (existing is not null)
                return existing;

            var created = new Guest
            {
                UserId = userId,
                FullName = info.FullName,
                Email = info.Email,
                Phone = info.Phone
            };
            db.Guests.Add(created);
            return created;
        }

        var anonymous = new Guest
        {
            FullName = info.FullName,
            Email = info.Email,
            Phone = info.Phone
        };
        db.Guests.Add(anonymous);
        return anonymous;
    }

    public async Task<Result<BookingResponse>> LookupAsync(BookingLookupRequest request, CancellationToken cancellationToken)
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

    public async Task<Result<BookingResponse>> CancelAsync(Guid? id, BookingLookupRequest? lookup, string? reason, Guid? userId, CancellationToken cancellationToken)
    {
        Booking? booking;

        if (userId is not null)
        {
            booking = await db.Bookings
                .Include(b => b.Guest)
                .FirstOrDefaultAsync(b => (id == null || b.Id == id) && b.Guest != null && b.Guest.UserId == userId, cancellationToken);
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

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.CheckedOut or BookingStatus.NoShow)
            return Error.Conflict("booking.not_cancellable", $"Booking in status '{booking.Status}' cannot be cancelled");

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = clock.UtcNow;
        booking.CancellationReason = reason;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        db.Notifications.Add(new Notification
        {
            GuestId = booking.GuestId,
            UserId = userId,
            Type = NotificationType.BookingCancelled,
            Title = "Booking cancelled",
            Message = $"Booking {booking.BookingReference} has been cancelled.",
            CreatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.cancelled", "Booking", booking.Id,
            reason ?? "Booking cancelled", cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingResponse>> GetByIdForOwnerAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        // Ownership is part of the query: someone else's booking is a 404, not a 403.
        var exists = await db.Bookings
            .Include(b => b.Guest)
            .AnyAsync(b => b.Id == id && b.Guest != null && b.Guest.UserId == userId, cancellationToken);

        if (!exists)
            return Error.NotFound("booking.not_found", "Booking not found");

        return await MapAsync(id, cancellationToken);
    }

    public async Task<Result<PaginatedResult<BookingSummaryResponse>>> SearchAsync(BookingSearchQuery query, CancellationToken cancellationToken)
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
            .OrderByDescending(b => b.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new BookingSummaryResponse(
                b.Id, b.BookingReference, b.Guest != null ? b.Guest.FullName : "",
                b.CheckInDate, b.CheckOutDate, b.Status.ToString(), b.PaymentStatus.ToString(), b.TotalAmount))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<BookingSummaryResponse>(items, totalCount, query.PageNumber, query.PageSize);
    }

    public async Task<Result<BookingResponse>> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Guest).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        return await MapAsync(id, cancellationToken);
    }

    public async Task<Result<BookingResponse>> AssignAndCheckInAsync(Guid id, CheckInRequest request, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(b => b.Guest)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Only confirmed bookings can be checked in (current: {booking.Status})");

        if (booking.CheckInDate > clock.Today)
            return Error.Conflict("booking.not_yet_arrivable", "Booking check-in date has not been reached");

        var lines = await db.BookingRoomTypes.Where(brt => brt.BookingId == id).ToListAsync(cancellationToken);
        var requestedRoomIds = request.Assignments?.RoomIds ?? new List<Guid>();

        if (requestedRoomIds.Count == 0)
            return Error.Validation("booking.no_assignments", "At least one room must be assigned");

        if (requestedRoomIds.Distinct().Count() != requestedRoomIds.Count)
            return Error.Validation("booking.duplicate_rooms", "Rooms must be unique in an assignment");

        var rooms = await db.Rooms
            .Where(r => requestedRoomIds.Contains(r.Id) && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (rooms.Count != requestedRoomIds.Count)
            return Error.Validation("booking.room_invalid", "One or more rooms do not exist");

        var roomsByType = rooms.GroupBy(r => r.RoomTypeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var line in lines)
        {
            roomsByType.TryGetValue(line.RoomTypeId, out var assignedOfType);
            assignedOfType ??= new List<Room>();

            if (assignedOfType.Count > line.Quantity)
                return Error.Validation("booking.over_assigned",
                    $"Too many rooms assigned for a room type on this booking");

            foreach (var room in assignedOfType)
            {
                if (room.Status is not (RoomStatus.Available or RoomStatus.Cleaning))
                    return Error.Conflict("booking.room_unavailable", $"Room {room.RoomNumber} is not available");
            }
        }

        // Any assigned room must belong to a room type booked on this reservation.
        var bookedTypeIds = lines.Select(l => l.RoomTypeId).ToHashSet();
        foreach (var room in rooms)
        {
            if (!bookedTypeIds.Contains(room.RoomTypeId))
                return Error.Validation("booking.wrong_roomtype",
                    $"Room {room.RoomNumber} does not belong to a booked room type");
        }

        foreach (var room in rooms)
        {
            db.RoomAssignments.Add(new RoomAssignment
            {
                BookingId = booking.Id,
                RoomId = room.Id,
                AssignedAt = clock.UtcNow,
                AssignedByUserId = staffUserId
            });
            room.Status = RoomStatus.Occupied;
        }

        booking.Status = BookingStatus.CheckedIn;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.checked_in", "Booking", booking.Id,
            $"Assigned {rooms.Count} room(s)", cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingResponse>> CheckOutAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.CheckedIn)
            return Error.Conflict("booking.invalid_state", $"Only checked-in bookings can be checked out (current: {booking.Status})");

        var assignments = await db.RoomAssignments
            .Where(ra => ra.BookingId == id && ra.ReleasedAt == null)
            .ToListAsync(cancellationToken);

        var roomIds = assignments.Select(a => a.RoomId).ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            assignment.ReleasedAt = clock.UtcNow;
        }

        foreach (var room in rooms)
        {
            room.Status = RoomStatus.Cleaning;
        }

        booking.Status = BookingStatus.CheckedOut;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.checked_out", "Booking", booking.Id, null, cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingResponse>> MarkNoShowAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Only confirmed bookings can be marked no-show (current: {booking.Status})");

        booking.Status = BookingStatus.NoShow;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "booking.no_show", "Booking", booking.Id, null, cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    private async Task<BookingResponse> MapAsync(Guid id, CancellationToken cancellationToken)
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
            .Where(p => p.BookingId == id && p.Amount > 0)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new BookingResponse(
            booking.Id,
            booking.BookingReference,
            booking.Guest?.FullName ?? "",
            booking.Guest?.Email ?? "",
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.Nights,
            booking.Status.ToString(),
            booking.PaymentStatus.ToString(),
            booking.TotalAmount,
            lines.Select(l => new BookingRoomTypeLineResponse(
                l.RoomTypeId,
                roomTypeNameById.GetValueOrDefault(l.RoomTypeId, ""),
                l.Quantity,
                l.NightlyRateSnapshot,
                l.LineTotal)).ToList(),
            assignments.Select(a => new AssignmentResponse(
                a.RoomId,
                roomById.GetValueOrDefault(a.RoomId, ""),
                a.AssignedAt,
                a.ReleasedAt)).ToList(),
            new PaymentSummary(paid, Math.Max(0, booking.TotalAmount - paid)),
            booking.CreatedAt);
    }
}