using Deutchezz.Application;
using Deutchezz.Infrastructure;
using Deutchezz.Infrastructure.Data;
using Deutchezz.Infrastructure.Seeders;
using Deutchezz.Api.Extensions;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddValidatedOptions(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();