using Deutchezz.Application.Common;

namespace Deutchezz.Application.Bookings;

public record GuestInfoRequest(string FullName, string Email, string? Phone);

public record BookingRoomRequest(Guid RoomTypeId, int Quantity);

public record CreateBookingRequest(
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    GuestInfoRequest Guest,
    List<BookingRoomRequest> Rooms);

public record BookingLookupRequest(string BookingReference, string Email);

public record CancelBookingRequest(string? Reason);

public record RoomAssignmentRequest(List<Guid> RoomIds);

public record CheckInRequest(RoomAssignmentRequest? Assignments);

public record CheckOutRequest();

public record BookingRoomTypeLineResponse(
    Guid RoomTypeId,
    string RoomTypeName,
    int Quantity,
    decimal NightlyRateSnapshot,
    decimal LineTotal);

public record AssignmentResponse(Guid RoomId, string RoomNumber, DateTime AssignedAt, DateTime? ReleasedAt);

public record PaymentSummary(decimal TotalPaid, decimal Balance);

public record BookingResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    string GuestEmail,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    string Status,
    string PaymentStatus,
    decimal TotalAmount,
    List<BookingRoomTypeLineResponse> Lines,
    List<AssignmentResponse> Assignments,
    PaymentSummary Payments,
    DateTime CreatedAt);

public record BookingSummaryResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string Status,
    string PaymentStatus,
    decimal TotalAmount);

public record CreateBookingResponse(
    BookingResponse Booking,
    bool SuggestAccount);

public class BookingSearchQuery : QueryParameters
{
    private string? _status;
    private string? _paymentStatus;
    private string? _reference;

    public string? Status { get => _status; set => _status = value?.ToLowerInvariant(); }
    public string? PaymentStatus { get => _paymentStatus; set => _paymentStatus = value?.ToLowerInvariant(); }
    public string? Reference { get => _reference; set => _reference = value?.Trim(); }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}