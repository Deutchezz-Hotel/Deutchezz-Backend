using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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

        var roomTypeIds = request.Rooms.Select(r => r.RoomTypeId).ToList();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await CreateInTransactionAsync(request, userId, roomTypeIds, cancellationToken);
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
        CreateBookingRequest request, Guid? userId, List<Guid> roomTypeIds, CancellationToken cancellationToken)
    {
        var nights = request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber;

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        var roomTypes = await db.RoomTypes
            .Where(rt => roomTypeIds.Contains(rt.Id) && rt.IsActive)
            .ToDictionaryAsync(rt => rt.Id, cancellationToken);

        if (roomTypes.Count != roomTypeIds.Count)
            return Error.Validation("booking.roomtype_invalid", "One or more room types are invalid or inactive");

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
            // New reservations start Pending; confirmation policy (payment/approval) is an
            // open hotel decision (Source of Truth - Open Business Decisions).
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedByUserId = userId,
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

        db.Notifications.Add(new Notification
        {
            GuestId = guest.Id,
            UserId = userId,
            Type = NotificationType.BookingConfirmed,
            Title = "Booking received",
            Message = $"Your booking {booking.BookingReference} has been received.",
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await activityLogger.LogAsync(userId, "booking.created", "Booking", booking.Id,
            $"Booking {booking.BookingReference} created for {request.Guest.Email}", cancellationToken);

        _ = emailSender.SendAsync(request.Guest.Email,
            $"Booking received {booking.BookingReference}",
            $"Your booking {booking.BookingReference} has been received. Check-in: {request.CheckInDate:yyyy-MM-dd}.",
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
                FirstName = info.FirstName,
                LastName = info.LastName,
                Email = info.Email,
                PhoneNumber = info.PhoneNumber
            };
            db.Guests.Add(created);
            return created;
        }

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

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.CheckedIn or BookingStatus.CheckedOut)
            return Error.Conflict("booking.not_cancellable", $"Booking in status '{booking.Status}' cannot be cancelled");

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
                b.Id, b.BookingReference,
                b.Guest != null ? (b.Guest.FirstName + " " + b.Guest.LastName).Trim() : "",
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

    public async Task<Result<BookingResponse>> AssignRoomsAsync(Guid id, RoomAssignmentRequest request, Guid staffUserId, CancellationToken cancellationToken)
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
            $"Assigned {assignments.Count} room(s)", cancellationToken);

        return await MapAsync(booking.Id, cancellationToken);
    }

    public async Task<Result<BookingResponse>> CheckInAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status != BookingStatus.Confirmed)
            return Error.Conflict("booking.invalid_state", $"Only confirmed bookings can be checked in (current: {booking.Status})");

        if (booking.CheckInDate > clock.Today)
            return Error.Conflict("booking.not_yet_arrivable", "Booking check-in date has not been reached");

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
            .Where(p => p.BookingId == id && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new BookingResponse(
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
            assignments.Select(a => new AssignmentResponse(
                a.RoomId,
                roomById.GetValueOrDefault(a.RoomId, ""),
                a.AssignedAt,
                a.ReleasedAt)).ToList(),
            new PaymentSummary(paid, Math.Max(0, booking.TotalAmount - paid)),
            booking.CreatedAt);
    }
}
