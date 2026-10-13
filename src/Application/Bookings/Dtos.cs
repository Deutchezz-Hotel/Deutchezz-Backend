using Deutchezz.Application.Common;

namespace Deutchezz.Application.Bookings;

public record GuestInfoRequest(string FirstName, string LastName, string Email, string? PhoneNumber);

public record BookingRoomRequest(Guid RoomTypeId, int Quantity);

public record CreateBookingRequest(
    DateOnly CheckInDate,
    string? CheckInTime,
    DateOnly CheckOutDate,
    string? CheckOutTime,
    int Adults,
    int Children,
    GuestInfoRequest Guest,
    List<BookingRoomRequest> Rooms,
    string? SpecialRequests,
    bool AcknowledgeDuplicate = false);

public record ReceptionGuestRequest(string FirstName, string LastName, string PhoneNumber);

public record ReceptionCreateBookingRequest(
    DateOnly CheckInDate,
    string? CheckInTime,
    DateOnly CheckOutDate,
    string? CheckOutTime,
    int Adults,
    int Children,
    ReceptionGuestRequest Guest,
    List<BookingRoomRequest> Rooms);

public enum StaffCancellationReason
{
    GuestRequest,
    NoShow,
    BookedInError,
    Other
}

public record ReceptionCancelBookingRequest(StaffCancellationReason Reason, string? Notes, bool RefundPaidAmounts);

public record BookingLookupRequest(string BookingReference, string Email);

public record CancelBookingRequest(string? Reason);

public record AnonymousCancelBookingRequest(string BookingReference, string Email, string? Reason);

public record RoomAssignmentLineRequest(Guid BookingRoomTypeId, Guid RoomId);

public record RoomAssignmentRequest(
    List<RoomAssignmentLineRequest> Assignments,
    bool AcknowledgeLateAvailability = false);

public record CheckInRequest();

public enum EarlyCheckOutReason
{
    GuestRequest,
    Emergency,
    RoomIssue,
    Other
}

public record EarlyCheckOutInfo(EarlyCheckOutReason Reason, string? Notes, bool GuestConsented);

public record CheckOutRequest(EarlyCheckOutInfo? EarlyCheckOut);

public record BookingRoomTypeLineResponse(
    Guid Id,
    Guid RoomTypeId,
    string RoomTypeName,
    int Quantity,
    decimal RatePerNight,
    int NumberOfNights,
    decimal Subtotal);

public record RoomAssignmentResponse(
    Guid Id,
    Guid RoomId,
    string RoomNumber,
    Guid BookingRoomTypeId,
    Guid RoomTypeId,
    DateTime AssignedAt,
    DateTime? ReleasedAt);

public record PaymentSummary(decimal TotalPaid, decimal Balance);

public record BookingDetailResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    string? GuestEmail,
    string? GuestPhoneNumber,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string CheckInTime,
    string CheckOutTime,
    string CheckInAt,
    string CheckOutAt,
    string? ActualCheckInAt,
    string? ActualCheckOutAt,
    int Nights,
    int Adults,
    int Children,
    string? SpecialRequests,
    string Status,
    string PaymentStatus,
    string Source,
    string? CreatedByName,
    decimal TotalAmount,
    List<BookingRoomTypeLineResponse> Lines,
    List<RoomAssignmentResponse> Assignments,
    PaymentSummary Payments,
    DateTime CreatedAt,
    DateTime? CancelledAt = null,
    DateTime? PurgeScheduledAt = null,
    bool HasPayments = false);

public record BookingListItemResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    string? GuestEmail,
    string? GuestPhoneNumber,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string CheckInTime,
    string CheckOutTime,
    string CheckInAt,
    string CheckOutAt,
    string? ActualCheckInAt,
    string? ActualCheckOutAt,
    string Status,
    string PaymentStatus,
    string Source,
    string? CreatedByName,
    decimal TotalAmount,
    decimal BalanceDue);

public record CheckInResponse(
    Guid BookingId,
    string BookingReference,
    string Status,
    DateTime CheckedInAt,
    List<string> RoomNumbers);

public record CheckOutResponse(
    Guid BookingId,
    string BookingReference,
    string Status,
    DateTime CheckedOutAt,
    List<string> ReleasedRoomNumbers,
    bool IsEarly,
    DateTime ScheduledCheckOutAt,
    DateTime ActualCheckOutAt);

public record ChangeRoomTypeRequest(Guid BookingRoomTypeId, Guid NewRoomTypeId, int Quantity);

public record ChangeRoomTypeResponse(BookingDetailResponse Booking, List<string> AssignmentsCleared);

/// <summary>A per-room conflict reported in a 409 <c>room.occupied_until</c> response (v1.12).</summary>
public record RoomConflictResponse(
    Guid RoomId,
    string RoomNumber,
    DateTime OccupiedUntil,
    string? OccupantGuestName,
    string? OccupantBookingReference,
    DateTime RequestedCheckInAt);

public record ReassignRoomRequest(Guid RoomAssignmentId, Guid NewRoomId, string? Reason, bool AcknowledgeLateAvailability = false);

public record UnassignRoomRequest(Guid RoomAssignmentId);

public record CreateBookingResponse(
    BookingDetailResponse Booking,
    bool SuggestAccount,
    bool Replay = false);

public class BookingSearchQuery : QueryParameters
{
    private string? _status;
    private string? _paymentStatus;
    private string? _reference;
    private string? _source;
    private string? _search;

    public string? Status { get => _status; set => _status = value?.ToLowerInvariant(); }
    public string? PaymentStatus { get => _paymentStatus; set => _paymentStatus = value?.ToLowerInvariant(); }
    public string? Reference { get => _reference; set => _reference = value?.Trim(); }
    public string? Source { get => _source; set => _source = value?.ToLowerInvariant(); }

    /// <summary>Free-text search over the guest's full name or the booking reference (7l).</summary>
    public string? Search { get => _search; set => _search = value?.Trim(); }

    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Reception filter: confirmed bookings whose check-in date is before today
    /// (Africa/Lagos) — guests who have not arrived yet.</summary>
    public bool OverdueArrivals { get; set; }

    /// <summary>Reception filter (v1.12): checked-in bookings whose scheduled check-out instant
    /// is before now — guests who have overstayed.</summary>
    public bool OverdueCheckOuts { get; set; }

    /// <summary>Reception filter (7l): only bookings whose outstanding balance is positive and
    /// which are not cancelled. Backs the "record payment" combobox.</summary>
    public bool HasBalanceDue { get; set; }

    /// <summary>Admin dashboard drill-down: only active bookings (Pending/Confirmed/CheckedIn).
    /// Backs the "Active bookings" card.</summary>
    public bool ActiveOnly { get; set; }

    /// <summary>Admin dashboard drill-down: bookings whose check-out falls today.
    /// Backs the "Rooms freeing today" card.</summary>
    public bool CheckOutToday { get; set; }
}
