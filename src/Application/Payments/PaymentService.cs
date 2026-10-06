using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Payments;

public class PaymentService(IAppDbContext db, IActivityLogger activityLogger) : IPaymentService
{
    public async Task<Result<PaymentResponse>> RecordAsync(RecordPaymentRequest request, Guid recordedByUserId, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            return Error.Validation("payment.invalid_amount", "Amount must be positive");

        if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
            return Error.Validation("payment.invalid_method", "Invalid payment method");

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.NoShow)
            return Error.Conflict("payment.booking_closed", "Cannot record a payment for a closed booking");

        var paid = await db.Payments
            .Where(p => p.BookingId == request.BookingId && p.Amount > 0)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        if (paid + request.Amount > booking.TotalAmount)
            return Error.Conflict("payment.overpayment",
                "Payment would exceed the booking total (overpayment is rejected)");

        var payment = new Domain.Entities.Payment
        {
            BookingId = booking.Id,
            Amount = request.Amount,
            Method = method,
            Reference = request.Reference,
            RecordedByUserId = recordedByUserId,
            PaidAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);

        var newPaid = paid + request.Amount;
        booking.PaymentStatus = newPaid >= booking.TotalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
        booking.ConcurrencyToken = Guid.NewGuid().ToByteArray();

        await db.SaveChangesAsync(cancellationToken);

        db.Notifications.Add(new Domain.Entities.Notification
        {
            GuestId = booking.GuestId,
            Type = NotificationType.PaymentReceived,
            Title = "Payment received",
            Message = $"A payment of {request.Amount} was recorded for booking {booking.BookingReference}.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(recordedByUserId, "payment.recorded", "Payment", payment.Id,
            $"Recorded {request.Amount} via {method} for booking {booking.BookingReference}", cancellationToken);

        return new PaymentResponse(payment.Id, payment.BookingId, payment.Amount, method.ToString(), payment.Reference, payment.CreatedAt, recordedByUserId);
    }

    public async Task<Result<PaginatedResult<PaymentResponse>>> ListAsync(PaymentQuery query, CancellationToken cancellationToken)
    {
        var q = db.Payments.AsQueryable();

        if (query.BookingId is not null)
            q = q.Where(p => p.BookingId == query.BookingId);

        if (query.From is not null)
            q = q.Where(p => DateOnly.FromDateTime(p.CreatedAt) >= query.From);

        if (query.To is not null)
            q = q.Where(p => DateOnly.FromDateTime(p.CreatedAt) <= query.To);

        if (query.GuestId is not null)
        {
            var bookingIds = await db.Bookings.Where(b => b.GuestId == query.GuestId).Select(b => b.Id).ToListAsync(cancellationToken);
            q = q.Where(p => bookingIds.Contains(p.BookingId));
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PaymentResponse(p.Id, p.BookingId, p.Amount, p.Method.ToString(), p.Reference, p.CreatedAt, p.RecordedByUserId))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<PaymentResponse>(items, total, query.PageNumber, query.PageSize);
    }
}