using Deutchezz.Application.Abstractions;

namespace Deutchezz.Infrastructure.Services;

public class NpgsqlConflictDetector : IDbConflictDetector
{
    public bool IsSerializationFailure(Exception exception)
    {
        var message = exception.ToString();
        return message.Contains("40001") || message.Contains("40P01");
    }
}