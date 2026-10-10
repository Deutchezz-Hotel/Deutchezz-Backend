using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Deutchezz.Api.OpenApi;

public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste a JWT access token. Scalar sends it as: Authorization: Bearer <token>."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = scheme;

        return Task.CompletedTask;
    }
}
