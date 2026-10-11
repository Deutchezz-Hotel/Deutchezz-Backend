using Deutchezz.Application;
using Deutchezz.Infrastructure;
using Deutchezz.Infrastructure.Data;
using Deutchezz.Infrastructure.Seeders;
using Deutchezz.Api.Extensions;
using Deutchezz.Api;
using Deutchezz.Api.Authorization;
using Deutchezz.Api.OpenApi;
using Deutchezz.Application.Common.Options;
using Deutchezz.Domain.Constants;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Administrator));
    options.AddPolicy(Policies.StaffOnly, policy => policy.RequireRole(Roles.Receptionist, Roles.Administrator));
    options.AddPolicy(Policies.CustomerOnly, policy => policy.RequireRole(Roles.Customer));
});

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection(CorsOptions.SectionName)
        .Get<CorsOptions>()?.AllowedOrigins
        ?? new[] { "http://localhost:5173", "http://localhost:3000" };

    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var problemJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Type = "https://httpstatuses.com/429",
                Detail = "Too many requests. Please try again later."
            },
            problemJsonOptions,
            contentType: "application/problem+json",
            cancellationToken);
    };

    options.AddPolicy("auth", context =>
        FixedWindowPartition(context, p => p.Auth.PermitLimit, p => p.Auth.WindowSeconds));
    options.AddPolicy("lookup", context =>
        FixedWindowPartition(context, p => p.Lookup.PermitLimit, p => p.Lookup.WindowSeconds));
    options.AddPolicy("booking-create", context =>
        FixedWindowPartition(context, p => p.BookingCreate.PermitLimit, p => p.BookingCreate.WindowSeconds));

    static RateLimitPartition<string> FixedWindowPartition(
        HttpContext context,
        Func<RateLimitingOptions, int> permitLimit,
        Func<RateLimitingOptions, int> windowSeconds)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(settings),
            Window = TimeSpan.FromSeconds(windowSeconds(settings)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
});

builder.Services.AddValidatedOptions(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

var app = builder.Build();

// Fail fast: ValidateOnStart normally runs when the host starts, which happens *after* the
// migration/seed block below. Force-validate the options now so a missing or invalid setting
// (empty connection string, Jwt:Key < 32 bytes, ...) aborts before any DB access.
app.Services.ValidateRequiredOptions();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (!app.Environment.IsProduction())
{
    // Development note: in Production the schema is created by the deployment pipeline
    // (dotnet ef database update / migration job), never on application startup.
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    await app.Services.SeedRolesAsync();
    await app.Services.SeedAdminAsync();

    if (app.Environment.IsDevelopment())
    {
        await app.Services.SeedRoomInventoryAsync();
    }
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
