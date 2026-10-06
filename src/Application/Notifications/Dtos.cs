using Deutchezz.Application.Common;

namespace Deutchezz.Application.Notifications;

public record NotificationResponse(Guid Id, string Type, string Title, string Message, bool IsRead, DateTime CreatedAt);

public class NotificationQuery : QueryParameters
{
    public bool? IsRead { get; set; }
}