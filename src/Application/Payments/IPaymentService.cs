using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Payments;

public interface IPaymentService
{
    Task<Result<PaymentResponse>> RecordAsync(RecordPaymentRequest request, Guid recordedByUserId, CancellationToken cancellationToken);
    Task<Result<PaginatedResult<PaymentResponse>>> ListAsync(PaymentQuery query, CancellationToken cancellationToken);
}