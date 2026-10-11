using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Uploads;

public record UploadedImage(string Url, string PublicId);

public interface IImageUploadService
{
    Task<Result<UploadedImage>> UploadImageAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken);
}
