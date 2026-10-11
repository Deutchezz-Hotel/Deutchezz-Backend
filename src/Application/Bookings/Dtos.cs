using Deutchezz.Application.Common;

namespace Deutchezz.Application.Bookings;

public record GuestInfoRequest(string FirstName, string LastName, string Email, string? PhoneNumber);

public record BookingRoomRequest(Guid RoomTypeId, int Quantity);

public record CreateBookingRequest(
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Adults,
    int Children,
    GuestInfoRequest Guest,
    List<BookingRoomRequest> Rooms,
    string? SpecialRequests);

public record BookingLookupRequest(string BookingReference, string Email);

public record CancelBookingRequest(string? Reason);

public record AnonymousCancelBookingRequest(string BookingReference, string Email, string? Reason);

public record RoomAssignmentLineRequest(Guid BookingRoomTypeId, Guid RoomId);

public record RoomAssignmentRequest(List<RoomAssignmentLineRequest> Assignments);

public record CheckInRequest();

public record CheckOutRequest();

public record BookingRoomTypeLineResponse(
    Guid Id,
    Guid RoomTypeId,
    string RoomTypeName,
    int Quantity,
    decimal RatePerNight,
    int NumberOfNights,
    decimal Subtotal);

public record RoomAssignmentResponse(Guid RoomId, string RoomNumber, DateTime AssignedAt, DateTime? ReleasedAt);

public record PaymentSummary(decimal TotalPaid, decimal Balance);

public record BookingDetailResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    string GuestEmail,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    int Adults,
    int Children,
    string? SpecialRequests,
    string Status,
    string PaymentStatus,
    decimal TotalAmount,
    List<BookingRoomTypeLineResponse> Lines,
    List<RoomAssignmentResponse> Assignments,
    PaymentSummary Payments,
    DateTime CreatedAt);

public record BookingListItemResponse(
    Guid Id,
    string BookingReference,
    string GuestFullName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string Status,
    string PaymentStatus,
    decimal TotalAmount);

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
    List<string> ReleasedRoomNumbers);

public record CreateBookingResponse(
    BookingDetailResponse Booking,
    bool SuggestAccount,
    bool Replay = false);

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
