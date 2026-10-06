namespace Deutchezz.Application.Abstractions;

public interface IDbConflictDetector
{
    bool IsSerializationFailure(Exception exception);
}