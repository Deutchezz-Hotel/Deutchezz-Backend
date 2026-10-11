using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Payments;

public class PaymentService(IAppDbContext db, IActivityLogger activityLogger, IEmailSender emailSender) : IPaymentService
{
    private static readonly Dictionary<string, Expression<Func<Domain.Entities.Payment, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = p => p.CreatedAt,
            ["amount"] = p => p.Amount,
            ["status"] = p => p.Status
        };
    public async Task<Result<PaymentResponse>> RecordAsync(RecordPaymentRequest request, Guid recordedByUserId, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            return Error.Validation("payment.invalid_amount", "Amount must be positive");

        if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
            return Error.Validation("payment.invalid_method", "Invalid payment method");

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null)
            return Error.NotFound("booking.not_found", "Booking not found");

        if (booking.Status is BookingStatus.Cancelled)
            return Error.Conflict("payment.booking_closed", "Cannot record a payment for a cancelled booking");

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
            Status = PaymentStatus.Paid,
            Reference = request.Reference,
            Notes = request.Notes,
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
            UserId = booking.CreatedByUserId,
            Type = NotificationType.PaymentReceived,
            Title = "Payment received",
            Message = $"A payment of {request.Amount} was recorded for booking {booking.BookingReference}.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(recordedByUserId, "payment.recorded", "Payment", payment.Id,
            $"Recorded {request.Amount} via {method} for booking {booking.BookingReference}", cancellationToken);

        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == booking.GuestId, cancellationToken);
        if (guest is not null && !string.IsNullOrWhiteSpace(guest.Email))
        {
            var (subject, html, text) = PaymentReceiptEmail.Build(new PaymentReceiptModel(
                booking.BookingReference,
                $"{guest.FirstName} {guest.LastName}".Trim(),
                payment.Amount,
                method.ToString(),
                payment.Reference,
                booking.TotalAmount,
                newPaid,
                Math.Max(0, booking.TotalAmount - newPaid),
                payment.CreatedAt));
            _ = emailSender.SendAsync(guest.Email, subject, html, text, CancellationToken.None);
        }

        return new PaymentResponse(payment.Id, payment.BookingId, payment.Amount, method.ToString(), payment.Status.ToString(), payment.Reference, payment.Notes, payment.CreatedAt, recordedByUserId);
    }

    public async Task<Result<PaginatedResult<PaymentListItemResponse>>> ListAsync(PaymentQuery query, CancellationToken cancellationToken)
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
        var page = await q
            .ApplySort(query, Sortable, qq => qq.OrderByDescending(p => p.CreatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var pageBookingIds = page.Select(p => p.BookingId).Distinct().ToList();
        var bookings = await db.Bookings
            .Where(b => pageBookingIds.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                b.BookingReference,
                GuestName = b.Guest != null ? (b.Guest.FirstName + " " + b.Guest.LastName).Trim() : ""
            })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var items = page.Select(p =>
        {
            bookings.TryGetValue(p.BookingId, out var booking);
            return new PaymentListItemResponse(
                p.Id,
                p.BookingId,
                p.Amount,
                p.Method.ToString(),
                p.Status.ToString(),
                p.CreatedAt,
                booking?.BookingReference ?? string.Empty,
                booking?.GuestName ?? string.Empty,
                p.Reference);
        }).ToList();

        return new PaginatedResult<PaymentListItemResponse>(items, total, query.Page, query.PageSize);
    }
}