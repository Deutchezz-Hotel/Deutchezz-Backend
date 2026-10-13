using Deutchezz.Api.Authorization;
using Deutchezz.Api.Extensions;
using Deutchezz.Application.Uploads;
using Deutchezz.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Deutchezz.Api.Controllers;

[ApiController]
[Route("api/v1/uploads")]
[Authorize(Policy = Policies.AdminOnly)]
public class UploadsController(IImageUploadService imageUploadService) : ControllerBase
{
    [HttpPost("images")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return Error.Validation("upload.file_required", "Image file is required").ToActionResult();
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            return Error.Validation("upload.file_too_large", "File size must be 10MB or less").ToActionResult();
        }

        var contentType = file.ContentType.ToLowerInvariant();
        var allowed = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif" };
        if (!allowed.Contains(contentType))
        {
            return Error.Validation("upload.invalid_type", "Only JPEG, PNG, WEBP, HEIC images are allowed").ToActionResult();
        }

        await using var stream = file.OpenReadStream();
        var result = await imageUploadService.UploadImageAsync(stream, file.FileName, contentType, cancellationToken);
        return result.ToActionResult();
    }
}

