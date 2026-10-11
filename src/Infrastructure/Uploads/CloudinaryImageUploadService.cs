using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Deutchezz.Application.Uploads;
using Deutchezz.Domain.Common;
using Error = Deutchezz.Domain.Common.Error;
using Microsoft.Extensions.Options;

namespace Deutchezz.Infrastructure.Uploads;

public class CloudinaryOptions
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public class CloudinaryImageUploadService : IImageUploadService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryImageUploadService(IOptions<CloudinaryOptions> options)
    {
        var opt = options.Value;
        var account = new Account(opt.CloudName, opt.ApiKey, opt.ApiSecret);
        _cloudinary = new Cloudinary(account);
    }

    public async Task<Result<UploadedImage>> UploadImageAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = "deutchezz/room-types",
                Transformation = new Transformation().Width(1600).Height(1600).Crop("limit").Quality("auto").FetchFormat("auto")
            };
            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode != System.Net.HttpStatusCode.OK || result.SecureUrl == null)
            {
                return Error.Failure("upload.failed", "Image upload failed");
            }
            return new UploadedImage(result.SecureUrl.AbsoluteUri, result.PublicId);
        }
        catch (Exception ex)
        {
            return Error.Failure("upload.error", ex.Message);
        }
    }
}


