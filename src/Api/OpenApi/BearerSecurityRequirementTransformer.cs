using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Deutchezz.Api.OpenApi;

/// <summary>
/// Attaches the "Bearer" security requirement only to operations that actually require
/// authorization, so anonymous endpoints stay unlocked in Scalar.
/// </summary>
public sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuthorization =
            metadata.OfType<IAuthorizeData>().Any() &&
            !metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

        var scheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [scheme] = Array.Empty<string>()
        });

        return Task.CompletedTask;
    }
}
